using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Pair an Android device through Android's official Wireless debugging pairing-code flow.
/// The pairing code is held only for the operation and is not saved by SoftBox.
/// </summary>
public sealed class WirelessAdbPairingWindow : Window
{
    private readonly WirelessAdbPairingService _service;
    private readonly TextBox _pairingHost = new();
    private readonly TextBox _pairingPort = new();
    private readonly TextBox _connectionHost = new();
    private readonly TextBox _connectionPort = new();
    private readonly PasswordBox _pairingCode = new();
    private readonly TextBox _output = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _pairButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _operation;

    public WirelessAdbPairingWindow(AdbClient adb)
    {
        ArgumentNullException.ThrowIfNull(adb);
        _service = new WirelessAdbPairingService(adb);
        Title = "SoftBox | اقتران ADB اللاسلكي";
        Width = 900;
        Height = 760;
        MinWidth = 720;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _pairButton = ButtonControl("اقتران ثم اتصال", "#14517A", Pair_Click);
        _cancelButton = ButtonControl("إلغاء", "#6B3D22", Cancel_Click);
        _cancelButton.IsEnabled = false;
        Content = BuildContent();
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "اتصال Android عبر Wi-Fi",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "ANDROID WIRELESS DEBUGGING • ADB PAIR + CONNECT",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var instructions = new Border
        {
            Background = Brush("#10263A"),
            BorderBrush = Brush("#285276"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock
            {
                Text = "على الهاتف: فعّل خيارات المطوّر ثم Wireless debugging. اختر Pair device with pairing code وأدخل IP ومنفذ الاقتران من نافذة الاقتران. بعد نجاحه، أدخل IP ومنفذ الاتصال من شاشة Wireless debugging الرئيسية؛ قد يختلف العنوان أو المنفذ بين الشاشتين. يجب أن يكون الهاتف والكمبيوتر على شبكة Wi-Fi نفسها. مدعوم على هواتف Android 11 فأحدث.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#C7D9EA"),
                FontSize = 12
            }
        };
        DockPanel.SetDock(instructions, Dock.Top);
        root.Children.Add(instructions);

        var fields = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < 3; index++)
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddField(fields, "IP من نافذة Pair device", _pairingHost, 0, 0, "عنوان IP الظاهر بجوار رمز الاقتران");
        AddField(fields, "منفذ الاقتران", _pairingPort, 0, 1, "المنفذ الظاهر في نافذة Pair device");
        AddField(fields, "IP من شاشة Wireless debugging", _connectionHost, 1, 0, "قد يكون مختلفًا عن عنوان الاقتران");
        AddField(fields, "منفذ الاتصال", _connectionPort, 1, 1, "المنفذ الظاهر في شاشة Wireless debugging الرئيسية");
        AddField(fields, "رمز الاقتران المؤقت", _pairingCode, 2, 0, "ستة أرقام من نافذة Pair device");
        _pairingCode.MaxLength = 6;
        _pairingCode.PasswordChar = '●';

        var note = new TextBlock
        {
            Text = "لا تغلق نافذة رمز الاقتران في الهاتف قبل نجاح ADB pair. لا يتم حفظ الرمز في SoftBox.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#9CB3CC"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 10, 10, 0)
        };
        Grid.SetRow(note, 2);
        Grid.SetColumn(note, 1);
        fields.Children.Add(note);
        DockPanel.SetDock(fields, Dock.Top);
        root.Children.Add(fields);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        actions.Children.Add(_pairButton);
        _cancelButton.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(_cancelButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _output.IsReadOnly = true;
        _output.AcceptsReturn = true;
        _output.TextWrapping = TextWrapping.Wrap;
        _output.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _output.Background = Brush("#071625");
        _output.Foreground = Brush("#DCEBFA");
        _output.BorderBrush = Brush("#263D58");
        _output.Padding = new Thickness(12);
        _output.FontFamily = new FontFamily("Consolas");
        _output.FontSize = 10;
        _output.Text = "ستظهر هنا مخرجات ADB الحقيقية بعد محاولة الاقتران.";
        root.Children.Add(_output);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _status.Text = "لم يبدأ الاتصال.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
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

    private static void AddField(Grid grid, string label, Control control, int row, int column, string? hint = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush("#9FC7E8"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        });
        control.Height = 36;
        control.Padding = new Thickness(9, 6, 9, 6);
        control.Background = Brush("#0A1B2D");
        control.Foreground = Brush("#EAF3FF");
        control.BorderBrush = Brush("#35516F");
        if (control is TextBox textBox)
        {
            textBox.ToolTip = hint;
            textBox.VerticalContentAlignment = VerticalAlignment.Center;
        }
        panel.Children.Add(control);
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;

        if (!IPAddress.TryParse(_pairingHost.Text.Trim(), out _))
        {
            SetStatus("أدخل IP صحيحًا من نافذة Pair device.");
            return;
        }
        if (!IPAddress.TryParse(_connectionHost.Text.Trim(), out _))
        {
            SetStatus("أدخل IP صحيحًا من شاشة Wireless debugging الرئيسية.");
            return;
        }
        if (!int.TryParse(_pairingPort.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pairPort) ||
            pairPort is < 1 or > 65535)
        {
            SetStatus("منفذ الاقتران يجب أن يكون رقمًا بين 1 و65535.");
            return;
        }
        if (!int.TryParse(_connectionPort.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var connectPort) ||
            connectPort is < 1 or > 65535)
        {
            SetStatus("منفذ الاتصال يجب أن يكون رقمًا بين 1 و65535.");
            return;
        }

        var code = _pairingCode.Password;
        _pairingCode.Clear();
        if (!WirelessAdbPairingService.IsValidPairingCode(code))
        {
            SetStatus("رمز الاقتران غير صحيح. أدخل الأرقام الستة الظاهرة على الهاتف.");
            return;
        }

        _operation = new CancellationTokenSource();
        SetBusy(true, "جارٍ تنفيذ ADB pair ثم ADB connect...");
        _output.Clear();
        try
        {
            var result = await _service.PairAndConnectAsync(
                _pairingHost.Text.Trim(), pairPort, code,
                _connectionHost.Text.Trim(), connectPort, _operation.Token);
            _output.Text =
                "ADB PAIR\n" + result.PairOutput +
                "\n\nADB CONNECT\n" + result.ConnectOutput +
                "\n\nPAIRING TARGET\n" + result.PairingEndpoint +
                "\nCONNECT TARGET\n" + result.ConnectEndpoint;
            _status.Text = "أكد ADB نجاح الاقتران والاتصال. ارجع إلى قائمة الأجهزة وحدّث الاكتشاف للتحقق من ظهور الجهاز.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء العملية. لم يتم حفظ رمز الاقتران.";
        }
        catch (Exception ex)
        {
            _output.Text = ex.Message;
            _status.Text = "لم يكتمل الاتصال. راجع عنواني IP والمنفذين والرمز والشبكة ومخرجات ADB.";
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false, _status.Text);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _pairButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
        _pairingHost.IsEnabled = !busy;
        _pairingPort.IsEnabled = !busy;
        _connectionHost.IsEnabled = !busy;
        _connectionPort.IsEnabled = !busy;
        _pairingCode.IsEnabled = !busy;
    }

    private void SetStatus(string message)
    {
        _status.Text = message;
        _pairingCode.Clear();
    }

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(14, 9, 14, 9),
            MinWidth = 140,
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
