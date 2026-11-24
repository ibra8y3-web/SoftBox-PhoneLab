using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Activity;
using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Core.Settings;
using SoftBox.PhoneLab.Desktop.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Dedicated Apple workspace. Its diagnostics are intentionally isolated from Android ADB/scrcpy.
/// </summary>
public sealed class IPhoneCenterWindow : Window
{
    private readonly PhoneLabSettingsService _settingsService;
    private readonly WindowsUsbInventoryService _usbInventory;
    private readonly DiagnosticEvidenceStore _evidenceStore;
    private readonly Action<string> _openAction;
    private readonly IPhoneDeviceDiagnosticsService _diagnostics = new();
    private readonly TextBox _toolsDirectory = new();
    private readonly Button _scanButton = new();
    private readonly ListBox _devices = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _details = new();
    private readonly HashSet<string> _recordedEvidence = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _autoScanTimer;
    private IReadOnlyList<IPhoneDeviceScanResult> _lastResults = Array.Empty<IPhoneDeviceScanResult>();
    private bool _scanInProgress;
    private PhoneLabSettings _settings;

    public IPhoneDeviceInfoSnapshot? SelectedSnapshot
        => (_devices.SelectedItem as IPhoneDeviceScanResult)?.Snapshot;

    public IPhoneBatteryDiagnosticsSnapshot? SelectedBatteryDiagnostics
        => (_devices.SelectedItem as IPhoneDeviceScanResult)?.BatteryDiagnostics;

    /// <summary>
    /// Applies a background Lockdown scan without sharing the iPhone device list with Android UI.
    /// </summary>
    public void ApplyBackgroundScanResults(IReadOnlyList<IPhoneDeviceScanResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => ApplyBackgroundScanResults(results));
            return;
        }

        _lastResults = results.ToArray();
        _devices.ItemsSource = _lastResults;
        if (_devices.Items.Count > 0 && _devices.SelectedIndex < 0)
            _devices.SelectedIndex = 0;

        var readable = _lastResults.Count(item => item.IsConnectedAndReadable);
        _status.Text = $"اكتشاف تلقائي: {readable} جهاز Apple تمت قراءة معلوماته عبر Lockdown من آخر فحص.";
    }

    public IPhoneCenterWindow(
        PhoneLabSettings settings,
        PhoneLabSettingsService settingsService,
        WindowsUsbInventoryService usbInventory,
        DiagnosticEvidenceStore evidenceStore,
        Action<string> openAction)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _usbInventory = usbInventory ?? throw new ArgumentNullException(nameof(usbInventory));
        _evidenceStore = evidenceStore ?? throw new ArgumentNullException(nameof(evidenceStore));
        _openAction = openAction ?? throw new ArgumentNullException(nameof(openAction));

        Title = "SoftBox | مركز iPhone و iPad";
        Width = 1120;
        Height = 820;
        MinWidth = 860;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        _toolsDirectory.Text = settings.IPhoneToolsDirectory;
        _toolsDirectory.IsReadOnly = true;
        _status.Text = "لم يبدأ فحص iPhone بعد.";
        _status.TextWrapping = TextWrapping.Wrap;
        _details.TextWrapping = TextWrapping.Wrap;
        _devices.SelectionChanged += Devices_SelectionChanged;
        _autoScanTimer = new DispatcherTimer
        {
            // Keep the live identity view responsive without repeatedly running battery diagnostics.
            Interval = TimeSpan.FromSeconds(Math.Max(10, settings.DiscoveryIntervalSeconds * 2))
        };
        _autoScanTimer.Tick += async (_, _) => await ScanDevicesAsync(includeBatteryDiagnostics: false);
        Loaded += async (_, _) =>
        {
            await ScanDevicesAsync();
            if (settings.AutoDiscoverDevices)
                _autoScanTimer.Start();
        };
        Closed += (_, _) => _autoScanTimer.Stop();
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(Text("مركز Apple iPhone و iPad", "#7DD3FC", 25, FontWeights.Bold));
        header.Children.Add(Text("اكتشاف مستقل • معلومات Apple • سجل القطع • البطارية • الفحص الوظيفي • AirPlay", "#91AFCB", 10));
        header.Children.Add(Notice(
            "تنبيه دقة: ظهور Apple USB أو قراءة الرقم التسلسلي لا يثبت أن الهاتف أصلي بالكامل. نعرض فقط البيانات التي قرأها بروتوكول Apple، وسجل القطع الذي تقرؤه من إعدادات الهاتف، والاختبارات التي ينفذها الفني فعليًا.",
            "#3A2D1B", "#A77B38", "#FFE3B5"));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var toolsPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        toolsPanel.Children.Add(Text("مجلد أدوات Apple Lockdown", "#7DD3FC", 11, FontWeights.SemiBold));
        var toolRow = new DockPanel { Margin = new Thickness(0, 5, 0, 6) };
        StyleInput(_toolsDirectory);
        toolRow.Children.Add(_toolsDirectory);
        var browse = Button("اختيار مجلد الأدوات", "#203B58", (_, _) => BrowseTools());
        DockPanel.SetDock(browse, Dock.Left);
        toolRow.Children.Add(browse);
        toolsPanel.Children.Add(toolRow);

        _scanButton.Content = "اكتشاف أجهزة iPhone / iPad وقراءة المعلومات";
        StyleButton(_scanButton, "#14517A");
        _scanButton.Click += async (_, _) => await ScanDevicesAsync();
        toolsPanel.Children.Add(_scanButton);
        DockPanel.SetDock(toolsPanel, Dock.Top);
        root.Children.Add(toolsPanel);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });

        var left = new Border
        {
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var leftStack = new DockPanel();
        leftStack.Children.Add(Text("أجهزة Apple المقروءة", "#7DD3FC", 14, FontWeights.Bold));
        _devices.Margin = new Thickness(0, 8, 0, 0);
        _devices.Background = Brush("#0D1C2E");
        _devices.Foreground = Brush("#EAF3FF");
        _devices.BorderThickness = new Thickness(0);
        _devices.DisplayMemberPath = nameof(IPhoneDeviceScanResult.DisplayName);
        leftStack.Children.Add(_devices);
        left.Child = leftStack;
        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        var right = new Border
        {
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(13),
            Margin = new Thickness(8, 0, 0, 0)
        };
        var rightStack = new DockPanel();
        rightStack.Children.Add(Text("تقرير الجهاز المختار", "#7DD3FC", 14, FontWeights.Bold));
        _status.Margin = new Thickness(0, 8, 0, 8);
        _status.Foreground = Brush("#F5D39E");
        DockPanel.SetDock(_status, Dock.Bottom);
        rightStack.Children.Add(_status);
        _details.Text = "صل iPhone بالكمبيوتر، افتح قفله، وافق على «الوثوق بهذا الكمبيوتر»، ثم نفّذ الاكتشاف.";
        _details.Foreground = Brush("#B7C9DE");
        _details.FontSize = 12;
        _details.Margin = new Thickness(0, 8, 0, 10);
        rightStack.Children.Add(new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        right.Child = rightStack;
        Grid.SetColumn(right, 1);
        body.Children.Add(right);

        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(Text("الأدوات المستقلة", "#7DD3FC", 13, FontWeights.Bold));
        var actions = new WrapPanel();
        actions.Children.Add(Button("سجل القطع والإصلاحات", "#203B58", (_, _) => _openAction("OpenIPhonePartsHistory")));
        actions.Children.Add(Button("صحة البطارية ودوراتها", "#203B58", (_, _) => _openAction("OpenIPhoneBatteryHealth")));
        actions.Children.Add(Button("الفحص الوظيفي والفيزيائي", "#203B58", (_, _) => _openAction("OpenIPhoneFunctionalTests")));
        actions.Children.Add(Button("أدلة الأصالة وتطابق الهوية", "#203B58", (_, _) => _openAction("OpenIPhoneAuthenticityEvidence")));
        actions.Children.Add(Button("عرض سلكي عبر HDMI Capture", "#176044", (_, _) => _openAction("OpenIPhoneHdmiCapture")));
        actions.Children.Add(Button("استقبال شاشة iPhone عبر AirPlay", "#14517A", (_, _) => _openAction("OpenAirPlayReceiver")));
        actions.Children.Add(Button("سجل أدلة الجهاز", "#203B58", (_, _) => OpenEvidenceHistory()));
        actions.Children.Add(Button("فحص تغطية Apple الرسمي", "#203B58", (_, _) => OpenAppleCoverage()));
        bottom.Children.Add(actions);
        bottom.Children.Add(Text(
            "مسارا العرض مستقلان: AirPlay عبر الشبكة، أو HDMI عبر كارت التقاط UVC موصول بالكمبيوتر. HDMI لا يحتاج اختيار Screen Mirroring، لكنه يتطلب أن يدعم موديل iPhone إخراج الفيديو السلكي ومحولًا مناسبًا. لا يفتح أي مسار قفل الهاتف أو يتجاوز رمز الحماية.",
            "#8FB0CE", 10));
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        return root;
    }

    private async Task ScanDevicesAsync(bool includeBatteryDiagnostics = true)
    {
        if (_scanInProgress)
            return;
        _scanInProgress = true;
        _scanButton.IsEnabled = false;
        _devices.ItemsSource = null;
        _recordedEvidence.Clear();
        _status.Text = "جارٍ فحص Windows USB ثم قراءة أجهزة Apple التي تستجيب لخدمة Lockdown...";
        try
        {
            var usb = await _usbInventory.EnumeratePresentDevicesAsync();
            var appleUsb = usb.Where(device =>
                    device.VendorId.Equals("05AC", StringComparison.OrdinalIgnoreCase) ||
                    device.HardwareIds.Any(id => id.Contains("VID_05AC", StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            var toolsDirectory = IPhoneDeviceDiagnosticsService.ResolveToolsDirectory(_toolsDirectory.Text);
            if (toolsDirectory is null)
            {
                _status.Text = appleUsb.Length > 0
                    ? $"رصد Windows عدد {appleUsb.Length} سجل USB مرتبطًا بمعرّف Apple VID 05AC، لكن لا توجد أدوات Lockdown موثوقة لقراءة الهاتف. اختر مجلد libimobiledevice، وثبّت دعم Apple Mobile Device، وافتح الهاتف ووافق على الوثوق بهذا الكمبيوتر. لا يعني USB وحده أن الجهاز تم التعرف عليه بالكامل."
                    : "لم تُكتشف أدوات Apple أو جهاز Apple USB. ثبّت Apple Devices / Apple Mobile Device Support، ثم اختر مجلد أدوات Lockdown موثوقًا.";
                _details.Text = "لم تُقرأ هوية iPhone بعد. ظهور سجل USB فقط لا يوفر طرازًا موثوقًا أو حالة قطع أو صحة بطارية.";
                return;
            }

            _toolsDirectory.Text = toolsDirectory;
            _settings = _settings with { IPhoneToolsDirectory = toolsDirectory };
            await _settingsService.SaveAsync(_settings);
            var results = await _diagnostics.ScanAsync(
                toolsDirectory,
                includeBatteryDiagnostics: includeBatteryDiagnostics);
            if (!includeBatteryDiagnostics && _lastResults.Count > 0)
            {
                // The masked suffix is not a unique device key; preserve GasGauge data only by
                // the one-way fingerprint of the exact Lockdown identity.
                var previous = _lastResults
                    .Where(item => item.Snapshot is not null)
                    .ToDictionary(
                        item => LocalActivityStore.CreateDeviceFingerprint(item.Snapshot!.EvidenceIdentity),
                        StringComparer.Ordinal);
                results = results.Select(item =>
                {
                    if (item.Snapshot is null)
                        return item;
                    var key = LocalActivityStore.CreateDeviceFingerprint(item.Snapshot.EvidenceIdentity);
                    return previous.TryGetValue(key, out var prior)
                        ? item with
                        {
                            BatteryDiagnostics = item.BatteryDiagnostics ?? prior.BatteryDiagnostics,
                            BatteryDiagnosticsError = item.BatteryDiagnosticsError ?? prior.BatteryDiagnosticsError
                        }
                        : item;
                }).ToArray();
            }
            _lastResults = results;
            _devices.ItemsSource = results;
            if (results.Count == 0)
            {
                _status.Text = appleUsb.Length > 0
                    ? $"Windows يرى {appleUsb.Length} سجل Apple USB، لكن Lockdown لم يعرض جهازًا مقروءًا. افتح قفل iPhone ثم اضغط «الوثوق بهذا الكمبيوتر»."
                    : "لم يعرض Lockdown أي جهاز iPhone/iPad متصل. تحقق من الكابل والمنفذ ودعم Apple Mobile Device.";
                _details.Text = "لا توجد بيانات جهاز مؤكدة في هذه الجولة.";
                return;
            }

            var readable = results.Count(result => result.IsConnectedAndReadable);
            var failed = results.Count - readable;
            _status.Text = $"اكتمل الفحص: {readable} جهاز تمت قراءة معلوماته، و{failed} جهاز لم تكتمل قراءته. تم إخفاء المعرّفات.";
            if (_devices.Items.Count > 0)
                _devices.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فحص أجهزة Apple: " + ex.Message;
        }
        finally
        {
            _scanButton.IsEnabled = true;
            _scanInProgress = false;
        }
    }

    private async void Devices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_devices.SelectedItem is not IPhoneDeviceScanResult result)
            return;
        if (result.Snapshot is not { } snapshot)
        {
            _details.Text = $"{result.DisplayName}\n\n{result.Error ?? "لم تصل معلومات قابلة للقراءة."}\n\nلا تصدر حكم أصالة أو استبدال قطع من سجل USB وحده.";
            return;
        }

        _details.Text =
            $"اسم الجهاز: {snapshot.DeviceName}\n" +
            $"Product Type: {snapshot.ProductType}\n" +
            $"إصدار iOS: {snapshot.ProductVersion}\n" +
            $"Build: {snapshot.BuildVersion}\n" +
            $"Model Number: {snapshot.ModelNumber}\n" +
            $"Hardware Model: {snapshot.HardwareModel}\n" +
            $"Serial: {snapshot.MaskedSerialNumber}\n" +
            $"معرّف الاتصال: {snapshot.MaskedDeviceIdentifier}\n" +
            $"السعة الاسمية المقروءة: {FormatBytes(snapshot.TotalDiskCapacityBytes)}\n" +
            $"مساحة البيانات: {FormatBytes(snapshot.TotalDataCapacityBytes)}\n" +
            $"المساحة المتاحة: {FormatBytes(snapshot.AmountDataAvailableBytes)}\n" +
            $"نسبة الشحن الحالية: {FormatPercent(snapshot.CurrentChargePercent)} — ليست السعة القصوى للبطارية\n" +
            $"حالة الشحن: {snapshot.IsCharging switch { true => "يشحن", false => "لا يشحن", null => "غير متاحة" }}\n" +
            $"وقت القراءة UTC: {snapshot.ReadAtUtc:O}\n" +
            $"دورات البطارية (Apple GasGauge): {FormatLong(result.BatteryDiagnostics?.CycleCount)}\n" +
            $"تقدير السعة القصوى من GasGauge: {FormatPercent(result.BatteryDiagnostics?.EstimatedMaximumCapacityPercent)}\n" +
            $"السعة التصميمية الخام: {FormatLong(result.BatteryDiagnostics?.DesignCapacity)}\n" +
            $"سعة الشحن الكامل الخام: {FormatLong(result.BatteryDiagnostics?.FullChargeCapacity)}\n" +
            $"حالة GasGauge: {result.BatteryDiagnostics?.Status ?? result.BatteryDiagnosticsError ?? "غير متاحة"}\n\n" +
            "دليل الهوية: استجابت خدمة Apple Lockdown لطلب معلومات الجهاز. هذا دليل اتصال وهوية بروتوكولية، وليس شهادة بأن كل مكونات الهاتف أصلية أو لم تُستبدل.";
        _status.Text = "تمت قراءة بيانات فعلية من الجهاز. تم جلب بيانات GasGauge تلقائيًا عند توفر idevicediagnostics.exe؛ ويمكنك فتح سجل القطع أو صحة البطارية أو الفحص الفيزيائي.";

        // Deduplicate by the one-way device fingerprint, not the masked suffix: two devices can share
        // the same final four serial characters and must still receive separate evidence histories.
        var evidenceFingerprint = LocalActivityStore.CreateDeviceFingerprint(snapshot.EvidenceIdentity);
        if (_recordedEvidence.Add(evidenceFingerprint))
        {
            try
            {
                var components = new List<ComponentDiagnostic>
                {
                    Evidence("apple-lockdown-identity", "قراءة هوية Apple Lockdown", DiagnosticStatus.DataRead,
                        $"تمت قراءة ProductType={snapshot.ProductType} وiOS={snapshot.ProductVersion} وBuild={snapshot.BuildVersion}."),
                    Evidence("apple-model-number", "رقم طراز الجهاز", snapshot.ModelNumber == "غير متاح" ? DiagnosticStatus.Unavailable : DiagnosticStatus.DataRead,
                        $"ModelNumber={snapshot.ModelNumber}; HardwareModel={snapshot.HardwareModel}."),
                    Evidence("apple-storage", "بيانات السعة والتخزين", snapshot.TotalDiskCapacityBytes.HasValue ? DiagnosticStatus.DataRead : DiagnosticStatus.Unavailable,
                        $"TotalDiskCapacity={snapshot.TotalDiskCapacityBytes}; Available={snapshot.AmountDataAvailableBytes}."),
                    Evidence("apple-current-charge", "نسبة الشحن الحالية", snapshot.CurrentChargePercent.HasValue ? DiagnosticStatus.DataRead : DiagnosticStatus.Unavailable,
                        snapshot.CurrentChargePercent.HasValue
                            ? $"Current charge {snapshot.CurrentChargePercent}% (not maximum capacity or battery health)."
                            : "Apple Lockdown did not expose a usable current charge value."),
                    Evidence("apple-battery-gasgauge", "دورات البطارية وبيانات GasGauge",
                        result.BatteryDiagnostics?.IsSuccessful == true ? DiagnosticStatus.DataRead : DiagnosticStatus.Unavailable,
                        result.BatteryDiagnostics is { } gauge
                            ? $"CycleCount={gauge.CycleCount}; DesignCapacity={gauge.DesignCapacity}; FullChargeCapacity={gauge.FullChargeCapacity}; EstimatedMaximumCapacityPercent={gauge.EstimatedMaximumCapacityPercent}; EstimateBasis={gauge.EstimateBasis}; Status={gauge.Status}."
                            : result.BatteryDiagnosticsError ?? "Apple diagnostics relay did not return GasGauge data.")
                };
                await _evidenceStore.RecordSnapshotAsync(
                    snapshot.EvidenceIdentity, "Apple", snapshot.ProductType, components,
                    "Apple Lockdown read-only information (ideviceinfo)");
            }
            catch (Exception ex)
            {
                _status.Text += "\nتعذر حفظ لقطة الأدلة محليًا: " + ex.Message;
            }
        }
    }

    private void BrowseTools()
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختر ideviceinfo.exe من حزمة libimobiledevice الموثوقة",
            Filter = "Apple device information utility (ideviceinfo.exe)|ideviceinfo.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;
        var directory = Path.GetDirectoryName(dialog.FileName)!;
        if (!File.Exists(Path.Combine(directory, "idevice_id.exe")))
        {
            MessageBox.Show(this,
                "المجلد المحدد لا يحتوي idevice_id.exe بجانب ideviceinfo.exe. لم يتم حفظ المسار.",
                "أدوات Apple", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _toolsDirectory.Text = directory;
        _settings = _settings with { IPhoneToolsDirectory = directory };
        _ = SaveToolsDirectoryAsync();
    }

    private async Task SaveToolsDirectoryAsync()
    {
        try
        {
            await _settingsService.SaveAsync(_settings);
            _status.Text = "تم حفظ مجلد أدوات Apple محليًا. لم يتم تنزيل أو تشغيل أي ملف في الخلفية.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر حفظ مجلد الأدوات: " + ex.Message;
        }
    }

    private void OpenEvidenceHistory()
    {
        if (SelectedSnapshot is not { } snapshot)
        {
            MessageBox.Show(this, "نفّذ قراءة ناجحة لجهاز iPhone أولًا.", "سجل الأدلة",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new DiagnosticEvidenceHistoryWindow(_evidenceStore, snapshot.EvidenceIdentity) { Owner = this }.ShowDialog();
    }

    private void OpenAppleCoverage()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://checkcoverage.apple.com/") { UseShellExecute = true });
            _status.Text = "تم فتح صفحة Apple الرسمية. أدخل الرقم التسلسلي بنفسك داخل موقع Apple؛ SoftBox لا يرسل الرقم تلقائيًا.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح صفحة Apple: " + ex.Message;
        }
    }

    private static ComponentDiagnostic Evidence(string id, string name, DiagnosticStatus status, string evidence)
        => new() { Id = id, Name = name, Status = status, Evidence = evidence, Description = "لقطة معلومات قراءة وليست اختبار أداء أو شهادة أصالة." };

    private static string FormatBytes(long? bytes)
        => bytes is null ? "غير متاح" : $"{bytes.Value / 1024d / 1024d / 1024d:0.##} GiB";

    private static string FormatPercent(int? percent)
        => percent is null ? "غير متاح" : percent.Value.ToString() + "%";

    private static string FormatLong(long? value)
        => value is null ? "غير متاح" : value.Value.ToString("N0");

    private static Border Notice(string text, string background, string border, string foreground)
        => new()
        {
            Background = Brush(background),
            BorderBrush = Brush(border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 8),
            Child = Text(text, foreground, 10)
        };

    private static Button Button(string label, string color, RoutedEventHandler action)
    {
        var button = new Button { Content = label, Padding = new Thickness(11, 7, 11, 7), Margin = new Thickness(0, 0, 7, 7), Background = Brush(color), Foreground = Brushes.White, BorderBrush = Brush("#3A668B") };
        button.Click += action;
        return button;
    }

    private static void StyleButton(Button button, string color)
    {
        button.Padding = new Thickness(12, 8, 12, 8);
        button.Background = Brush(color);
        button.Foreground = Brushes.White;
        button.BorderBrush = Brush("#3A668B");
        button.HorizontalAlignment = HorizontalAlignment.Right;
    }

    private static void StyleInput(TextBox input)
    {
        input.Padding = new Thickness(9, 7, 9, 7);
        input.Background = Brush("#0A1726");
        input.Foreground = Brush("#EAF3FF");
        input.BorderBrush = Brush("#36516D");
        input.Margin = new Thickness(0, 0, 8, 0);
    }

    private static TextBlock Text(string value, string color, double size, FontWeight? weight = null)
        => new()
        {
            Text = value,
            Foreground = Brush(color),
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 3)
        };

    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value)!);
}
