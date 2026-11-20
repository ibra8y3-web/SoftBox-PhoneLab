using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Read-only Windows USB/PnP inventory and conservative VID/PID mode hints.
/// No driver installation, removal, restart, flashing or device configuration is performed.
/// </summary>
public sealed class UsbDoctorWindow : Window
{
    private readonly WindowsUsbInventoryService _inventory = new();
    private readonly ObservableCollection<WindowsUsbDevice> _allDevices = [];
    private readonly ObservableCollection<WindowsUsbDevice> _visibleDevices = [];
    private readonly ListBox _devices = new();
    private readonly TextBox _details = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly ComboBox _filter = new();
    private readonly string _initialFilter;
    private readonly Button _refreshButton;
    private CancellationTokenSource? _operation;

    public UsbDoctorWindow(string initialFilter = "all")
    {
        _initialFilter = initialFilter?.Trim().ToLowerInvariant() ?? "all";
        if (_initialFilter is not ("all" or "known" or "problems" or "qualcomm" or "mediatek"))
            throw new ArgumentException("مرشح USB غير معروف.", nameof(initialFilter));

        Title = _initialFilter switch
        {
            "qualcomm" => "SoftBox | Qualcomm USB Diagnostics",
            "mediatek" => "SoftBox | MediaTek USB Diagnostics",
            _ => "SoftBox | طبيب USB — USB Doctor"
        };
        Width = 1120;
        Height = 790;
        MinWidth = 850;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _refreshButton = ButtonControl(_initialFilter switch
        {
            "qualcomm" => "فحص أجهزة Qualcomm EDL",
            "mediatek" => "فحص أجهزة MediaTek Preloader",
            _ => "فحص أجهزة USB المتصلة"
        }, "#14517A", Refresh_Click);
        Content = BuildContent();
        _filter.SelectedValue = _initialFilter;
        _devices.SelectionChanged += (_, _) => ShowSelectedDevice();
        if (_initialFilter is "qualcomm" or "mediatek")
            Loaded += (_, _) => Refresh_Click(this, new RoutedEventArgs());
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = _initialFilter switch
            {
                "qualcomm" => "تشخيص USB — Qualcomm EDL / 9008",
                "mediatek" => "تشخيص USB — MediaTek Preloader / BROM",
                _ => "طبيب USB"
            },
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "WINDOWS PLUG-AND-PLAY • SETUPAPI • USB VID/PID • DEVICE PROBLEM CODES",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = _initialFilter switch
            {
                "qualcomm" => "يعرض أجهزة Windows PnP المطابقة لمؤشر VID/PID الشائع 05C6:9008 فقط. هذا مؤشر اتصال EDL ولا يفتح قناة EDL أو يحدد موديلًا أو programmer متوافقًا.",
                "mediatek" => "يعرض أجهزة Windows PnP المطابقة لمؤشر VID/PID الشائع 0E8D:0003 فقط. هذا مؤشر Preloader/BROM ولا يقرأ البروتوكول أو يختار Download Agent.",
                _ => "يقرأ قائمة أجهزة USB الموجودة حاليًا في Windows ويعرض حالة PnP وVID/PID. معرفات Qualcomm وMediaTek وSamsung وGoogle الظاهرة مؤشرات معروفة فقط وليست إثباتًا لطراز الجهاز أو توافق التفليش."
            },
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var warning = new Border
        {
            Background = Brush("#211D2A"),
            BorderBrush = Brush("#66516B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock
            {
                Text = "وضع القراءة فقط: لا يتم تثبيت أو إزالة أو إعادة تشغيل أي تعريف. لا يتحقق هذا الفحص من توقيع ملفات التعريف أو صلاحية حزم Driver، ولا يتجاوز FRP أو أقفال أو وسائل حماية.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(warning, Dock.Top);
        root.Children.Add(warning);

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        _filter.ItemsSource = new[]
        {
            new FilterItem("all", "كل أجهزة USB"),
            new FilterItem("known", "المعرفات المعروفة فقط"),
            new FilterItem("problems", "الأجهزة ذات مشكلة PnP"),
            new FilterItem("qualcomm", "Qualcomm EDL / 9008"),
            new FilterItem("mediatek", "MediaTek Preloader / BROM")
        };
        _filter.DisplayMemberPath = nameof(FilterItem.Label);
        _filter.SelectedValuePath = nameof(FilterItem.Id);
        _filter.SelectedIndex = 0;
        _filter.SelectionChanged += (_, _) => ApplyFilter();
        _filter.Width = 220;
        DockPanel.SetDock(_filter, Dock.Right);
        toolbar.Children.Add(_filter);
        _refreshButton.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(_refreshButton, Dock.Left);
        toolbar.Children.Add(_refreshButton);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

        _devices.ItemsSource = _visibleDevices;
        _devices.DisplayMemberPath = nameof(WindowsUsbDevice.FriendlyName);
        _devices.Background = Brush("#071625");
        _devices.Foreground = Brush("#DCEBFA");
        _devices.BorderBrush = Brush("#263D58");
        _devices.Padding = new Thickness(8);
        _devices.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(_devices, 0);
        body.Children.Add(_devices);

        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.Wrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.Background = Brush("#071625");
        _details.Foreground = Brush("#DCEBFA");
        _details.BorderBrush = Brush("#263D58");
        _details.Padding = new Thickness(12);
        _details.FontFamily = new FontFamily("Consolas");
        _details.FontSize = 10;
        _details.Text = "اضغط «فحص أجهزة USB المتصلة» لقراءة قاعدة PnP في Windows.";
        _details.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_details, 1);
        body.Children.Add(_details);
        root.Children.Add(body);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Text = "لم يتم إجراء فحص USB بعد.";
        _status.Foreground = Brush("#9FC7E8");
        _status.FontSize = 11;
        footer.Children.Add(_status);
        _progress.Height = 4;
        _progress.IsIndeterminate = true;
        _progress.Visibility = Visibility.Collapsed;
        _progress.Foreground = Brush("#38BDF8");
        footer.Children.Add(_progress);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        return root;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;

        _operation = new CancellationTokenSource();
        _refreshButton.IsEnabled = false;
        _progress.Visibility = Visibility.Visible;
        _status.Text = "جارٍ قراءة أجهزة Windows Plug-and-Play...";
        try
        {
            var devices = await _inventory.EnumeratePresentDevicesAsync(_operation.Token);
            _allDevices.Clear();
            foreach (var device in devices)
                _allDevices.Add(device);

            ApplyFilter();
            _status.Text = $"اكتمل فحص Windows PnP: {_allDevices.Count:N0} جهاز USB حاضر. لم يتم تعديل أي تعريف.";
            if (_allDevices.Count == 0)
                _details.Text = "لم يُرجع Windows أي جهاز USB حاضرًا. لا يعني ذلك عدم وجود أجهزة MTP أو أجهزة موصولة في وضع غير متاح للنظام.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء فحص USB.";
        }
        catch (Exception ex)
        {
            _status.Text = "فشل فحص Windows PnP: " + ex.Message;
            MessageBox.Show(this, ex.Message, "USB Doctor",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _refreshButton.IsEnabled = true;
            _progress.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyFilter()
    {
        var mode = (_filter.SelectedItem as FilterItem)?.Id ?? "all";
        _visibleDevices.Clear();
        foreach (var device in _allDevices.Where(device => mode switch
                 {
                     "known" => device.Identity.Category != "Unknown",
                     "problems" => device.ProblemCode > 0,
                     "qualcomm" => device.Identity.Category.Equals("Qualcomm", StringComparison.OrdinalIgnoreCase),
                     "mediatek" => device.Identity.Category.Equals("MediaTek", StringComparison.OrdinalIgnoreCase),
                     _ => true
                 }))
            _visibleDevices.Add(device);
        ShowSelectedDevice();
    }

    private void ShowSelectedDevice()
    {
        if (_devices.SelectedItem is not WindowsUsbDevice device)
            return;

        var text = new StringBuilder();
        text.AppendLine("DEVICE: " + device.FriendlyName);
        text.AppendLine("MANUFACTURER: " + (string.IsNullOrWhiteSpace(device.Manufacturer) ? "Unavailable" : device.Manufacturer));
        text.AppendLine("CLASS: " + (string.IsNullOrWhiteSpace(device.DeviceClass) ? "Unavailable" : device.DeviceClass));
        text.AppendLine("INSTANCE ID: " + device.InstanceId);
        text.AppendLine("VID: " + device.VendorId);
        text.AppendLine("PID: " + device.ProductId);
        text.AppendLine("PnP STATUS: " + device.PnpStatus);
        text.AppendLine("PROBLEM CODE: " + (device.ProblemCode == 0 ? "None reported" : device.ProblemCode.ToString()));
        text.AppendLine("DRIVER SERVICE: " + (string.IsNullOrWhiteSpace(device.Service) ? "Unavailable" : device.Service));
        text.AppendLine();
        text.AppendLine("IDENTITY HINT: " + device.Identity.ModeHint);
        text.AppendLine("CATEGORY: " + device.Identity.Category);
        text.AppendLine("LIMITATION: " + device.Identity.Limitation);
        text.AppendLine();
        text.AppendLine("No driver signature or firmware compatibility conclusion is made by this inventory.");
        _details.Text = text.ToString();
    }

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 7, 12, 7),
            MinWidth = 130,
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

    private sealed record FilterItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
