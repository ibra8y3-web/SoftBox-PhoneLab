using SoftBox.PhoneLab.Core.Compatibility;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Services;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Read-only view of the shared DeviceCapabilityEngine decisions for the currently selected phone.
/// It revalidates ADB/Fastboot transport state before displaying operational capabilities.
/// </summary>
public sealed class DeviceCapabilityCenterWindow : Window
{
    private readonly AdbClient _adb;
    private readonly DeviceSnapshot _device;
    private readonly bool _wasAuthorized;
    private readonly bool _agentWasConnected;
    private readonly PhoneDiscoveryService _discovery;
    private readonly FastbootService _fastboot = new();
    private readonly DeviceProfileCatalog _profiles = new();
    private readonly BoardMapCatalog _maps = new();
    private readonly TextBox _report = new();
    private readonly TextBlock _status = new();
    private readonly Button _refreshButton;
    private bool _busy;

    public DeviceCapabilityCenterWindow(
        AdbClient adb,
        DeviceSnapshot device,
        bool adbAuthorized,
        bool agentConnected)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        if (!AdbClient.IsSafeSerial(device.Serial))
            throw new ArgumentException("معرف الجهاز غير صالح.", nameof(device));
        _wasAuthorized = adbAuthorized;
        _agentWasConnected = agentConnected;
        _discovery = new PhoneDiscoveryService(_adb);

        Title = "SoftBox | محرك قدرات وتوافق الجهاز";
        Width = 1120;
        Height = 820;
        MinWidth = 850;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _refreshButton = ButtonControl("إعادة فحص القدرات", "#14517A", async (_, _) => await RefreshAsync());
        Content = BuildContent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "محرك قدرات وتوافق الجهاز",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "DEVICE CAPABILITY ENGINE • EVIDENCE-BASED IDENTITY • FAIL-CLOSED OPERATION POLICY",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = $"الجهاز: {_device.Manufacturer} {_device.Model} • رقم الموديل: {_device.ModelNumber} • Serial: {Mask(_device.Serial)}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
        });
        header.Children.Add(new TextBlock
        {
            Text = "هذه الشاشة تقرأ الأدلة وتعرض ما يدعمه التطبيق فعلًا. لا تشغّل عمليات تفليش أو فتح Bootloader أو مسح بيانات أو استعادة EDL/BROM.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#F0C9A5"),
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.Children.Add(_refreshButton);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        _report.IsReadOnly = true;
        _report.AcceptsReturn = true;
        _report.TextWrapping = TextWrapping.Wrap;
        _report.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _report.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _report.Background = Brush("#071625");
        _report.Foreground = Brush("#DCEBFA");
        _report.BorderBrush = Brush("#263D58");
        _report.Padding = new Thickness(12);
        _report.FontFamily = new FontFamily("Consolas");
        _report.FontSize = 11;
        _report.Text = "جارٍ تحميل محرك القدرات...";
        root.Children.Add(_report);

        _status.Text = "بانتظار إعادة فحص الاتصال والقدرات.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private async Task RefreshAsync()
    {
        if (_busy)
            return;

        _busy = true;
        _refreshButton.IsEnabled = false;
        _status.Text = "جارٍ إعادة اكتشاف ADB وFastboot وتحميل ملفات تعريف الأجهزة...";
        try
        {
            var adbAuthorized = false;
            try
            {
                var current = await _discovery.DiscoverAsync();
                adbAuthorized = _wasAuthorized && current.Any(item =>
                    item.Serial.Equals(_device.Serial, StringComparison.Ordinal) &&
                    item.IsAuthorized);
            }
            catch
            {
                adbAuthorized = false;
            }

            var fastbootDetected = false;
            if (_fastboot.IsAvailable)
            {
                try
                {
                    var fastbootDevices = await _fastboot.DiscoverAsync();
                    fastbootDetected = fastbootDevices.Any(item =>
                        item.Serial.Equals(_device.Serial, StringComparison.Ordinal));
                }
                catch
                {
                    fastbootDetected = false;
                }
            }

            var catalog = await _profiles.LoadAsync();
            var identity = DeviceCapabilityEngine.ResolveIdentity(_device, catalog.Profiles);

            var verifiedBoardMap = false;
            try
            {
                var map = await _maps.LoadBestMapAsync(_device, identity.Profile);
                verifiedBoardMap = BoardMapCatalog.IsVerifiedForDevice(map, _device);
            }
            catch
            {
                verifiedBoardMap = false;
            }

            var report = DeviceCapabilityEngine.Evaluate(
                new DeviceCapabilityContext(
                    _device,
                    adbAuthorized,
                    AgentConnected: _agentWasConnected && adbAuthorized,
                    FastbootDetected: fastbootDetected,
                    VerifiedBoardMapAvailable: verifiedBoardMap),
                catalog.Profiles);

            _report.Text = FormatReport(report, catalog);
            _status.Text =
                $"اكتمل التقييم: هوية الجهاز «{IdentityLabel(report.Identity.State)}»، " +
                $"العمليات المسموح بها {report.Decisions.Count(item => item.CanExecute)} من أصل {report.Decisions.Count}. " +
                "المحجوب لا يتحول إلى مسموح بمجرد تشابه الموديل.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تقييم القدرات: " + ex.Message;
            _report.Text = "لم يصدر محرك SoftBox أي حكم دعم بسبب فشل تحميل الأدلة.\n" +
                           "لا يتم افتراض دعم أي عملية عند فشل التقييم.";
        }
        finally
        {
            _busy = false;
            _refreshButton.IsEnabled = true;
        }
    }

    private static string FormatReport(DeviceCapabilityReport report, DeviceProfileCatalogDocument catalog)
    {
        var text = new StringBuilder();
        text.AppendLine("SOFTBOX DEVICE CAPABILITY ENGINE");
        text.AppendLine("EVALUATED UTC: " + report.EvaluatedAtUtc.ToString("O"));
        text.AppendLine("PROFILE CATALOG: " + catalog.CatalogVersion);
        text.AppendLine("PROFILE COUNT: " + catalog.Profiles.Count);
        text.AppendLine();

        text.AppendLine("DEVICE IDENTITY");
        text.AppendLine("STATE: " + IdentityLabel(report.Identity.State));
        text.AppendLine("PROFILE: " + report.Identity.DisplayName);
        text.AppendLine("SCORE: " + report.Identity.Score);
        text.AppendLine("EXACT MODEL NUMBER: " + report.Identity.ExactModelNumberMatched);
        text.AppendLine("MANUFACTURER MATCH: " + report.Identity.ManufacturerMatched);
        text.AppendLine("IDENTITY SOURCE VERIFIED: " + report.Identity.VerifiedIdentitySource);
        foreach (var reason in report.Identity.Reasons)
            text.AppendLine("EVIDENCE: " + reason);
        foreach (var conflict in report.Identity.Conflicts)
            text.AppendLine("CONFLICT: " + conflict);

        text.AppendLine();
        text.AppendLine("CAPABILITY DECISIONS");
        foreach (var item in report.Decisions)
        {
            text.AppendLine();
            text.AppendLine($"[{DecisionLabel(item.State)}] {item.DisplayName}");
            text.AppendLine("CAN EXECUTE: " + item.CanExecute);
            text.AppendLine("EVIDENCE: " + item.Evidence);
            text.AppendLine("REQUIRED ACTION: " + item.RequiredAction);
        }

        text.AppendLine();
        text.AppendLine("ENGINE SAFETY RULES");
        foreach (var rule in report.SafetyRules)
            text.AppendLine("• " + rule);

        text.AppendLine();
        text.AppendLine("This is a capability policy report, not a hardware pass/fail certificate.");
        return text.ToString();
    }

    private static string IdentityLabel(DeviceIdentityState state) => state switch
    {
        DeviceIdentityState.Exact => "EXACT VERIFIED IDENTITY",
        DeviceIdentityState.Probable => "PROBABLE — NOT EXACT",
        DeviceIdentityState.Ambiguous => "AMBIGUOUS — NO PROFILE SELECTED",
        DeviceIdentityState.Conflicting => "CONFLICTING IDENTITY EVIDENCE",
        _ => "UNKNOWN"
    };

    private static string DecisionLabel(CapabilityDecisionState state) => state switch
    {
        CapabilityDecisionState.Supported => "SUPPORTED",
        CapabilityDecisionState.NeedsSetup => "NEEDS SETUP",
        CapabilityDecisionState.ReviewRequired => "REVIEW REQUIRED",
        CapabilityDecisionState.Blocked => "BLOCKED",
        _ => "UNKNOWN"
    };

    private static string Mask(string serial)
        => serial.Length > 4 ? "••••" + serial[^4..] : "••••";

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 150,
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
}
