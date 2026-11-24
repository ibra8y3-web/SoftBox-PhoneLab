using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Live Windows USB PnP inventory for Android bootloader and vendor service modes.
/// This remains independent from ADB so a device does not disappear when Android shuts ADB down.
/// </summary>
public sealed class MaintenanceModeCenterWindow : Window
{
    private readonly WindowsUsbInventoryService _inventory;
    private readonly Func<string, Task<bool>> _authorizeAndAudit;
    private readonly ObservableCollection<MaintenanceModeDevice> _devices = [];
    private readonly ListBox _deviceList = new();
    private readonly TextBox _details = new();
    private readonly TextBlock _status = new();
    private readonly Button _firmwareButton;
    private readonly Button _toolButton;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _refreshing;

    public MaintenanceModeCenterWindow(WindowsUsbInventoryService inventory, Func<string, Task<bool>> authorizeAndAudit)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _authorizeAndAudit = authorizeAndAudit ?? throw new ArgumentNullException(nameof(authorizeAndAudit));
        Title = "SoftBox | Download & Service Mode Center";
        Width = 1060;
        Height = 760;
        MinWidth = 820;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _firmwareButton = ButtonControl("فتح مركز Firmware", "#14517A", OpenFirmware_Click);
        _toolButton = ButtonControl("فتح أداة الوضع المكتشف", "#176044", OpenTool_Click);
        _toolButton.IsEnabled = false;
        _timer.Tick += async (_, _) => await RefreshAsync();

        Content = BuildContent();
        _deviceList.SelectionChanged += (_, _) => SelectionChanged();
        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "مركز Download Mode وأوضاع الصيانة",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(Text(
            "اكتشاف مستقل من Windows USB PnP لجميع الشركات؛ لا يعتمد على ADB. يعرض أوضاع Samsung وFastboot وQualcomm وMediaTek وواجهات Recovery/Download التي تعلنها شركات أخرى وApple DFU/Recovery كأدلة منفصلة. الواجهة غير المعروفة تظهر للقراءة فقط دون اختيار بروتوكول تخميني.",
            "#B7C9DE", 10));
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(ButtonControl("إعادة فحص USB الآن", "#20364F", async (_, _) => await RefreshAsync()));
        actions.Children.Add(_firmwareButton);
        actions.Children.Add(_toolButton);
        header.Children.Add(actions);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.95, GridUnitType.Star) });

        var usbPanel = CreatePanel("واجهات USB المرصودة • تُحدّث تلقائيًا كل ثانيتين", out var usbContent);
        _deviceList.ItemsSource = _devices;
        _deviceList.Background = Brush("#071625");
        _deviceList.Foreground = Brush("#EAF3FF");
        _deviceList.BorderBrush = Brush("#263D58");
        _deviceList.Padding = new Thickness(8);
        _deviceList.ItemTemplate = DeviceTemplate();
        usbContent.Children.Add(_deviceList);
        Grid.SetColumn(usbPanel, 0);
        body.Children.Add(usbPanel);

        var detailPanel = CreatePanel("تفاصيل الجهاز والأدلة", out var detailContent);
        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.Wrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.Background = Brush("#071625");
        _details.Foreground = Brush("#DCEBFA");
        _details.BorderBrush = Brush("#263D58");
        _details.Padding = new Thickness(10);
        _details.Text = "حدد جهاز USB لعرض وضعه وHardware IDs وحالة تعريف Windows.";
        detailContent.Children.Add(_details);
        Grid.SetColumn(detailPanel, 1);
        body.Children.Add(detailPanel);
        root.Children.Add(body);

        var footer = Text(
            "حدود مهمة: معرّف USB قد يحدد الشركة أو الواجهة فقط. Huawei/OPPO/Xiaomi/Apple وغيرها تظهر في وضع الخدمة عند إعلان Windows قرينة صريحة؛ البروتوكولات الخاصة لا تتحول تلقائيًا إلى أدوات تفليش. لا يوجد تجاوز قفل أو اختيار Programmer/DA أو Flash تلقائي.",
            "#F0C9A5", 10);
        footer.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        return root;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || !IsVisible) return;
        _refreshing = true;
        try
        {
            var usb = await _inventory.EnumeratePresentDevicesAsync();
            var found = MaintenanceModeDiscoveryService.Discover(usb);
            var selectedInstance = (_deviceList.SelectedItem as MaintenanceModeDevice)?.UsbDevice.InstanceId;
            _devices.Clear();
            foreach (var item in found)
                _devices.Add(item);
            _deviceList.SelectedItem = _devices.FirstOrDefault(item =>
                string.Equals(item.UsbDevice.InstanceId, selectedInstance, StringComparison.OrdinalIgnoreCase));
            _status.Text = found.Count == 0
                ? $"Windows PnP رصد {usb.Count:N0} جهاز USB، لكن لم تظهر واجهة صيانة/Bootloader معروفة. لو الهاتف لا يظهر هنا، افحص الكابل ومنفذ USB وتعريف الشركة في Device Manager."
                : $"تم رصد {found.Count:N0} واجهة صيانة من أصل {usb.Count:N0} جهاز USB في Windows. هذه قراءة PnP مستقلة عن ADB.";
            if (_deviceList.SelectedItem is null)
                _details.Text = "لا يوجد جهاز صيانة محدد.\n\n" + string.Join(Environment.NewLine,
                    found.Select(item => $"{item.ManufacturerLabel} • {item.ModeEvidence.Mode} • {item.HardwareId}"));
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فحص Windows USB PnP: " + ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SelectionChanged()
    {
        if (_deviceList.SelectedItem is not MaintenanceModeDevice item)
        {
            _firmwareButton.IsEnabled = false;
            _toolButton.IsEnabled = false;
            return;
        }
        _details.Text = item.Details;
        _firmwareButton.IsEnabled = true;
        _toolButton.IsEnabled = item.IsSamsungDownloadMode || item.IsFastboot ||
                                item.IsQualcommEdl || item.IsMediaTekService;
        _toolButton.Content = item.IsSamsungDownloadMode ? "فتح Samsung Odin Assistant (فحص الحزم)" :
            item.IsFastboot ? "فتح Fastboot Manager" :
            item.IsQualcommEdl ? "فتح Qualcomm USB Doctor" :
            item.IsMediaTekService ? "فتح MediaTek USB Doctor" :
            item.IsAppleRecoveryOrDfu ? "Apple Recovery/DFU — عرض الأدلة فقط" :
            item.IsUnclassifiedVendorServiceMode ? "واجهة خدمة غير مصنفة — عرض الأدلة فقط" :
            "لا توجد أداة خدمة محددة";
    }

    private async void OpenFirmware_Click(object sender, RoutedEventArgs e)
    {
        if (_deviceList.SelectedItem is not MaintenanceModeDevice item)
        {
            _status.Text = "حدد واجهة USB أولًا.";
            return;
        }
        if (!await _authorizeAndAudit("OpenFirmwareCenter")) return;
        new FirmwareCenterWindow(
            deviceSnapshot: null,
            adbAuthorized: false,
            detectedManufacturer: item.ManufacturerLabel,
            detectedMode: item.ModeEvidence.Mode,
            detectedHardwareId: item.HardwareId)
        { Owner = this }.Show();
    }

    private async void OpenTool_Click(object sender, RoutedEventArgs e)
    {
        if (_deviceList.SelectedItem is not MaintenanceModeDevice item)
            return;

        if (item.IsSamsungDownloadMode)
        {
            if (!await _authorizeAndAudit("OpenSamsungOdinAssistant")) return;
            var suffix = WindowsUsbInstanceIdentifierParser.TryExtractInstanceSuffix(
                item.UsbDevice.InstanceId, out var parsed) ? parsed : null;
            new SamsungOdinAssistantWindow(
                adb: null, deviceSerial: null, deviceManufacturer: "Samsung", deviceModel: null,
                pnpInstanceSuffix: suffix,
                downloadModeComPort: item.ComPort,
                downloadModeInstanceId: item.UsbDevice.InstanceId,
                downloadModeHardwareId: item.HardwareId,
                downloadModeModeEvidence: item.ModeEvidence.Mode,
                authorizeAndAudit: _authorizeAndAudit)
            { Owner = this }.Show();
        }
        else if (item.IsFastboot)
        {
            if (!await _authorizeAndAudit("OpenFastbootManager")) return;
            new FastbootManagerWindow { Owner = this }.Show();
        }
        else if (item.IsQualcommEdl)
        {
            if (!await _authorizeAndAudit("OpenQualcommUsbDiagnostics")) return;
            new UsbDoctorWindow("qualcomm") { Owner = this }.Show();
        }
        else if (item.IsMediaTekService)
        {
            if (!await _authorizeAndAudit("OpenMediaTekUsbDiagnostics")) return;
            new UsbDoctorWindow("mediatek") { Owner = this }.Show();
        }
    }

    private static DataTemplate DeviceTemplate()
    {
        var template = new DataTemplate(typeof(MaintenanceModeDevice));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brush("#132238"));
        border.SetValue(Border.BorderBrushProperty, Brush("#263E58"));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(9));
        border.SetValue(Border.MarginProperty, new Thickness(0, 2, 0, 4));

        var stack = new FrameworkElementFactory(typeof(StackPanel));
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MaintenanceModeDevice.DisplayName)));
        title.SetValue(TextBlock.ForegroundProperty, Brush("#EAF3FF"));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        stack.AppendChild(title);

        var mode = new FrameworkElementFactory(typeof(TextBlock));
        mode.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("ModeEvidence.Mode"));
        mode.SetValue(TextBlock.ForegroundProperty, Brush("#7DD3FC"));
        mode.SetValue(TextBlock.MarginProperty, new Thickness(0, 5, 0, 0));
        mode.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        stack.AppendChild(mode);

        var ids = new FrameworkElementFactory(typeof(TextBlock));
        ids.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MaintenanceModeDevice.HardwareId)));
        ids.SetValue(TextBlock.ForegroundProperty, Brush("#9FB4CC"));
        ids.SetValue(TextBlock.FontSizeProperty, 10d);
        stack.AppendChild(ids);

        var driver = new FrameworkElementFactory(typeof(TextBlock));
        driver.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MaintenanceModeDevice.DriverStatus)));
        driver.SetValue(TextBlock.ForegroundProperty, Brush("#B8C9DA"));
        driver.SetValue(TextBlock.FontSizeProperty, 9d);
        driver.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
        stack.AppendChild(driver);
        border.AppendChild(stack);
        template.VisualTree = border;
        return template;
    }

    private static Border CreatePanel(string title, out DockPanel content)
    {
        content = new DockPanel();
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(heading, Dock.Top);
        content.Children.Add(heading);
        return new Border
        {
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 8, 0),
            Child = content
        };
    }

    private static Button ButtonControl(string text, string color, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 7, 7),
            Background = Brush(color),
            Foreground = Brushes.White,
            BorderBrush = Brush("#35516F")
        };
        button.Click += click;
        return button;
    }

    private static TextBlock Text(string text, string color, double size)
        => new()
        {
            Text = text,
            Foreground = Brush(color),
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        };

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
