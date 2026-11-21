using SoftBox.PhoneLab.Core.Fastboot;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Xiaomi/Redmi/POCO transition and read-only Fastboot inspection.
/// The only state transition is a confirmed ADB reboot to Bootloader.
/// </summary>
public sealed class XiaomiFastbootDiagnosticsWindow : Window
{
    private readonly AdbClient _adb;
    private readonly string _serial;
    private readonly string _manufacturer;
    private readonly string _model;
    private readonly AndroidDeviceToolsService _androidTools;
    private readonly FastbootService _fastboot = new();
    private readonly TextBox _details = new();
    private readonly TextBlock _status = new();
    private readonly Button _enterButton;
    private readonly Button _refreshButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _operation;

    public XiaomiFastbootDiagnosticsWindow(AdbClient adb, string serial, string manufacturer, string model)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        if (!XiaomiFastbootDiagnosticsPolicy.IsXiaomiManufacturer(manufacturer))
            throw new UnauthorizedAccessException("تم رفض فتح أدوات Xiaomi Fastboot لأن هوية الشركة ليست Xiaomi/Redmi/POCO.");

        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _serial = serial;
        _manufacturer = manufacturer;
        _model = model ?? "";
        _androidTools = new AndroidDeviceToolsService(adb);

        Title = "SoftBox | Xiaomi Fastboot Diagnostics";
        Width = 1000;
        Height = 740;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _enterButton = ButtonControl("إعادة تشغيل الهاتف إلى Fastboot", "#704B20", EnterFastboot_Click);
        _refreshButton = ButtonControl("إعادة فحص Fastboot وقراءة المتغيرات", "#14517A", Refresh_Click);
        _cancelButton = ButtonControl("إلغاء", "#713B43", Cancel_Click);
        _refreshButton.IsEnabled = false;
        _cancelButton.IsEnabled = false;
        Content = BuildContent();
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Xiaomi / Redmi / POCO — Fastboot Read-only Diagnostics",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = $"الجهاز: {_manufacturer} {_model} | ADB: {Mask(_serial)}",
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 6, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "ينفذ انتقالًا اختياريًا إلى Bootloader بعد إعادة التحقق من ADB وخصائص الشركة، ثم يقرأ fastboot getvar all للجهاز المطابق فقط. لا يحتوي على أوامر Flash أو Unlock أو Erase أو Format أو Relock.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#F0C9A5"),
            Margin = new Thickness(0, 8, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(_enterButton);
        actions.Children.Add(_refreshButton);
        actions.Children.Add(_cancelButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.Wrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.Background = Brush("#071625");
        _details.Foreground = Brush("#DCEBFA");
        _details.BorderBrush = Brush("#263D58");
        _details.Padding = new Thickness(10);
        _details.FontFamily = new FontFamily("Consolas");
        _details.FontSize = 10;
        _details.Text = "تأكد من اختيار الهاتف الصحيح. الانتقال إلى Bootloader يوقف جلسة ADB الحالية مؤقتًا.";
        root.Children.Add(_details);

        _status.Text = "لم يتم تنفيذ أي انتقال بعد.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private async void EnterFastboot_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;
        if (!_fastboot.IsAvailable)
        {
            _status.Text = "Fastboot غير متاح داخل حزمة SoftBox أو PATH؛ لم يتم إعادة تشغيل الهاتف.";
            return;
        }
        var confirmation = MessageBox.Show(this,
            $"سيعيد SoftBox تشغيل هاتف Xiaomi/Redmi/POCO المحدد إلى Bootloader/Fastboot:\n{_model} | {Mask(_serial)}\n\nلن يتم تفليش أو مسح أو فتح Bootloader. قد يتوقف ADB حتى يعاد تشغيل الهاتف. متابعة؟",
            "تأكيد الانتقال إلى Xiaomi Fastboot", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await RunOperationAsync("جارٍ التحقق من ADB وهوية Xiaomi قبل الانتقال...", async token =>
        {
            var state = await _adb.RunForDeviceAsync(_serial, ["get-state"], token, TimeSpan.FromSeconds(12));
            if (!state.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("الهاتف لم يعد في حالة ADB device؛ تم إيقاف العملية.");

            var manufacturer = await _adb.RunForDeviceAsync(
                _serial, ["shell", "getprop", "ro.product.manufacturer"], token, TimeSpan.FromSeconds(12));
            var brand = await _adb.RunForDeviceAsync(
                _serial, ["shell", "getprop", "ro.product.brand"], token, TimeSpan.FromSeconds(12));
            if (!XiaomiFastbootDiagnosticsPolicy.IsXiaomiManufacturer(manufacturer) &&
                !XiaomiFastbootDiagnosticsPolicy.IsXiaomiManufacturer(brand))
                throw new UnauthorizedAccessException("خصائص Android الحالية لا تؤكد Xiaomi/Redmi/POCO؛ لم يتم إرسال أمر reboot.");

            var output = await _androidTools.RebootDeviceAsync(_serial, AndroidRebootTarget.Bootloader, token);
            _status.Text = output + " جارٍ انتظار Fastboot على نفس Serial...";
            var device = await WaitForSelectedFastbootDeviceAsync(token);
            var raw = await _fastboot.ReadAllVariablesAsync(device.Serial, token);
            var parsed = FastbootVariableParser.Parse(raw);
            var report = XiaomiFastbootDiagnosticsPolicy.Evaluate(
                manufacturer + " " + brand, _serial, device.Serial, device.Details, parsed);
            _details.Text = FormatReport(report, raw);
            _refreshButton.IsEnabled = true;
            return "ظهر الجهاز في Fastboot وتمت قراءة متغيراته. لم يتم تغيير أي قسم أو إعداد.";
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;
        await RunOperationAsync("جارٍ البحث عن نفس الجهاز في Fastboot وقراءة المتغيرات...", async token =>
        {
            var devices = await _fastboot.DiscoverAsync(token);
            var device = devices.FirstOrDefault(item =>
                item.Serial.Equals(_serial, StringComparison.OrdinalIgnoreCase));
            if (device is null)
            {
                _details.Text = "لم يظهر Serial الخاص بالهاتف المحدد في fastboot devices -l. تم رفض قراءة أي هاتف آخر.";
                return "لم يتم العثور على نفس الهاتف في Fastboot.";
            }
            var raw = await _fastboot.ReadAllVariablesAsync(device.Serial, token);
            var report = XiaomiFastbootDiagnosticsPolicy.Evaluate(
                _manufacturer, _serial, device, FastbootVariableParser.Parse(raw));
            _details.Text = FormatReport(report, raw);
            return "اكتملت قراءة متغيرات Fastboot للجهاز المحدد.";
        });
    }

    private async Task<FastbootDevice> WaitForSelectedFastbootDeviceAsync(CancellationToken cancellationToken)
    {
        if (!_fastboot.IsAvailable)
            throw new FileNotFoundException("لم يتم العثور على fastboot.exe في أدوات SoftBox أو PATH.");
        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var devices = await _fastboot.DiscoverAsync(cancellationToken);
            var selected = devices.FirstOrDefault(item =>
                item.Serial.Equals(_serial, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
                return selected;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        throw new TimeoutException("لم يظهر الهاتف المحدد في Fastboot خلال 60 ثانية. تحقق من كابل USB وتعريف Fastboot؛ لم تتم قراءة أي جهاز آخر.");
    }

    private static string FormatReport(XiaomiFastbootDiagnosticsReport report, string raw)
    {
        var lines = new List<string>
        {
            "XIAOMI FASTBOOT INSPECTION — READ ONLY",
            "DEVICE: " + report.DeviceLabel,
            "XIAOMI PROFILE: " + report.IsXiaomiDevice,
            "SELECTED SERIAL MATCH: " + report.SelectedSerialMatches,
            "FLASH ALLOWED: " + report.FlashAllowed,
            "PRODUCT: " + (report.Product ?? "غير متاح"),
            "VARIANT: " + (report.Variant ?? "غير متاح"),
            "HARDWARE REVISION: " + (report.HardwareRevision ?? "غير متاح"),
            "BOOTLOADER VERSION: " + (report.BootloaderVersion ?? "غير متاح"),
            "CURRENT SLOT: " + (report.CurrentSlot ?? "غير متاح"),
            "BOOTLOADER LOCK: " + report.LockState,
            "SECURE BOOT: " + report.SecureBootState,
            "",
            "ANTI-ROLLBACK VARIABLES:"
        };
        lines.AddRange(report.RollbackVariables.Count == 0
            ? ["لا توجد متغيرات معروفة في المخرجات."]
            : report.RollbackVariables.Select(pair => pair.Key + ": " + pair.Value));
        lines.Add("");
        lines.Add("FINDINGS:");
        lines.AddRange(report.Findings.Select(item => "- " + item));
        lines.Add("");
        lines.Add("WARNINGS:");
        lines.AddRange(report.Warnings.Select(item => "- " + item));
        lines.Add("");
        lines.Add("RAW FASTBOOT GETVAR OUTPUT:");
        lines.Add(raw);
        return string.Join(Environment.NewLine, lines);
    }

    private async Task RunOperationAsync(string message, Func<CancellationToken, Task<string>> operation)
    {
        if (_operation is not null)
            return;
        _operation = new CancellationTokenSource();
        _enterButton.IsEnabled = false;
        _refreshButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        _status.Text = message;
        try
        {
            _status.Text = await operation(_operation.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء العملية.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تنفيذ فحص Xiaomi Fastboot: " + ex.Message;
            _details.Text = ex.ToString();
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _enterButton.IsEnabled = true;
            _refreshButton.IsEnabled = true;
            _cancelButton.IsEnabled = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private static string Mask(string serial) => serial.Length > 4 ? "••••" + serial[^4..] : "••••";

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 135,
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
