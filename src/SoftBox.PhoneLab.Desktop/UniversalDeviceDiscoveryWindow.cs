using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Presents read-only Windows PnP discovery and ADB device discovery as separate evidence sources.
/// It never treats MTP/USB presence as proof of screen mirroring or remote-control capability.
/// </summary>
public sealed class UniversalDeviceDiscoveryWindow : Window
{
    private readonly WindowsUsbInventoryService _inventory;
    private readonly IReadOnlyList<DeviceListItem> _adbDevices;
    private readonly StackPanel _usbResults = new();
    private readonly StackPanel _adbResults = new();
    private readonly TextBlock _status = new();

    public UniversalDeviceDiscoveryWindow(
        WindowsUsbInventoryService inventory,
        IReadOnlyList<DeviceListItem> adbDevices)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _adbDevices = adbDevices ?? Array.Empty<DeviceListItem>();
        Title = "SoftBox | Universal Device Discovery";
        Width = 1040;
        Height = 780;
        MinWidth = 820;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "محرك اكتشاف الهواتف والأجهزة العالمي",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(Text(
            "يجمع نتائج Windows USB PnP ونتائج ADB في مسارين منفصلين. قد يعرض Windows اسم الشركة أو VID/PID دون معرفة موديل الهاتف، كما أن MTP لا يعني أن الشاشة قابلة للعرض أو التحكم.",
            "#B7C9DE", 10));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Button("إعادة فحص USB", "#14517A", async (_, _) => await RefreshAsync()));
        actions.Children.Add(Button("فتح مركز تعريفات الشركات", "#20364F", (_, _) =>
            new UsbDriverCenterWindow { Owner = this }.ShowDialog()));
        header.Children.Add(actions);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Border
        {
            Background = Brush("#171F2C"),
            BorderBrush = Brush("#67532F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 0),
            Child = Text(
                "قواعد النقل: Android + ADB مصرح به قد يدعم scrcpy؛ iPhone يحتاج AirPlay للعرض فقط؛ MTP مخصص لنقل الملفات؛ Fastboot/EDL/Preloader أوضاع خدمة وليست جلسات شاشة. لا تُخمن SoftBox بروتوكولًا غير معروف.",
                "#EBCB9B", 10)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

        var usbPanel = Panel("أجهزة Windows USB المكتشفة", out var usbContent);
        usbContent.Children.Add(_usbResults);
        Grid.SetColumn(usbPanel, 0);
        body.Children.Add(usbPanel);

        var adbPanel = Panel("أجهزة ADB التي أعلنها Android Platform-Tools", out var adbContent);
        adbContent.Children.Add(_adbResults);
        Grid.SetColumn(adbPanel, 1);
        body.Children.Add(adbPanel);

        root.Children.Add(body);
        return root;
    }

    private async Task RefreshAsync()
    {
        _status.Text = "جارٍ فحص أجهزة Windows USB PnP... لا يتم تثبيت تعريفات أو تغيير إعدادات أي جهاز.";
        _usbResults.Children.Clear();
        try
        {
            var devices = await _inventory.EnumeratePresentDevicesAsync();
            if (devices.Count == 0)
                _usbResults.Children.Add(Text("لم يعرض Windows أي جهاز USB متصل حاليًا.", "#AFC7DF", 10));

            foreach (var device in devices)
            {
                var resource = OemUsbDriverCatalog.FindForUsbIdentity(
                    device.Manufacturer, device.FriendlyName, device.VendorId);
                var transport = UniversalDeviceTransportEngine.Assess(
                    device.Manufacturer, device.FriendlyName, device.VendorId, device.ProductId,
                    adbAuthorized: false);

                var card = new Border
                {
                    Background = Brush("#10243A"),
                    BorderBrush = Brush("#2D4A68"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(9),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var stack = new StackPanel();
                stack.Children.Add(Text(device.FriendlyName, "#EAF3FF", 11));
                stack.Children.Add(Text(
                    $"الشركة: {Fallback(device.Manufacturer)} • VID/PID: {device.VendorId}:{device.ProductId}",
                    "#8FC7EA", 9));
                stack.Children.Add(Text(
                    $"PnP: {device.PnpStatus} • Problem Code: {device.ProblemCode} • Service: {Fallback(device.Service)}",
                    device.ProblemCode == 0 ? "#9FC7E8" : "#FFB4A6", 9));
                stack.Children.Add(Text(
                    $"{transport.DeviceFamily} — {transport.UsbTransport}\nالعرض: {transport.ScreenViewingRoute}\nالتحكم: {transport.RemoteControlRoute}\nالحدود: {transport.Limitation}",
                    "#B7C9DE", 9));

                if (resource is not null)
                {
                    stack.Children.Add(Text(
                        $"المورد الرسمي المطابق: {resource.Name} • {resource.ResourceKind}",
                        "#7DDBAA", 9));
                    stack.Children.Add(Button(
                        "فتح مصدر الشركة الرسمي",
                        "#20364F",
                        (_, _) => OpenOfficialUrl(resource.OfficialUrl)));
                }
                else
                {
                    stack.Children.Add(Text(
                        "لا يوجد مورد رسمي مطابق آليًا. لم يتم تخمين الشركة أو تعريف الجهاز.",
                        "#F0C9A5", 9));
                }

                card.Child = stack;
                _usbResults.Children.Add(card);
            }

            _adbResults.Children.Clear();
            if (_adbDevices.Count == 0)
                _adbResults.Children.Add(Text("لم يعرض ADB أجهزة في هذه الجلسة. تحقق من Platform-Tools، أو استخدم مسار الشركة الرسمي.", "#AFC7DF", 10));
            foreach (var device in _adbDevices)
            {
                var card = new Border
                {
                    Background = Brush("#10243A"),
                    BorderBrush = Brush("#2D4A68"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(9),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var stack = new StackPanel();
                stack.Children.Add(Text(device.DisplayName, "#EAF3FF", 11));
                stack.Children.Add(Text($"الحالة: {device.StateText} • Serial: {device.MaskedSerial}", device.IsAuthorized ? "#7DDBAA" : "#F0C9A5", 9));
                stack.Children.Add(Text(
                    device.IsAuthorized
                        ? "قناة ADB مصرح بها. مسار العرض والتحكم المتاح حاليًا هو scrcpy إذا كان هذا الموديل وإصدار Android متوافقين."
                        : "الجهاز لم يصرّح لهذا الكمبيوتر. لا يوجد عرض شاشة أو تحكم قبل موافقة المستخدم على الهاتف.",
                    "#B7C9DE", 9));
                card.Child = stack;
                _adbResults.Children.Add(card);
            }

            _status.Text = $"اكتمل الفحص: {devices.Count} عنصر USB و{_adbDevices.Count} جهاز/حالة ADB. لا يعني وجودهما أن لكل USB جهاز ADB مطابقًا؛ لم يتم الربط بالتخمين.";
        }
        catch (Exception ex)
        {
            _status.Text = "فشل فحص Windows USB PnP: " + ex.Message;
        }
    }

    private static Border Panel(string title, out StackPanel content)
    {
        content = new StackPanel();
        content.Children.Add(Text(title, "#7DD3FC", 13));
        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        return new Border
        {
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 8, 0),
            Child = scroll
        };
    }

    private static string Fallback(string? value)
        => string.IsNullOrWhiteSpace(value) ? "غير متاح" : value;

    private static void OpenOfficialUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
    }

    private static Button Button(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 6, 0, 2),
            Background = Brush(color),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096")
        };
        button.Click += handler;
        return button;
    }

    private static TextBlock Text(string value, string color, double size)
        => new() { Text = value, Foreground = Brush(color), FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };

    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value)!);
}
