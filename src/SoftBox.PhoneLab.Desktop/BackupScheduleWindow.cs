using System.IO;
using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Backup;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Creates and manages recurring encrypted backups for a specific authorized Android device.
/// The scheduler executes while the authenticated SoftBox desktop session remains open.
/// </summary>
public sealed class BackupScheduleWindow : Window
{
    private readonly AndroidBackupSchedulerService _scheduler;
    private readonly string _serial;
    private readonly string _deviceLabel;
    private readonly ObservableCollection<AndroidBackupSchedule> _schedules = [];
    private readonly ComboBox _kind = new();
    private readonly ComboBox _weekday = new();
    private readonly TextBox _remotePath = new();
    private readonly TextBox _destination = new();
    private readonly TextBox _localTime = new();
    private readonly PasswordBox _password = new();
    private readonly ListBox _list = new();
    private readonly TextBlock _status = new();
    private readonly Button _addButton;
    private readonly Button _refreshButton;
    private readonly Button _enableButton;
    private readonly Button _deleteButton;
    private readonly Button _runNowButton;
    private bool _busy;

    public BackupScheduleWindow(
        AndroidBackupSchedulerService scheduler,
        string serial,
        string deviceLabel)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        _serial = serial;
        _deviceLabel = string.IsNullOrWhiteSpace(deviceLabel) ? Mask(serial) : deviceLabel;

        Title = "SoftBox | جدولة النسخ الاحتياطي التلقائي";
        Width = 1100;
        Height = 820;
        MinWidth = 850;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _addButton = ButtonControl("حفظ الجدولة", "#176044", async (_, _) => await AddScheduleAsync());
        _refreshButton = ButtonControl("تحديث", "#14517A", async (_, _) => await RefreshAsync());
        _enableButton = ButtonControl("إيقاف / تفعيل", "#704B20", async (_, _) => await ToggleSelectedAsync());
        _deleteButton = ButtonControl("حذف الجدولة", "#713B43", async (_, _) => await DeleteSelectedAsync());
        _runNowButton = ButtonControl("نسخ احتياطي الآن", "#145F85", async (_, _) => await RunSelectedNowAsync());

        Content = BuildContent();
        _list.SelectionChanged += (_, _) => UpdateButtons();
        _scheduler.ScheduleStatusChanged += Scheduler_StatusChanged;
        Closed += (_, _) => _scheduler.ScheduleStatusChanged -= Scheduler_StatusChanged;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "جدولة النسخ الاحتياطي التلقائي",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "DAILY • WEEKLY • ON DEVICE CONNECT • AES-256 SBX • WINDOWS DPAPI",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = $"الجهاز: {_deviceLabel} • Serial: {Mask(_serial)}",
            FontSize = 11,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 7, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "التشغيل التلقائي يتم أثناء بقاء SoftBox مفتوحًا وتسجيل دخول المستخدم. احفظ كلمة مرور قوية؛ يخزن SoftBox إعدادات الجدولة وكلمة المرور داخل ملف DPAPI مرتبط بحساب Windows الحالي، ويُنشئ نسخ SBX مشفرة. لا تشمل النسخة بيانات التطبيقات الخاصة أو أقسام النظام.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#F0C9A5"),
            Background = Brush("#211D2A"),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var form = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 10; row++)
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(form, "نوع الجدولة", 0, 0);
        _kind.ItemsSource = new[]
        {
            new ScheduleChoice(AndroidBackupScheduleKind.OnConnect, "عند توصيل الهاتف"),
            new ScheduleChoice(AndroidBackupScheduleKind.Daily, "يوميًا"),
            new ScheduleChoice(AndroidBackupScheduleKind.Weekly, "أسبوعيًا")
        };
        _kind.DisplayMemberPath = nameof(ScheduleChoice.Label);
        _kind.SelectedIndex = 0;
        StyleCombo(_kind);
        Grid.SetRow(_kind, 1);
        Grid.SetColumnSpan(_kind, 2);
        form.Children.Add(_kind);

        AddLabel(form, "يوم الأسبوع (للجدولة الأسبوعية)", 2, 0);
        _weekday.ItemsSource = Enum.GetValues<DayOfWeek>();
        _weekday.SelectedItem = DayOfWeek.Sunday;
        StyleCombo(_weekday);
        Grid.SetRow(_weekday, 3);
        Grid.SetColumn(_weekday, 0);
        form.Children.Add(_weekday);

        AddLabel(form, "الوقت المحلي (HH:mm، 24 ساعة)", 2, 1);
        ConfigureInput(_localTime);
        _localTime.Text = "02:00";
        Grid.SetRow(_localTime, 3);
        Grid.SetColumn(_localTime, 1);
        form.Children.Add(_localTime);

        AddLabel(form, "مجلد الهاتف المراد نسخه", 4, 0);
        ConfigureInput(_remotePath);
        _remotePath.Text = "/sdcard/DCIM";
        Grid.SetRow(_remotePath, 5);
        Grid.SetColumnSpan(_remotePath, 2);
        form.Children.Add(_remotePath);

        AddLabel(form, "مجلد حفظ النسخ على الكمبيوتر", 6, 0);
        ConfigureInput(_destination);
        _destination.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SoftBox Backups",
            Mask(_serial));
        Grid.SetRow(_destination, 7);
        Grid.SetColumn(_destination, 0);
        Grid.SetColumnSpan(_destination, 2);
        form.Children.Add(_destination);

        AddLabel(form, "كلمة مرور النسخ المشفرة SBX (تُحمى محليًا بواسطة DPAPI)", 8, 0);
        _password.Password = "";
        _password.Height = 36;
        _password.Margin = new Thickness(0, 0, 0, 8);
        _password.Padding = new Thickness(8);
        Grid.SetRow(_password, 9);
        Grid.SetColumnSpan(_password, 2);
        form.Children.Add(_password);

        _kind.SelectionChanged += (_, _) => UpdateScheduleFields();
        UpdateScheduleFields();

        var addPanel = new DockPanel { Margin = new Thickness(0, 4, 0, 12) };
        var note = new TextBlock
        {
            Text = "المسارات المسموح بها: /sdcard أو /storage/emulated/0 أو /storage/self/primary. اختر مجلدًا فرعيًا مثل DCIM أو Pictures أو Documents.",
            Foreground = Brush("#9FC7E8"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        addPanel.Children.Add(note);
        DockPanel.SetDock(addPanel, Dock.Top);
        root.Children.Add(addPanel);
        DockPanel.SetDock(form, Dock.Top);
        root.Children.Add(form);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(_addButton);
        actions.Children.Add(_refreshButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _list.ItemsSource = _schedules;
        _list.Background = Brush("#071625");
        _list.Foreground = Brush("#DCEBFA");
        _list.BorderBrush = Brush("#263D58");
        _list.Padding = new Thickness(9);
        _list.Margin = new Thickness(0, 0, 0, 8);
        root.Children.Add(_list);

        var management = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        management.Children.Add(_runNowButton);
        management.Children.Add(_enableButton);
        management.Children.Add(_deleteButton);
        DockPanel.SetDock(management, Dock.Bottom);
        root.Children.Add(management);

        _status.Text = "تحميل جداول النسخ الاحتياطي...";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        return root;
    }

    private async Task AddScheduleAsync()
    {
        if (_kind.SelectedItem is not ScheduleChoice choice)
            return;

        var remote = _remotePath.Text.Trim();
        var destination = _destination.Text.Trim();
        var time = _localTime.Text.Trim();
        try
        {
            _ = AndroidDeviceToolsService.ValidatePublicStoragePath(remote);
            if (!Path.IsPathFullyQualified(destination))
                throw new ArgumentException("اكتب مسار مجلد كاملًا لحفظ النسخ.");
            EncryptedBackupService.ValidatePassword(_password.Password);
            if (choice.Kind is AndroidBackupScheduleKind.Daily or AndroidBackupScheduleKind.Weekly &&
                !TimeOnly.TryParseExact(time, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
                throw new ArgumentException("الوقت يجب أن يكون بصيغة HH:mm، مثل 02:30.");

            var schedule = new AndroidBackupSchedule
            {
                DeviceSerial = _serial,
                DeviceLabel = _deviceLabel,
                RemoteDirectory = remote,
                DestinationDirectory = destination,
                Kind = choice.Kind,
                LocalTime = choice.Kind == AndroidBackupScheduleKind.OnConnect ? "00:00" : time,
                WeeklyDay = _weekday.SelectedItem is DayOfWeek day ? day : DayOfWeek.Sunday,
                BackupPassword = _password.Password,
                IsEnabled = true,
                WasConnected = false
            };

            await RunAsync("جارٍ حفظ الجدولة محليًا بشكل محمي...", async () =>
            {
                Directory.CreateDirectory(destination);
                await _scheduler.AddAsync(schedule);
                _password.Clear();
                await LoadListAsync();
                return "تم حفظ الجدولة. سيُنشئ SoftBox ملف SBX مشفرًا عند حلول موعدها مع اتصال ADB مصرح به.";
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "بيانات الجدولة غير صالحة",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ToggleSelectedAsync()
    {
        if (_list.SelectedItem is not AndroidBackupSchedule selected)
            return;
        await RunAsync("جارٍ تحديث حالة الجدولة...", async () =>
        {
            await _scheduler.SetEnabledAsync(selected.Id, !selected.IsEnabled);
            await LoadListAsync();
            return selected.IsEnabled ? "تم إيقاف الجدولة." : "تم تفعيل الجدولة.";
        });
    }

    private async Task DeleteSelectedAsync()
    {
        if (_list.SelectedItem is not AndroidBackupSchedule selected)
            return;
        var answer = MessageBox.Show(this,
            $"حذف جدولة النسخ لهذا الجهاز؟\n{selected}\n\nلن يحذف SoftBox أي ملف نسخة احتياطية تم إنشاؤه سابقًا.",
            "تأكيد حذف الجدولة", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        await RunAsync("جارٍ حذف الجدولة...", async () =>
        {
            await _scheduler.RemoveAsync(selected.Id);
            await LoadListAsync();
            return "تم حذف الجدولة فقط. ملفات النسخ التي أُنشئت سابقًا لم تتغير.";
        });
    }

    private async Task RunSelectedNowAsync()
    {
        if (_list.SelectedItem is not AndroidBackupSchedule selected)
            return;
        var answer = MessageBox.Show(this,
            $"تنفيذ نسخة احتياطية مشفرة الآن للجهاز:\n{Mask(selected.DeviceSerial)}\nالمجلد: {selected.RemoteDirectory}\n\nمتابعة؟",
            "تأكيد النسخ الاحتياطي الفوري", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes)
            return;

        await RunAsync("جارٍ جدولة نسخة احتياطية فورية...", async () =>
        {
            await _scheduler.RunNowAsync(selected.Id);
            await LoadListAsync();
            return "بدأ تنفيذ النسخة الاحتياطية في الخلفية. ستظهر النتيجة في حالة الجدولة.";
        });
    }

    private void Scheduler_StatusChanged(Guid scheduleId, string status)
    {
        if (!_schedules.Any(schedule => schedule.Id == scheduleId))
            return;
        _ = Dispatcher.BeginInvoke(new Action(() => _ = RefreshAsync()));
    }

    private async Task RefreshAsync()
        => await RunAsync("جارٍ تحميل جداول النسخ الاحتياطي...", async () =>
        {
            await LoadListAsync();
            return $"تم تحميل {_schedules.Count:N0} جدولة.";
        });

    private async Task LoadListAsync()
    {
        var all = await _scheduler.GetSchedulesAsync();
        _schedules.Clear();
        foreach (var schedule in all.Where(item =>
                     item.DeviceSerial.Equals(_serial, StringComparison.Ordinal)))
            _schedules.Add(schedule);
    }

    private async Task RunAsync(string message, Func<Task<string>> operation)
    {
        if (_busy)
            return;
        _busy = true;
        SetButtons();
        _status.Text = message;
        try
        {
            _status.Text = await operation();
        }
        catch (Exception ex)
        {
            _status.Text = "فشلت العملية: " + ex.Message;
            MessageBox.Show(this, ex.Message, "جدولة النسخ الاحتياطي",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            SetButtons();
        }
    }

    private void UpdateScheduleFields()
    {
        var dailyOrWeekly = _kind.SelectedItem is ScheduleChoice choice &&
                            choice.Kind is AndroidBackupScheduleKind.Daily or AndroidBackupScheduleKind.Weekly;
        _localTime.IsEnabled = dailyOrWeekly;
        _weekday.IsEnabled = _kind.SelectedItem is ScheduleChoice selected &&
                             selected.Kind == AndroidBackupScheduleKind.Weekly;
    }

    private void UpdateButtons()
    {
        var selected = _list.SelectedItem is AndroidBackupSchedule;
        _enableButton.IsEnabled = !_busy && selected;
        _deleteButton.IsEnabled = !_busy && selected;
        _runNowButton.IsEnabled = !_busy && selected;
    }

    private void SetButtons()
    {
        _addButton.IsEnabled = !_busy;
        _refreshButton.IsEnabled = !_busy;
        UpdateButtons();
    }

    private void AddLabel(Grid grid, string text, int row, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = Brush("#9FC7E8"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        Grid.SetColumnSpan(label, column == 0 && row is 0 or 2 or 4 or 6 or 8 ? 2 : 1);
        grid.Children.Add(label);
    }

    private void AddLabel(Grid grid, string text, int row, int column, bool unused = false)
        => AddLabel(grid, text, row, column);

    private static void StyleCombo(ComboBox combo)
    {
        combo.Height = 36;
        combo.Margin = new Thickness(0, 0, 0, 8);
        combo.Background = Brush("#0A1B2D");
        combo.Foreground = Brush("#EAF3FF");
        combo.BorderBrush = Brush("#35516F");
    }

    private static void ConfigureInput(TextBox textBox)
    {
        textBox.Height = 36;
        textBox.Margin = new Thickness(0, 0, 0, 8);
        textBox.Padding = new Thickness(9, 6, 9, 6);
        textBox.Background = Brush("#0A1B2D");
        textBox.Foreground = Brush("#EAF3FF");
        textBox.BorderBrush = Brush("#35516F");
    }

    private static string Mask(string serial)
        => serial.Length > 4 ? "••••" + serial[^4..] : "••••";

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 115,
            Background = Brush(color),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold
        };
        button.Click += handler;
        return button;
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));

    private sealed record ScheduleChoice(AndroidBackupScheduleKind Kind, string Label);
}
