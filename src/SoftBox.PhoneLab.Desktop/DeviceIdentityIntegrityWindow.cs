using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Settings;
using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Standalone, read-only identity and operating-mode workspace.
/// It correlates only exact identifiers of the same declared type and never writes device identity.
/// </summary>
public sealed class DeviceIdentityIntegrityWindow : Window
{
    private readonly WindowsUsbInventoryService _usbInventory;
    private readonly AdbClient _adb;
    private readonly Func<IReadOnlyList<DeviceListItem>> _adbDevicesProvider;
    private readonly FastbootService _fastboot = new();
    private readonly IPhoneDeviceDiagnosticsService _iphoneDiagnostics = new();
    private readonly PhoneLabSettings _settings;
    private readonly ObservableCollection<DeviceIdentityRow> _rows = [];
    private readonly ListBox _devices = new();
    private readonly TextBlock _status = new();
    private readonly TextBox _details = new();
    private readonly TextBox _manufacturer = new();
    private readonly TextBox _model = new();
    private readonly TextBox _mode = new();
    private readonly TextBox _identifier = new();
    private readonly Button _copyIdentifier = new();
    private readonly Button _refresh = new();
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly Dictionary<string, string> _fastbootVariableCache = new(StringComparer.Ordinal);
    private DateTime _lastAppleScanUtc = DateTime.MinValue;
    private bool _closing;

    public DeviceIdentityIntegrityWindow(
        WindowsUsbInventoryService usbInventory,
        AdbClient adb,
        Func<IReadOnlyList<DeviceListItem>> adbDevicesProvider,
        PhoneLabSettings settings)
    {
        _usbInventory = usbInventory ?? throw new ArgumentNullException(nameof(usbInventory));
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _adbDevicesProvider = adbDevicesProvider ?? throw new ArgumentNullException(nameof(adbDevicesProvider));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Title = "SoftBox | Device Identity Integrity";
        Width = 1120;
        Height = 820;
        MinWidth = 860;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _devices.ItemsSource = _rows;
        _devices.DisplayMemberPath = nameof(DeviceIdentityRow.DisplayLabel);
        _devices.SelectionChanged += (_, _) => ShowSelection();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(settings.DiscoveryIntervalSeconds, 5, 30))
        };
        _timer.Tick += async (_, _) => await RefreshAsync(manual: false);
        Loaded += async (_, _) =>
        {
            await RefreshAsync(manual: true);
            if (_settings.AutoDiscoverDevices)
                _timer.Start();
        };
        Closed += (_, _) =>
        {
            _closing = true;
            _timer.Stop();
        };

        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(Text("محرك سلامة هوية الجهاز والتعبئة الذكية", 24, "#7DD3FC", true));
        header.Children.Add(Text(
            "يقرأ معلومات النقل الفعلية من Windows USB وADB وFastboot وApple Lockdown، ويعرض وضع الجهاز ومصدر كل معلومة ويكشف التعارضات. لا يغيّر IMEI أو Serial أو NVRAM/EFS ولا ينفّذ Flash.",
            11, "#B7C9DE"));
        header.Children.Add(Notice(
            "التعرّف في وضع القفل أو Download أو Recovery يعتمد على ما يعلنه البروتوكول نفسه. إذا كان الوضع لا يتيح رقمًا تسلسليًا أو موديلًا، سيظهر «غير متاح» بدل تخمينه. موافقة المالك مطلوبة لأي قناة وصول محمية.",
            "#30251B", "#8B6238", "#FFE0B7"));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        _refresh = Button("فحص جميع قنوات الاتصال", "#14517A", async (_, _) => await RefreshAsync(manual: true));
        actions.Children.Add(_refresh);
        _copyIdentifier.Content = "نسخ المعرّف المقروء";
        StyleButton(_copyIdentifier, "#203B58");
        _copyIdentifier.IsEnabled = false;
        _copyIdentifier.Click += (_, _) => CopySelectedIdentifier();
        actions.Children.Add(_copyIdentifier);
        actions.Children.Add(Button("نسخ تقرير الأدلة", "#203B58", (_, _) => CopyEvidenceReport()));
        header.Children.Add(actions);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#F5D39E");
        _status.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });

        var listPanel = Panel("الأجهزة والأوضاع التي تم رصدها");
        var listContent = (StackPanel)listPanel.Child;
        _devices.Margin = new Thickness(0, 6, 0, 0);
        listContent.Children.Add(_devices);
        Grid.SetColumn(listPanel, 0);
        body.Children.Add(listPanel);

        var detailPanel = Panel("حقول الهوية المقروءة تلقائيًا");
        var detailContent = (StackPanel)detailPanel.Child;
        detailContent.Children.Add(Field("الشركة المصنّعة", _manufacturer));
        detailContent.Children.Add(Field("الموديل", _model));
        detailContent.Children.Add(Field("وضع الجهاز", _mode));
        detailContent.Children.Add(Field("المعرّف (مقنّع)", _identifier));
        detailContent.Children.Add(Text("مصدر القراءة ودرجة الثقة والتحذيرات", 12, "#7DD3FC", true));
        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.Wrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.MinHeight = 230;
        _details.Background = Brush("#071625");
        _details.Foreground = Brush("#DCEBFA");
        _details.BorderBrush = Brush("#263D58");
        _details.Padding = new Thickness(10);
        detailContent.Children.Add(_details);
        Grid.SetColumn(detailPanel, 1);
        body.Children.Add(detailPanel);

        root.Children.Add(body);
        var footer = Notice(
            "التعبئة الذكية هنا تملأ حقول SoftBox من بيانات القراءة المؤكدة. النسخ إلى تطبيق آخر يتطلب ضغط زر النسخ. لا يتم تعبئة عبارة تأكيد التفليش أو الضغط على Start تلقائيًا؛ هذا حاجز موافقة مستقل.",
            "#10283B", "#2F6687", "#CDEBFF");
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        return root;
    }

    private async Task RefreshAsync(bool manual)
    {
        if (_closing || !await _scanGate.WaitAsync(0))
            return;

        _refresh.IsEnabled = false;
        _status.Text = "جارٍ فحص Windows USB وADB وFastboot وApple Lockdown بشكل منفصل...";
        try
        {
            var rows = new List<DeviceIdentityRow>();
            var observations = new List<DeviceIdentityObservation>();

            IReadOnlyList<WindowsUsbDevice> usbDevices;
            try
            {
                usbDevices = await _usbInventory.EnumeratePresentDevicesAsync();
                foreach (var device in usbDevices)
                {
                    var mode = DeviceModeClassifier.ClassifyUsb(device);
                    if (!device.VendorId.Equals("----", StringComparison.OrdinalIgnoreCase) &&
                        !device.ProductId.Equals("----", StringComparison.OrdinalIgnoreCase))
                    {
                        var id = device.VendorId + ":" + device.ProductId;
                        observations.Add(new DeviceIdentityObservation(
                            "USB", mode.Mode, "USB VID/PID", id,
                            device.Manufacturer, string.Empty,
                            "Windows SetupAPI / PnP", mode.Confidence));
                        rows.Add(new DeviceIdentityRow(
                            "USB", mode.Mode, "USB VID/PID", id,
                            device.Manufacturer, string.Empty, "Windows USB PnP",
                            mode.Confidence, mode.Limitation, device.ProblemCode));
                    }
                    else
                    {
                        rows.Add(new DeviceIdentityRow(
                            "USB", mode.Mode, "No identifier exposed", string.Empty,
                            device.Manufacturer, string.Empty, "Windows USB PnP",
                            mode.Confidence, mode.Limitation, device.ProblemCode));
                    }
                }
            }
            catch (Exception ex)
            {
                rows.Add(ErrorRow("USB", "Windows PnP", ex.Message));
                usbDevices = Array.Empty<WindowsUsbDevice>();
            }

            var adbDevices = _adbDevicesProvider();
            foreach (var device in adbDevices)
            {
                var authorized = device.IsAuthorized;
                var mode = DeviceModeClassifier.ClassifyAdbState(device.State);
                var confidence = authorized
                    ? "ADB reports an authorized transport"
                    : "ADB transport state; access not authorized";
                observations.Add(new DeviceIdentityObservation(
                    "Android", mode.Mode, "ADB transport serial", device.Serial,
                    device.Manufacturer, device.Model, "Android Debug Bridge", confidence));
                rows.Add(new DeviceIdentityRow(
                    "Android", mode.Mode, "ADB transport serial", device.Serial,
                    device.Manufacturer, device.Model, "ADB",
                    confidence, mode.Limitation, 0));
            }

            if (_fastboot.IsAvailable)
            {
                try
                {
                    var fastbootDevices = await _fastboot.DiscoverAsync();
                    foreach (var device in fastbootDevices)
                    {
                        var details = device.Details;
                        var mode = "Android Bootloader / Fastboot";
                        var confidence = "Fastboot transport listed the exact device identifier";
                        var warning = "Serial is a Fastboot transport identifier. It is not compared with ADB serial unless the identifier types are explicitly the same.";

                        if (!_fastbootVariableCache.TryGetValue(device.Serial, out var raw))
                        {
                            try
                            {
                                raw = await _fastboot.ReadAllVariablesAsync(device.Serial);
                                _fastbootVariableCache[device.Serial] = raw;
                            }
                            catch (Exception ex)
                            {
                                warning += " Read-only getvar failed: " + SafeMessage(ex);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            var parsed = SoftBox.PhoneLab.Core.Fastboot.FastbootVariableParser.Parse(raw);
                            var fields = new[]
                            {
                                parsed.Product is null ? null : "product=" + parsed.Product,
                                parsed.Variant is null ? null : "variant=" + parsed.Variant,
                                parsed.HardwareRevision is null ? null : "hardware-revision=" + parsed.HardwareRevision
                            }.Where(value => value is not null);
                            var parsedDetails = string.Join("; ", fields);
                            if (!string.IsNullOrWhiteSpace(parsedDetails))
                                details = parsedDetails;
                            confidence = "Fastboot device list + read-only getvar all";
                            warning += " Product/hardware strings are raw Bootloader claims and still need model verification.";
                        }

                        observations.Add(new DeviceIdentityObservation(
                            "Android", mode, "Fastboot transport serial", device.Serial,
                            string.Empty, details, "Android Fastboot", confidence));
                        rows.Add(new DeviceIdentityRow(
                            "Android", mode, "Fastboot transport serial", device.Serial,
                            string.Empty, details, "Fastboot", confidence, warning, 0));
                    }
                }
                catch (Exception ex)
                {
                    rows.Add(ErrorRow("Android", "Fastboot", ex.Message));
                }
            }
            else if (manual)
            {
                rows.Add(new DeviceIdentityRow(
                    "Android", "Fastboot tool unavailable", "No identifier exposed", string.Empty,
                    string.Empty, string.Empty, "fastboot.exe",
                    "Unknown", "لم يتم العثور على fastboot.exe؛ لا يمكن قراءة وضع Bootloader من قناة Fastboot.", 0));
            }

            var appleToolsDirectory = IPhoneDeviceDiagnosticsService.ResolveToolsDirectory(_settings.IPhoneToolsDirectory);
            if (appleToolsDirectory is not null)
            {
                if (DateTime.UtcNow - _lastAppleScanUtc >= TimeSpan.FromSeconds(15))
                {
                    _lastAppleScanUtc = DateTime.UtcNow;
                    try
                    {
                        var apple = await _iphoneDiagnostics.ScanAsync(
                            appleToolsDirectory,
                            includeBatteryDiagnostics: false);
                        foreach (var item in apple)
                        {
                            if (item.Snapshot is { } snapshot)
                            {
                                var isSerial = !snapshot.MaskedSerialNumber.Equals("غير متاح", StringComparison.Ordinal);
                                var identifierKind = isSerial ? "Apple serial" : "Apple UDID";
                                var rawIdentifier = snapshot.EvidenceIdentity;
                                var confidence = "Apple Lockdown returned device information";
                                var warning = "Lockdown response is an identity clue, not a certificate that the complete device or its parts are genuine.";
                                observations.Add(new DeviceIdentityObservation(
                                    "Apple", "iOS / Lockdown", identifierKind, rawIdentifier,
                                    "Apple", snapshot.ProductType, "Apple Lockdown", confidence));
                                rows.Add(new DeviceIdentityRow(
                                    "Apple", "iOS / Lockdown", identifierKind, rawIdentifier,
                                    "Apple", snapshot.ProductType, "Apple Lockdown",
                                    confidence, warning, 0));
                            }
                            else
                            {
                                rows.Add(new DeviceIdentityRow(
                                    "Apple", "Lockdown listed; information unavailable", "Apple device identifier",
                                    string.Empty, "Apple", string.Empty, "Apple Lockdown",
                                    "Unverified", item.Error ?? "Apple device information is not readable in this session.", 0));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        rows.Add(ErrorRow("Apple", "Apple Lockdown", ex.Message));
                    }
                }
            }
            else if (manual)
            {
                rows.Add(new DeviceIdentityRow(
                    "Apple", "Apple Lockdown tools not configured", "No identifier exposed", string.Empty,
                    "Apple", string.Empty, "Apple Lockdown",
                    "Unknown", "لم يتم العثور على أدوات libimobiledevice. اختر مجلد الأدوات من مركز iPhone؛ لن يتم تخمين الرقم من USB VID وحده.", 0));
            }

            var report = DeviceIdentityIntegrityEngine.Analyze(observations);
            foreach (var finding in report.Findings)
            {
                // Match on a one-way hash of the full identifier, never on its masked suffix.
                var existing = rows.FirstOrDefault(item =>
                    item.Platform.Equals(finding.Platform, StringComparison.OrdinalIgnoreCase) &&
                    item.IdentifierKind.Equals(finding.IdentifierKind, StringComparison.OrdinalIgnoreCase) &&
                    DeviceIdentityIntegrityEngine.CreateIdentifierFingerprint(item.RawIdentifier) ==
                    finding.IdentifierFingerprint);
                if (existing is null)
                    continue;
                var updated = existing with
                {
                    IntegrityStatus = finding.Status,
                    IntegrityWarnings = finding.Warnings
                };
                var index = rows.IndexOf(existing);
                if (index >= 0)
                    rows[index] = updated;
            }

            // No transport is merged with another transport by timing, vendor name or a similar suffix.
            _rows.Clear();
            foreach (var row in rows.OrderBy(item => item.Platform, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Mode, StringComparer.OrdinalIgnoreCase))
                _rows.Add(row);

            _status.Text =
                $"اكتمل الفحص: USB={usbDevices.Count} • ADB={adbDevices.Count} • Fastboot={rows.Count(row => row.Source == "Fastboot")} • " +
                $"Apple Lockdown={rows.Count(row => row.Source == "Apple Lockdown" && row.RawIdentifier.Length > 0)}. " +
                $"حالة الأدلة: {StatusLabel(report.OverallStatus)}. لم يتم تنفيذ أي كتابة على الجهاز.";
            if (_devices.Items.Count > 0 && _devices.SelectedIndex < 0)
                _devices.SelectedIndex = 0;
            if (_devices.SelectedItem is not null)
                ShowSelection();
        }
        catch (Exception ex)
        {
            _status.Text = "فشل فحص سلامة الهوية: " + SafeMessage(ex);
        }
        finally
        {
            _refresh.IsEnabled = true;
            _scanGate.Release();
        }
    }

    private void ShowSelection()
    {
        if (_devices.SelectedItem is not DeviceIdentityRow row)
        {
            ClearFields();
            return;
        }

        _manufacturer.Text = Fallback(row.Manufacturer);
        _model.Text = Fallback(row.Model);
        _mode.Text = row.Mode;
        _identifier.Text = DeviceIdentityIntegrityEngine.MaskIdentifier(row.RawIdentifier);
        _copyIdentifier.IsEnabled = DeviceIdentityIntegrityEngine.IsSafeIdentifierForUi(row.RawIdentifier);
        _details.Text = string.Join(Environment.NewLine, new[]
        {
            "Platform: " + row.Platform,
            "Mode: " + row.Mode,
            "Identifier type: " + row.IdentifierKind,
            "Identifier source: " + row.Source,
            "Confidence: " + row.Confidence,
            "Integrity state: " + StatusLabel(row.IntegrityStatus),
            "Manufacturer: " + Fallback(row.Manufacturer),
            "Model/Product: " + Fallback(row.Model),
            "Windows Problem Code: " + row.ProblemCode,
            "Details:",
            row.Warning,
            string.Join(Environment.NewLine, row.IntegrityWarnings ?? Array.Empty<string>())
        });
    }

    private void CopySelectedIdentifier()
    {
        if (_devices.SelectedItem is not DeviceIdentityRow row ||
            !DeviceIdentityIntegrityEngine.IsSafeIdentifierForUi(row.RawIdentifier))
            return;
        try
        {
            Clipboard.SetText(row.RawIdentifier);
            _status.Text = "تم نسخ المعرّف المقروء من المصدر المحدد. راجع نوع المعرّف؛ قد يكون USB VID/PID أو ADB/Fastboot/Apple، وليس بالضرورة IMEI.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر نسخ المعرّف: " + SafeMessage(ex);
        }
    }

    private void CopyEvidenceReport()
    {
        var report = string.Join(Environment.NewLine, _rows.Select(row =>
            $"{row.Platform} | {row.Mode} | {row.IdentifierKind} | {DeviceIdentityIntegrityEngine.MaskIdentifier(row.RawIdentifier)} | {row.Source} | {row.Confidence} | {StatusLabel(row.IntegrityStatus)}"));
        if (string.IsNullOrWhiteSpace(report))
            report = "No device identity evidence has been collected.";
        try
        {
            Clipboard.SetText(report);
            _status.Text = "تم نسخ تقرير الأدلة بمعرّفات مقنّعة فقط.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر نسخ التقرير: " + SafeMessage(ex);
        }
    }

    private void ClearFields()
    {
        _manufacturer.Clear();
        _model.Clear();
        _mode.Clear();
        _identifier.Clear();
        _details.Clear();
        _copyIdentifier.IsEnabled = false;
    }

    private static Border Panel(string title)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(title, 13, "#7DD3FC", true));
        return new Border
        {
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 8, 0),
            Child = stack
        };
    }

    private static UIElement Field(string title, TextBox field)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 3, 0, 6) };
        stack.Children.Add(Text(title, 10, "#8FB0CE"));
        field.IsReadOnly = true;
        field.Padding = new Thickness(8);
        field.Background = Brush("#071625");
        field.Foreground = Brush("#DCEBFA");
        field.BorderBrush = Brush("#263D58");
        stack.Children.Add(field);
        return stack;
    }

    private static DeviceIdentityRow ErrorRow(string platform, string source, string message)
        => new(platform, "Read error", "No identifier exposed", string.Empty,
            string.Empty, string.Empty, source, "Unknown", SafeMessage(new InvalidOperationException(message)), 0);

    private static string Fallback(string? value)
        => string.IsNullOrWhiteSpace(value) ? "غير متاح" : value;

    private static string StatusLabel(DeviceIdentityIntegrityStatus status) => status switch
    {
        DeviceIdentityIntegrityStatus.ConsistentEvidence => "أدلة متسقة من أكثر من قراءة",
        DeviceIdentityIntegrityStatus.ReviewRequired => "تحتاج مراجعة",
        DeviceIdentityIntegrityStatus.ConflictDetected => "تعارض في الأدلة",
        _ => "أدلة غير كافية"
    };

    private static string SafeMessage(Exception exception)
    {
        var value = exception.Message ?? string.Empty;
        value = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= 500 ? value : value[..500] + "…";
    }

    private static Border Notice(string text, string background, string border, string foreground)
        => new()
        {
            Background = Brush(background),
            BorderBrush = Brush(border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 6),
            Child = Text(text, 10, foreground)
        };

    private static Button Button(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 8, 0),
            Background = Brush(color),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096")
        };
        button.Click += handler;
        return button;
    }

    private static void StyleButton(Button button, string color)
    {
        button.Padding = new Thickness(10, 7, 10, 7);
        button.Margin = new Thickness(0, 0, 8, 0);
        button.Background = Brush(color);
        button.Foreground = Brushes.White;
        button.BorderBrush = Brush("#3E7096");
    }

    private static TextBlock Text(string value, double size, string color, bool bold = false)
        => new()
        {
            Text = value,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Brush(color),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 6)
        };

    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value)!);

    private sealed record DeviceIdentityRow(
        string Platform,
        string Mode,
        string IdentifierKind,
        string RawIdentifier,
        string Manufacturer,
        string Model,
        string Source,
        string Confidence,
        string Warning,
        int ProblemCode,
        DeviceIdentityIntegrityStatus IntegrityStatus = DeviceIdentityIntegrityStatus.InsufficientEvidence,
        IReadOnlyList<string>? IntegrityWarnings = null)
    {
        public string DisplayLabel =>
            $"{Platform} • {Mode} • {IdentifierKind}: {DeviceIdentityIntegrityEngine.MaskIdentifier(RawIdentifier)}";
    }
}
