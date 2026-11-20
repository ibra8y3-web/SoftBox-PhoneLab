using SoftBox.PhoneLab.Desktop.Services;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Displays real read-only network observations from one explicitly selected authorized ADB device.
/// </summary>
public sealed class AndroidNetworkDiagnosticsWindow : Window
{
    private readonly AndroidNetworkDiagnosticsService _service;
    private readonly string _serial;
    private readonly TextBox _results = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _inspectButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _operation;

    public AndroidNetworkDiagnosticsWindow(AdbClient adb, string authorizedSerial, string deviceName)
    {
        ArgumentNullException.ThrowIfNull(adb);
        if (!AdbClient.IsSafeSerial(authorizedSerial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(authorizedSerial));

        _service = new AndroidNetworkDiagnosticsService(adb);
        _serial = authorizedSerial;
        Title = "SoftBox | تشخيص شبكة Android";
        Width = 940;
        Height = 730;
        MinWidth = 720;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _inspectButton = ButtonControl("قراءة حالة الشبكة الآن", "#14517A", Inspect_Click);
        _cancelButton = ButtonControl("إلغاء", "#51333A", Cancel_Click);
        _cancelButton.IsEnabled = false;
        Content = BuildContent(deviceName);
        Closed += (_, _) => _operation?.Cancel();
        Loaded += (_, _) => _ = InspectAsync();
    }

    private UIElement BuildContent(string deviceName)
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "تشخيص شبكة الهاتف",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "ANDROID ADB NETWORK INSPECTION • READ ONLY",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 7)
        });
        header.Children.Add(new TextBlock
        {
            Text = "الجهاز: " + deviceName + "    •    ADB ID: " + MaskSerial(_serial),
            Foreground = Brush("#B7C9DE"),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var note = new Border
        {
            Background = Brush("#211D2A"),
            BorderBrush = Brush("#66516B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock
            {
                Text = "يقرأ SoftBox عناوين IP العامة التي يعرضها Android، ومسار الشبكة الافتراضي والبوابة واسم واجهة الاتصال، وحالتي Wi-Fi ووضع الطيران من إعدادات النظام. لا يقرأ اسم شبكة Wi-Fi (SSID) أو كلمة المرور أو حركة التطبيقات. قد تمنع نسخة Android أو الشركة المصنعة بعض الأوامر؛ عندها ستظهر القيمة «غير متاحة» مع سببها، ولن تُستبدل بتخمين.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(_inspectButton);
        _cancelButton.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(_cancelButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _results.IsReadOnly = true;
        _results.AcceptsReturn = true;
        _results.TextWrapping = TextWrapping.Wrap;
        _results.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _results.Background = Brush("#071625");
        _results.Foreground = Brush("#DCEBFA");
        _results.BorderBrush = Brush("#263D58");
        _results.Padding = new Thickness(12);
        _results.FontFamily = new FontFamily("Consolas");
        _results.FontSize = 11;
        _results.Text = "في انتظار قراءة الجهاز...";
        root.Children.Add(_results);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _status.Text = "يتم جمع قراءات حقيقية من الجهاز المحدد عبر ADB.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(_status);
        _progress.Height = 4;
        _progress.IsIndeterminate = true;
        _progress.Foreground = Brush("#38BDF8");
        _progress.Visibility = Visibility.Collapsed;
        footer.Children.Add(_progress);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        return root;
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e) => await InspectAsync();

    private async Task InspectAsync()
    {
        if (_operation is not null)
            return;

        _operation = new CancellationTokenSource();
        SetBusy(true, "جارٍ قراءة واجهات وعناوين ومسارات شبكة Android عبر ADB...");
        try
        {
            var snapshot = await _service.InspectAsync(_serial, _operation.Token);
            _results.Text = Format(snapshot);
            _status.Text = "اكتملت قراءة أوامر ADB. هذه معلومات الشبكة التي أتاحها النظام في وقت الفحص فقط.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء قراءة شبكة الهاتف.";
        }
        catch (Exception ex)
        {
            _results.Text = ex.ToString();
            _status.Text = "تعذر إكمال قراءة شبكة الهاتف: " + ex.Message;
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false, _status.Text);
        }
    }

    private static string Format(AndroidNetworkDiagnosticsSnapshot snapshot)
    {
        var output = new StringBuilder();
        output.AppendLine("ANDROID NETWORK SNAPSHOT");
        output.AppendLine("Collected UTC: " + snapshot.CollectedAtUtc.ToString("O"));
        output.AppendLine("Device: " + MaskSerial(snapshot.DeviceSerial));
        output.AppendLine();

        output.AppendLine("DEFAULT ROUTE");
        output.AppendLine("Interface: " + (snapshot.DefaultInterface ?? "غير متاح"));
        output.AppendLine("Gateway: " + (snapshot.DefaultGateway ?? "غير متاح"));
        output.AppendLine();

        output.AppendLine("IP ADDRESSES");
        output.AppendLine("IPv4:");
        output.AppendLine(snapshot.Ipv4Addresses.Count == 0
            ? "  غير متاح"
            : string.Join(Environment.NewLine, snapshot.Ipv4Addresses.Select(x => "  " + x)));
        output.AppendLine("IPv6:");
        output.AppendLine(snapshot.Ipv6Addresses.Count == 0
            ? "  غير متاح"
            : string.Join(Environment.NewLine, snapshot.Ipv6Addresses.Select(x => "  " + x)));
        output.AppendLine();

        output.AppendLine("RADIO / SYSTEM SETTINGS");
        output.AppendLine("Wi-Fi enabled: " + Toggle(snapshot.WifiEnabled));
        output.AppendLine("Airplane mode: " + Toggle(snapshot.AirplaneModeEnabled));
        output.AppendLine();

        output.AppendLine("UNAVAILABLE / PARTIAL READS");
        output.AppendLine(snapshot.UnavailableCommands.Count == 0
            ? "لا توجد أوامر أبلغت عن فشل أو غياب مخرجات."
            : string.Join(Environment.NewLine, snapshot.UnavailableCommands.Select(x => "- " + x)));

        return output.ToString();
    }

    private static string Toggle(bool? value) => value switch
    {
        true => "مفعّل",
        false => "متوقف",
        null => "غير متاح"
    };

    private static string MaskSerial(string serial)
        => serial.Length <= 6 ? "••••••" : "••••••" + serial[^6..];

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _inspectButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(14, 9, 14, 9),
            MinWidth = 145,
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
