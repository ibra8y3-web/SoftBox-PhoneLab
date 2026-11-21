using Microsoft.Win32;
using SoftBox.PhoneLab.Desktop.Services;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Manages a daily Windows Task Scheduler backup of the DPAPI-protected workshop database.
/// The scheduled process copies protected bytes and never decrypts customer records.
/// </summary>
public sealed class WorkshopBackupSchedulerWindow : Window
{
    private readonly WorkshopStore _store = new();
    private readonly WorkshopBackupSchedulerService _scheduler = new();
    private readonly TextBox _sourcePath = new();
    private readonly TextBox _destinationPath = new();
    private readonly TextBox _time = new() { Text = "02:30", Width = 100 };
    private readonly TextBox _retention = new() { Text = "30", Width = 90 };
    private readonly TextBox _status = new();
    private readonly Button _configureButton;
    private readonly Button _deleteButton;
    private readonly Button _queryButton;
    private bool _busy;

    public WorkshopBackupSchedulerWindow()
    {
        Title = "SoftBox | Workshop Encrypted Backup Scheduler";
        Width = 1000;
        Height = 720;
        MinWidth = 780;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _configureButton = ButtonControl("تفعيل النسخ اليومي", "#176044", Configure_Click);
        _deleteButton = ButtonControl("حذف الجدولة", "#7A3434", Delete_Click);
        _queryButton = ButtonControl("فحص حالة الجدولة", "#14517A", Query_Click);
        Content = BuildContent();
        Loaded += async (_, _) => await RefreshDefaultsAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "نسخ قاعدة بيانات مركز الصيانة تلقائيًا",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "ينشئ Windows Task Scheduler نسخة يومية من ملف قاعدة الورشة المشفر DPAPI. تُنسخ البايتات المشفرة كما هي دون فك تشفير بيانات العملاء.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 7, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "مهم: المهمة تعمل عند تسجيل دخول حساب Windows الحالي فقط. لا تعمل بعد تسجيل الخروج، ولا تنسخ بيانات الهاتف أو البيانات الخاصة بالتطبيقات.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#F0C9A5"),
            Margin = new Thickness(0, 7, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var body = new StackPanel();
        body.Children.Add(BuildPathField("ملف قاعدة بيانات الورشة", _sourcePath, readOnly: true));
        body.Children.Add(BuildPathField("مجلد حفظ النسخ", _destinationPath, readOnly: false));
        var options = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        options.Children.Add(Field("وقت النسخ المحلي (HH:mm)", _time));
        options.Children.Add(Field("الاحتفاظ بالأيام", _retention));
        body.Children.Add(options);

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 10) };
        actions.Children.Add(_configureButton);
        actions.Children.Add(_queryButton);
        actions.Children.Add(_deleteButton);
        body.Children.Add(actions);

        _status.IsReadOnly = true;
        _status.AcceptsReturn = true;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _status.MinHeight = 170;
        _status.Background = Brush("#071625");
        _status.Foreground = Brush("#DCEBFA");
        _status.BorderBrush = Brush("#263D58");
        _status.Padding = new Thickness(12);
        _status.Text = "يتم تحميل مسار قاعدة البيانات والتحقق من حالة Windows Task Scheduler.";
        body.Children.Add(_status);

        root.Children.Add(new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        return root;
    }

    private async Task RefreshDefaultsAsync()
    {
        _sourcePath.Text = _store.DatabasePath;
        _destinationPath.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SoftBox", "WorkshopBackups");
        try
        {
            // Load workshop state so any legacy JSON is migrated to DPAPI before scheduling.
            await _store.GetSnapshotAsync();
            _status.Text = File.Exists(_store.DatabasePath)
                ? "تم تحميل قاعدة الورشة. سيتم التحقق من أنها محمية بـ DPAPI قبل إنشاء أي مهمة."
                : "لم يتم إنشاء ملف قاعدة الورشة بعد. احفظ بيانات ورشة أو أنشئ أمر صيانة أولًا، ثم فعّل الجدولة.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تحميل قاعدة الورشة بأمان: " + ex.Message;
        }
    }

    private UIElement BuildPathField(string label, TextBox input, bool readOnly)
    {
        var panel = Panel();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#7DD3FC"), FontWeight = FontWeights.SemiBold });
        input.IsReadOnly = readOnly;
        input.Background = Brush("#071625");
        input.Foreground = Brush("#DCEBFA");
        input.BorderBrush = Brush("#263D58");
        input.Padding = new Thickness(9);
        input.Margin = new Thickness(0, 7, 0, 0);
        stack.Children.Add(input);
        panel.Child = stack;
        return panel;
    }

    private async void Configure_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (!TimeOnly.TryParseExact(_time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var localTime))
        {
            ShowStatus("أدخل وقتًا صحيحًا بصيغة 24 ساعة مثل 02:30.");
            return;
        }
        if (!int.TryParse(_retention.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var retention) ||
            retention is < 1 or > 3650)
        {
            ShowStatus("مدة الاحتفاظ يجب أن تكون عدد أيام بين 1 و3650.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_destinationPath.Text))
        {
            ShowStatus("حدد مجلد حفظ النسخ الاحتياطية.");
            return;
        }

        await RunAsync("جارٍ التحقق من قاعدة الورشة وإنشاء مهمة النسخ اليومية...", async token =>
        {
            await _store.GetSnapshotAsync(token);
            await _scheduler.ConfigureDailyBackupAsync(
                _store.DatabasePath,
                _destinationPath.Text.Trim(),
                localTime,
                retention,
                token);
            return "تم تسجيل مهمة النسخ الاحتياطي اليومية داخل Windows Task Scheduler. " +
                   $"الوقت المحلي: {localTime.ToString("HH:mm", CultureInfo.InvariantCulture)}. مدة الاحتفاظ: {retention} يومًا. " +
                   "سيتم نسخ ملف DPAPI المشفر كما هو؛ لن يتم فك تشفير بيانات العملاء.";
        });
    }

    private async void Query_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync("جارٍ فحص Windows Task Scheduler...", async token =>
        {
            var result = await _scheduler.QueryDailyBackupAsync(token);
            if (result.ExitCode == 0)
                return "تم العثور على مهمة النسخ اليومية:\n" + result.StandardOutput;
            var detail = string.Join(Environment.NewLine,
                new[] { result.StandardError, result.StandardOutput }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return "لم يتم تأكيد وجود المهمة. قد تكون غير مسجلة أو أن Windows رفض الاستعلام.\n" + detail;
        });
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "سيتم حذف مهمة النسخ اليومية من Windows Task Scheduler. لن يتم حذف أي نسخة احتياطية تم إنشاؤها سابقًا. متابعة؟",
                "تأكيد حذف الجدولة", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunAsync("جارٍ حذف مهمة النسخ اليومية...", async token =>
        {
            await _scheduler.DeleteDailyBackupAsync(token);
            return "تم حذف مهمة النسخ اليومية. النسخ الاحتياطية السابقة لم يتم حذفها.";
        });
    }

    private async Task RunAsync(string busyMessage, Func<CancellationToken, Task<string>> action)
    {
        if (_busy)
            return;
        _busy = true;
        SetButtons(false);
        _status.Text = busyMessage;
        try
        {
            _status.Text = await action(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تنفيذ العملية:\n" + ex.Message;
        }
        finally
        {
            _busy = false;
            SetButtons(true);
        }
    }

    private void SetButtons(bool enabled)
    {
        _configureButton.IsEnabled = enabled;
        _queryButton.IsEnabled = enabled;
        _deleteButton.IsEnabled = enabled;
    }

    private void ShowStatus(string message) => _status.Text = message;

    private static UIElement Field(string label, Control control)
    {
        var stack = new StackPanel { Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Bottom };
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#B7C9DE"), Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(control);
        return stack;
    }

    private static Border Panel()
        => new()
        {
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10)
        };

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 140,
            Background = Brush(background),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold
        };
        button.Click += handler;
        return button;
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
