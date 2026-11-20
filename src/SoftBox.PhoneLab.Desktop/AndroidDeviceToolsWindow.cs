using System.IO;
using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Apk;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Android file browsing, installed-package inspection and safe ADB command presets.
/// Device writes are limited to pushing a chosen file and creating a chosen shared-storage directory.
/// </summary>
public sealed class AndroidDeviceToolsWindow : Window
{
    private readonly AndroidDeviceToolsService _service;
    private readonly string _serial;
    private readonly IReadOnlyList<string> _authorizedSerials;
    private readonly TextBox _remoteDirectory = new();
    private readonly TextBox _remoteFile = new();
    private readonly TextBox _folderName = new();
    private readonly TextBox _fileOutput = new();
    private readonly TextBox _packageDetails = new();
    private readonly TextBox _commandOutput = new();
    private readonly TextBox _batchOutput = new();
    private readonly ListBox _packages = new();
    private readonly ListBox _batchDevices = new();
    private readonly ComboBox _commandPreset = new();
    private readonly ComboBox _batchPreset = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly ObservableCollection<string> _packageNames = [];
    private readonly ObservableCollection<string> _deviceNames = [];
    private CancellationTokenSource? _operation;
    private readonly Button _listButton;
    private readonly Button _downloadButton;
    private readonly Button _uploadButton;
    private readonly Button _mkdirButton;
    private readonly Button _packageRefreshButton;
    private readonly Button _packageDetailsButton;
    private readonly Button _installApkButton;
    private readonly Button _commandButton;
    private readonly Button _batchButton;
    private readonly Button _cancelButton;

    public AndroidDeviceToolsWindow(
        AdbClient adb,
        string serial,
        IReadOnlyList<string> authorizedSerials)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        _serial = serial;
        _authorizedSerials = authorizedSerials.Where(AdbClient.IsSafeSerial)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (!_authorizedSerials.Contains(serial, StringComparer.Ordinal))
            throw new ArgumentException("الجهاز الحالي ليس ضمن قائمة أجهزة ADB المصرّح بها.", nameof(serial));

        _service = new AndroidDeviceToolsService(adb);
        Title = $"SoftBox | أدوات Android — {_serial}";
        Width = 1080;
        Height = 790;
        MinWidth = 850;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _listButton = ButtonControl("استعراض المجلد", "#14517A", ListDirectory_Click);
        _downloadButton = ButtonControl("تنزيل الملف المحدد", "#14517A", Download_Click);
        _uploadButton = ButtonControl("رفع ملف إلى الهاتف", "#243B56", Upload_Click);
        _mkdirButton = ButtonControl("إنشاء مجلد", "#176044", CreateFolder_Click);
        _packageRefreshButton = ButtonControl("قراءة التطبيقات", "#14517A", LoadPackages_Click);
        _packageDetailsButton = ButtonControl("تفاصيل الحزمة", "#243B56", PackageDetails_Click);
        _installApkButton = ButtonControl("تثبيت / تحديث APK", "#176044", InstallApk_Click);
        _commandButton = ButtonControl("تنفيذ الأمر الآمن", "#14517A", RunCommand_Click);
        _batchButton = ButtonControl("تنفيذ على الأجهزة المحددة", "#14517A", RunBatch_Click);
        _cancelButton = ButtonControl("إلغاء العملية", "#51333A", Cancel_Click);
        _cancelButton.IsEnabled = false;

        Content = BuildContent();
        Loaded += (_, _) =>
        {
            _remoteDirectory.Text = "/sdcard";
            RefreshBatchDevices();
        };
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "مركز أدوات Android",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "AUTHORIZED ADB • FILE MANAGER • APP INSPECTOR • READ-ONLY COMMANDS • BATCH",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "الجهاز المستهدف: " + _serial + " — العمليات مقيدة بهذا المعرف. لا توجد أوامر Shell حرة أو إزالة تطبيقات أو وصول إلى بيانات التطبيقات الخاصة.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Text = "بانتظار اختيار أداة.";
        _status.Foreground = Brush("#9FC7E8");
        _status.FontSize = 11;
        footer.Children.Add(_status);
        _progress.Height = 4;
        _progress.IsIndeterminate = true;
        _progress.Visibility = Visibility.Collapsed;
        _progress.Foreground = Brush("#38BDF8");
        footer.Children.Add(_progress);
        footer.Children.Add(_cancelButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var tabs = new TabControl
        {
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58")
        };
        tabs.Items.Add(BuildFileTab());
        tabs.Items.Add(BuildPackageTab());
        tabs.Items.Add(BuildCommandTab());
        tabs.Items.Add(BuildBatchTab());
        root.Children.Add(tabs);
        return root;
    }

    private TabItem BuildFileTab()
    {
        var panel = new DockPanel { Margin = new Thickness(10) };
        var title = TextBlock("مدير ملفات Android — مساحة التخزين المشتركة التي يسمح بها النظام فقط", 12, "#B7C9DE");
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);

        var toolbar = new Grid { Margin = new Thickness(0, 10, 0, 8) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _remoteDirectory.Height = 36;
        _remoteDirectory.Padding = new Thickness(9, 6, 9, 6);
        _remoteDirectory.Background = Brush("#0A1B2D");
        _remoteDirectory.Foreground = Brush("#EAF3FF");
        _remoteDirectory.BorderBrush = Brush("#35516F");
        _remoteDirectory.Text = "/sdcard";
        Grid.SetColumn(_remoteDirectory, 0);
        toolbar.Children.Add(_remoteDirectory);
        _listButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_listButton, 1);
        toolbar.Children.Add(_listButton);
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);

        var pathPanel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        pathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        pathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _remoteFile.Height = 34;
        _remoteFile.Margin = new Thickness(0, 0, 8, 0);
        _remoteFile.Padding = new Thickness(9, 6, 9, 6);
        _remoteFile.Background = Brush("#0A1B2D");
        _remoteFile.Foreground = Brush("#EAF3FF");
        _remoteFile.BorderBrush = Brush("#35516F");
        _remoteFile.ToolTip = "المسار الكامل لملف على الهاتف لتنزيله";
        Grid.SetColumn(_remoteFile, 0);
        pathPanel.Children.Add(_remoteFile);
        _folderName.Height = 34;
        _folderName.Margin = new Thickness(0, 0, 8, 0);
        _folderName.Padding = new Thickness(9, 6, 9, 6);
        _folderName.Background = Brush("#0A1B2D");
        _folderName.Foreground = Brush("#EAF3FF");
        _folderName.BorderBrush = Brush("#35516F");
        _folderName.ToolTip = "اسم مجلد جديد داخل المسار الحالي";
        Grid.SetColumn(_folderName, 1);
        pathPanel.Children.Add(_folderName);
        _mkdirButton.Margin = new Thickness(0);
        Grid.SetColumn(_mkdirButton, 2);
        pathPanel.Children.Add(_mkdirButton);
        DockPanel.SetDock(pathPanel, Dock.Top);
        panel.Children.Add(pathPanel);

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        buttons.Children.Add(_downloadButton);
        buttons.Children.Add(_uploadButton);
        panel.Children.Add(buttons);

        _fileOutput.IsReadOnly = true;
        _fileOutput.AcceptsReturn = true;
        _fileOutput.TextWrapping = TextWrapping.NoWrap;
        _fileOutput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _fileOutput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _fileOutput.Background = Brush("#071625");
        _fileOutput.Foreground = Brush("#DCEBFA");
        _fileOutput.BorderBrush = Brush("#263D58");
        _fileOutput.Padding = new Thickness(10);
        _fileOutput.FontFamily = new FontFamily("Consolas");
        _fileOutput.FontSize = 11;
        _fileOutput.Text = "أدخل مسار مجلد مثل /sdcard/DCIM واضغط استعراض المجلد.";
        panel.Children.Add(_fileOutput);

        return new TabItem { Header = "مدير الملفات", Content = panel };
    }

    private TabItem BuildPackageTab()
    {
        var grid = new Grid { Margin = new Thickness(10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var packageActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        packageActions.Children.Add(_packageRefreshButton);
        packageActions.Children.Add(_packageDetailsButton);
        packageActions.Children.Add(_installApkButton);
        Grid.SetColumnSpan(packageActions, 2);
        grid.Children.Add(packageActions);

        _packages.ItemsSource = _packageNames;
        _packages.Background = Brush("#071625");
        _packages.Foreground = Brush("#DCEBFA");
        _packages.BorderBrush = Brush("#263D58");
        _packages.Padding = new Thickness(8);
        Grid.SetRow(_packages, 1);
        Grid.SetColumn(_packages, 0);
        grid.Children.Add(_packages);

        _packageDetails.IsReadOnly = true;
        _packageDetails.AcceptsReturn = true;
        _packageDetails.TextWrapping = TextWrapping.NoWrap;
        _packageDetails.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _packageDetails.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _packageDetails.Background = Brush("#071625");
        _packageDetails.Foreground = Brush("#DCEBFA");
        _packageDetails.BorderBrush = Brush("#263D58");
        _packageDetails.Padding = new Thickness(10);
        _packageDetails.FontFamily = new FontFamily("Consolas");
        _packageDetails.FontSize = 10;
        _packageDetails.Text = "اختر حزمة من القائمة واضغط «تفاصيل الحزمة».";
        Grid.SetRow(_packageDetails, 1);
        Grid.SetColumn(_packageDetails, 1);
        grid.Children.Add(_packageDetails);

        return new TabItem { Header = "مدير التطبيقات", Content = grid };
    }

    private TabItem BuildCommandTab()
    {
        var panel = new DockPanel { Margin = new Thickness(10) };
        var note = TextBlock("أوامر قراءة فقط محددة مسبقًا: getprop، البطارية، التخزين، uptime، التطبيقات الخارجية والخدمات. لا يمكن إدخال أمر Shell حر.", 11, "#B7C9DE");
        DockPanel.SetDock(note, Dock.Top);
        panel.Children.Add(note);

        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 8) };
        _commandPreset.ItemsSource = new[]
        {
            new PresetItem("properties", "خصائص Android — getprop"),
            new PresetItem("battery", "معلومات البطارية — dumpsys battery"),
            new PresetItem("storage", "مساحة التخزين — df"),
            new PresetItem("uptime", "وقت تشغيل النظام — uptime"),
            new PresetItem("third-party-apps", "التطبيقات الخارجية — pm list packages"),
            new PresetItem("services", "خدمات Android — service list")
        };
        _commandPreset.DisplayMemberPath = nameof(PresetItem.Label);
        _commandPreset.SelectedIndex = 0;
        DockPanel.SetDock(_commandPreset, Dock.Right);
        row.Children.Add(_commandPreset);
        _commandButton.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(_commandButton, Dock.Left);
        row.Children.Add(_commandButton);
        DockPanel.SetDock(row, Dock.Top);
        panel.Children.Add(row);

        _commandOutput.IsReadOnly = true;
        _commandOutput.AcceptsReturn = true;
        _commandOutput.TextWrapping = TextWrapping.NoWrap;
        _commandOutput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _commandOutput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _commandOutput.Background = Brush("#071625");
        _commandOutput.Foreground = Brush("#DCEBFA");
        _commandOutput.BorderBrush = Brush("#263D58");
        _commandOutput.Padding = new Thickness(10);
        _commandOutput.FontFamily = new FontFamily("Consolas");
        _commandOutput.FontSize = 10;
        panel.Children.Add(_commandOutput);
        return new TabItem { Header = "مركز الأوامر", Content = panel };
    }

    private TabItem BuildBatchTab()
    {
        var grid = new Grid { Margin = new Thickness(10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _batchPreset.ItemsSource = new[]
        {
            new PresetItem("properties", "خصائص النظام"),
            new PresetItem("battery", "معلومات البطارية"),
            new PresetItem("storage", "مساحة التخزين"),
            new PresetItem("uptime", "وقت تشغيل النظام"),
            new PresetItem("third-party-apps", "التطبيقات الخارجية"),
            new PresetItem("services", "قائمة الخدمات")
        };
        _batchPreset.DisplayMemberPath = nameof(PresetItem.Label);
        _batchPreset.SelectedIndex = 0;
        DockPanel.SetDock(_batchPreset, Dock.Right);
        controls.Children.Add(_batchPreset);
        _batchButton.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(_batchButton, Dock.Left);
        controls.Children.Add(_batchButton);
        Grid.SetColumnSpan(controls, 2);
        grid.Children.Add(controls);

        _batchDevices.ItemsSource = _deviceNames;
        _batchDevices.SelectionMode = SelectionMode.Extended;
        _batchDevices.Background = Brush("#071625");
        _batchDevices.Foreground = Brush("#DCEBFA");
        _batchDevices.BorderBrush = Brush("#263D58");
        _batchDevices.Padding = new Thickness(8);
        Grid.SetRow(_batchDevices, 1);
        Grid.SetColumn(_batchDevices, 0);
        grid.Children.Add(_batchDevices);

        _batchOutput.IsReadOnly = true;
        _batchOutput.AcceptsReturn = true;
        _batchOutput.TextWrapping = TextWrapping.NoWrap;
        _batchOutput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _batchOutput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _batchOutput.Background = Brush("#071625");
        _batchOutput.Foreground = Brush("#DCEBFA");
        _batchOutput.BorderBrush = Brush("#263D58");
        _batchOutput.Padding = new Thickness(10);
        _batchOutput.FontFamily = new FontFamily("Consolas");
        _batchOutput.FontSize = 10;
        _batchOutput.Text = "اختر عدة أجهزة مصرح بها من القائمة ثم نفّذ عملية قراءة فقط. الحد الأقصى للتوازي: 3.";
        Grid.SetRow(_batchOutput, 1);
        Grid.SetColumn(_batchOutput, 1);
        grid.Children.Add(_batchOutput);

        return new TabItem { Header = "العمليات الجماعية", Content = grid };
    }

    private async void ListDirectory_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("جارٍ استعراض مجلد الهاتف...", async token =>
        {
            var output = await _service.ListDirectoryAsync(_serial, _remoteDirectory.Text, token);
            _fileOutput.Text = output;
            return "تم جلب قائمة المجلد عبر ADB.";
        });
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_remoteFile.Text))
        {
            MessageBox.Show(this, "اكتب المسار الكامل للملف المراد تنزيله في الحقل المخصص.",
                "مدير الملفات", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "حفظ ملف من الهاتف",
            FileName = Path.GetFileName(_remoteFile.Text.Trim()),
            Filter = "All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        await RunOperationAsync("جارٍ تنزيل الملف من الهاتف...", async token =>
        {
            await _service.PullFileAsync(_serial, _remoteFile.Text.Trim(), dialog.FileName, token);
            _fileOutput.Text = "تم تنزيل الملف إلى:\n" + dialog.FileName;
            return "اكتمل تنزيل الملف.";
        });
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "اختيار ملف للرفع إلى الهاتف", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
            return;

        var confirm = MessageBox.Show(this,
            $"سيتم رفع الملف المحدد إلى {_remoteDirectory.Text.Trim()} على الجهاز {_serial}. هل توافق؟",
            "تأكيد رفع ملف", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        await RunOperationAsync("جارٍ رفع الملف المحدد إلى مساحة الهاتف المشتركة...", async token =>
        {
            await _service.PushFileAsync(_serial, dialog.FileName, _remoteDirectory.Text, token);
            _fileOutput.Text = "تم رفع الملف:\n" + dialog.FileName;
            return "اكتمل رفع الملف.";
        });
    }

    private async void CreateFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = _folderName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.Contains('/') || name.Contains('\\') || name.Any(char.IsControl))
        {
            MessageBox.Show(this, "اكتب اسم مجلد واحدًا صالحًا فقط.", "مدير الملفات",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var target = _remoteDirectory.Text.Trim().TrimEnd('/') + "/" + name;
        await RunOperationAsync("جارٍ إنشاء المجلد...", async token =>
        {
            await _service.CreateDirectoryAsync(_serial, target, token);
            _fileOutput.Text = "تم طلب إنشاء المجلد:\n" + target;
            return "تم إنشاء المجلد أو كان موجودًا بالفعل.";
        });
    }

    private async void LoadPackages_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("جارٍ قراءة أسماء التطبيقات الخارجية...", async token =>
        {
            var packages = await _service.ListThirdPartyPackagesAsync(_serial, token);
            _packageNames.Clear();
            foreach (var package in packages)
                _packageNames.Add(package.PackageName);
            _packageDetails.Text = $"تمت قراءة {packages.Count:N0} حزمة خارجية من الجهاز.\nلم يتم تثبيت أو إزالة أي تطبيق.";
            return $"تمت قراءة {packages.Count:N0} حزمة.";
        });
    }

    private async void PackageDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_packages.SelectedItem is not string packageName)
        {
            MessageBox.Show(this, "اختر اسم حزمة أولًا.", "مدير التطبيقات",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunOperationAsync("جارٍ قراءة تفاصيل الحزمة...", async token =>
        {
            _packageDetails.Text = await _service.GetPackageDetailsAsync(_serial, packageName, token);
            return "تم جلب معلومات الحزمة دون تعديلها.";
        });
    }

    private async void InstallApk_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار ملف APK لتثبيته أو تحديثه على الهاتف",
            Filter = "Android Package (*.apk)|*.apk",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        await RunOperationAsync("جارٍ تحليل APK محليًا قبل عرض تأكيد التثبيت...", async token =>
        {
            var inspection = await new ApkInspectionService().InspectAsync(dialog.FileName, token);
            if (string.IsNullOrWhiteSpace(inspection.PackageName))
                throw new InvalidDataException("لم يتمكن المحلل من قراءة اسم الحزمة من Manifest؛ لن يتم إرسال الملف إلى الهاتف.");

            var confirmation = MessageBox.Show(this,
                $"الحزمة: {inspection.PackageName}\nالإصدار: {inspection.VersionName ?? "غير متاح"}\n" +
                $"Version Code: {inspection.VersionCode?.ToString() ?? "غير متاح"}\nSHA-256: {inspection.Sha256}\n\n" +
                "سيتم إرسال APK إلى الجهاز المحدد واستخدام ADB install -r. قد يرفض Android التحديث عند اختلاف توقيع الناشر. لا يتم تجاوز هذا الفحص. هل توافق؟",
                "تأكيد تثبيت APK", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
                return "ألغى المستخدم تثبيت APK؛ لم يتم إرسال أي ملف إلى الهاتف.";

            var result = await _service.InstallApkAsync(_serial, dialog.FileName, token);
            _packageDetails.Text = result;
            return result.Contains("Success", StringComparison.OrdinalIgnoreCase)
                ? "أبلغ Android عن نجاح تثبيت/تحديث APK. لم يتم تجاوز قيود التوقيع أو النظام."
                : "انتهى أمر ADB. راجع المخرجات لتحديد النتيجة الفعلية.";
        });
    }

    private async void RunCommand_Click(object sender, RoutedEventArgs e)
    {
        if (_commandPreset.SelectedItem is not PresetItem preset)
            return;

        await RunOperationAsync("جارٍ تنفيذ أمر قراءة فقط على الجهاز المحدد...", async token =>
        {
            _commandOutput.Text = await _service.RunReadOnlyPresetAsync(_serial, preset.Id, token);
            return "اكتمل أمر القراءة.";
        });
    }

    private async void RunBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_batchPreset.SelectedItem is not PresetItem preset)
            return;
        var selected = _batchDevices.SelectedItems.Cast<string>().ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "حدد جهازًا واحدًا أو أكثر.", "العمليات الجماعية",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunOperationAsync("جارٍ تنفيذ العملية المحددة بشكل جماعي وبحد أقصى 3 أجهزة متزامنة...", async token =>
        {
            var results = await _service.RunBatchReadOnlyPresetAsync(selected, preset.Id, 3, token);
            _batchOutput.Text = string.Join(
                Environment.NewLine + Environment.NewLine + new string('=', 72) + Environment.NewLine,
                results.Select(result =>
                    $"DEVICE: {result.DeviceSerial}\nSTATUS: {(result.Success ? "SUCCESS" : "FAILED")}\n" +
                    (result.Success ? result.Output : result.Error)));
            var success = results.Count(result => result.Success);
            return $"اكتملت العملية لكل الأجهزة المحددة: {success} نجاح، {results.Count - success} فشل. كل نتيجة مرتبطة بمعرف جهازها.";
        });
    }

    private async Task RunOperationAsync(string startingMessage, Func<CancellationToken, Task<string>> operation)
    {
        if (_operation is not null)
            return;

        _operation = new CancellationTokenSource();
        SetBusy(true, startingMessage);
        try
        {
            var completed = await operation(_operation.Token);
            _status.Text = completed;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء العملية.";
        }
        catch (Exception ex)
        {
            _status.Text = "فشلت العملية: " + ex.Message;
            MessageBox.Show(this, ex.Message, "أداة Android",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false, _status.Text);
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _cancelButton.IsEnabled = busy;
        _listButton.IsEnabled = !busy;
        _downloadButton.IsEnabled = !busy;
        _uploadButton.IsEnabled = !busy;
        _mkdirButton.IsEnabled = !busy;
        _packageRefreshButton.IsEnabled = !busy;
        _packageDetailsButton.IsEnabled = !busy;
        _installApkButton.IsEnabled = !busy;
        _commandButton.IsEnabled = !busy;
        _batchButton.IsEnabled = !busy;
    }

    private void RefreshBatchDevices()
    {
        _deviceNames.Clear();
        foreach (var serial in _authorizedSerials)
            _deviceNames.Add(serial);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private static TextBlock TextBlock(string text, double size, string color)
        => new() { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 94,
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

    private sealed record PresetItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
