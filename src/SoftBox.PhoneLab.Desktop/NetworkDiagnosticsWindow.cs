using SoftBox.PhoneLab.Core.Network;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Local Windows host-network diagnostics. Android-private network details are not inferred.
/// </summary>
public sealed class NetworkDiagnosticsWindow : Window
{
    private readonly NetworkDiagnosticsService _service = new();
    private readonly AndroidNetworkDiagnosticsService? _androidService;
    private readonly string? _androidSerial;
    private readonly ObservableCollection<string> _interfaces = [];
    private readonly TextBox _host = new();
    private readonly TextBox _results = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _probeButton;
    private readonly Button _androidInspectButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _operation;

    public NetworkDiagnosticsWindow(IAdbCommandRunner? adb = null, string? selectedAndroidSerial = null)
    {
        if (adb is not null && !string.IsNullOrWhiteSpace(selectedAndroidSerial) && AdbClient.IsSafeSerial(selectedAndroidSerial))
        {
            _androidService = new AndroidNetworkDiagnosticsService(adb);
            _androidSerial = selectedAndroidSerial;
        }
        Title = "SoftBox | تشخيص الشبكات — Network Diagnostics";
        Width = 980;
        Height = 740;
        MinWidth = 760;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _probeButton = ButtonControl("اختبار DNS وPing", "#14517A", Probe_Click);
        _androidInspectButton = ButtonControl("قراءة شبكة Android", "#0F6251", InspectAndroid_Click);
        _androidInspectButton.IsEnabled = _androidService is not null;
        _androidInspectButton.ToolTip = _androidService is null
            ? "اختر هاتف Android مصرحًا به عبر ADB من مركز الأجهزة أولًا."
            : "قراءة عناوين واجهات ومسارات الشبكة التي يعرضها Android عبر ADB.";
        _cancelButton = ButtonControl("إلغاء", "#51333A", Cancel_Click);
        _cancelButton.IsEnabled = false;
        Content = BuildContent();
        Loaded += (_, _) => RefreshInterfaces();
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "تشخيص الشبكات",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "WINDOWS HOST NETWORK • LOCAL INTERFACES • DNS • ICMP",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "يعرض واجهات الشبكة وعناوين IP والبوابات وخوادم DNS التي يتيحها Windows، ثم يختبر اسم نطاق أو IP تختاره أنت. لا يجري مسح منافذ أو يجمع بيانات خاصة من الهاتف.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 9, 0, 0),
            FontSize = 12
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Border
        {
            Background = Brush("#211D2A"),
            BorderBrush = Brush("#66516B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 12, 0, 0),
            Child = new TextBlock
            {
                Text = "حدود الفحص: هذه بيانات الكمبيوتر المحلي. لا تمثل معلومات Wi-Fi الخاصة بالهاتف ولا تثبت اتصال ADB اللاسلكي. قد تمنع الجدران النارية أو إعدادات الشبكة رد Ping حتى عند نجاح DNS.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 9) };
        actions.Children.Add(ButtonControl("تحديث واجهات الشبكة", "#243B56", (_, _) => RefreshInterfaces()));
        _host.Width = 270;
        _host.Height = 36;
        _host.Margin = new Thickness(0, 0, 8, 0);
        _host.Padding = new Thickness(9, 6, 9, 6);
        _host.Background = Brush("#0D1C2E");
        _host.Foreground = Brush("#EAF3FF");
        _host.BorderBrush = Brush("#35516F");
        _host.ToolTip = "اسم نطاق مثل example.com أو عنوان IP";
        actions.Children.Add(_host);
        actions.Children.Add(_probeButton);
        actions.Children.Add(_androidInspectButton);
        actions.Children.Add(_cancelButton);
        top.Children.Add(actions);

        _status.Text = "جارٍ تجهيز تشخيص الشبكة المحلي...";
        _status.Foreground = Brush("#9FC7E8");
        _status.FontSize = 11;
        _status.Margin = new Thickness(0, 0, 0, 6);
        top.Children.Add(_status);
        _progress.Height = 4;
        _progress.IsIndeterminate = true;
        _progress.Visibility = Visibility.Collapsed;
        _progress.Foreground = Brush("#38BDF8");
        top.Children.Add(_progress);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.95, GridUnitType.Star) });

        var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        var leftTitle = new TextBlock
        {
            Text = "واجهات الشبكة المتاحة في Windows",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#DCEBFA"),
            Margin = new Thickness(2, 0, 2, 8)
        };
        DockPanel.SetDock(leftTitle, Dock.Top);
        left.Children.Add(leftTitle);

        var list = new ListBox
        {
            ItemsSource = _interfaces,
            Background = Brush("#0D1C2E"),
            Foreground = Brush("#DCEBFA"),
            BorderBrush = Brush("#263D58"),
            Padding = new Thickness(8),
            FontSize = 10
        };
        left.Children.Add(list);
        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        var right = new DockPanel { Margin = new Thickness(8, 0, 0, 0) };
        var resultTitle = new TextBlock
        {
            Text = "نتيجة اختبار الهدف المحدد",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#DCEBFA"),
            Margin = new Thickness(2, 0, 2, 8)
        };
        DockPanel.SetDock(resultTitle, Dock.Top);
        right.Children.Add(resultTitle);

        _results.IsReadOnly = true;
        _results.AcceptsReturn = true;
        _results.TextWrapping = TextWrapping.Wrap;
        _results.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _results.Background = Brush("#0D1C2E");
        _results.Foreground = Brush("#DCEBFA");
        _results.BorderBrush = Brush("#263D58");
        _results.Padding = new Thickness(10);
        _results.FontFamily = new FontFamily("Consolas");
        _results.FontSize = 11;
        _results.Text = "أدخل اسم نطاق أو IP واضغط اختبار DNS وPing.";
        right.Children.Add(_results);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);

        root.Children.Add(body);
        return root;
    }

    private void RefreshInterfaces()
    {
        try
        {
            _interfaces.Clear();
            foreach (var item in _service.GetLocalInterfaces())
            {
                var line = new StringBuilder();
                line.AppendLine(item.Name + " — " + item.Status + " — " + item.Type);
                line.AppendLine(item.Description);
                line.AppendLine("IPv4: " + Join(item.Ipv4Addresses));
                line.AppendLine("IPv6: " + Join(item.Ipv6Addresses));
                line.AppendLine("Gateway: " + Join(item.Gateways));
                line.Append("DNS: " + Join(item.DnsServers));
                _interfaces.Add(line.ToString());
            }

            _status.Text = $"تمت قراءة {_interfaces.Count} واجهة شبكة من Windows محليًا. لم يتم إرسال أي طلب خارجي.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر قراءة واجهات الشبكة: " + ex.Message;
        }
    }

    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;

        if (!NetworkDiagnosticsService.IsValidHost(_host.Text))
        {
            MessageBox.Show(this, "أدخل اسم نطاق صحيحًا مثل example.com أو عنوان IP دون https:// أو مسار.",
                "Network Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _operation = new CancellationTokenSource();
        _probeButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        _progress.Visibility = Visibility.Visible;
        _status.Text = "جارٍ حل اسم النطاق عبر DNS ثم إرسال طلب ICMP واحد إلى أول عنوان مناسب...";
        try
        {
            var result = await _service.ProbeHostAsync(_host.Text, _operation.Token);
            var text = new StringBuilder();
            text.AppendLine("Host: " + result.Host);
            text.AppendLine("DNS addresses:");
            foreach (var address in result.ResolvedAddresses)
                text.AppendLine("  " + address);
            text.AppendLine();
            text.AppendLine("Ping target: " + result.PingAddress);
            text.AppendLine("ICMP status: " + result.PingStatus);
            text.AppendLine("Round-trip: " + (result.RoundtripMilliseconds is long roundtrip ? $"{roundtrip} ms" : "غير متاح"));
            text.AppendLine("TTL: " + (result.TimeToLive?.ToString() ?? "غير متاح"));
            _results.Text = text.ToString();
            _status.Text = "انتهى الاختبار. نجاح DNS مستقل عن استجابة ICMP.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء اختبار الشبكة.";
        }
        catch (Exception ex)
        {
            _status.Text = "فشل اختبار الهدف: " + ex.Message;
            _results.Text = ex.ToString();
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _probeButton.IsEnabled = true;
            _cancelButton.IsEnabled = false;
            _progress.Visibility = Visibility.Collapsed;
        }
    }

    private async void InspectAndroid_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;
        if (_androidService is null || string.IsNullOrWhiteSpace(_androidSerial))
        {
            _status.Text = "اختر هاتف Android مصرحًا به عبر ADB أولًا ثم افتح تشخيص الشبكات.";
            return;
        }

        _operation = new CancellationTokenSource();
        _probeButton.IsEnabled = false;
        _androidInspectButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        _progress.Visibility = Visibility.Visible;
        _status.Text = "جارٍ قراءة عناوين واجهات ومسارات الشبكة من هاتف Android المحدد عبر ADB...";
        try
        {
            var snapshot = await _androidService.InspectAsync(_androidSerial, _operation.Token);
            var output = new StringBuilder();
            output.AppendLine("ANDROID DEVICE: " + snapshot.DeviceSerial);
            output.AppendLine("CAPTURED UTC: " + snapshot.CapturedAtUtc.ToString("O"));
            output.AppendLine();
            output.AppendLine("INTERFACE ADDRESSES:");
            output.AppendLine(string.IsNullOrWhiteSpace(snapshot.InterfaceAddressOutput)
                ? "غير متاح من Android."
                : snapshot.InterfaceAddressOutput);
            output.AppendLine();
            output.AppendLine("NETWORK ROUTES:");
            output.AppendLine(string.IsNullOrWhiteSpace(snapshot.RouteOutput)
                ? "غير متاح من Android."
                : snapshot.RouteOutput);
            _results.Text = output.ToString();
            _status.Text = "اكتملت قراءة معلومات الشبكة التي أتاحها Android عبر ADB. هذه قراءة إعدادات شبكة وليست اختبارًا لسرعة الإنترنت أو فحصًا أمنيًا.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء قراءة شبكة Android.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر قراءة شبكة Android: " + ex.Message;
            _results.Text = ex.ToString();
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _probeButton.IsEnabled = true;
            _androidInspectButton.IsEnabled = _androidService is not null;
            _cancelButton.IsEnabled = false;
            _progress.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private static string Join(IEnumerable<string> values)
    {
        var items = values.ToArray();
        return items.Length == 0 ? "غير متاح" : string.Join(", ", items);
    }

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(13, 8, 13, 8),
            MinWidth = 92,
            Margin = new Thickness(0, 0, 7, 0),
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
