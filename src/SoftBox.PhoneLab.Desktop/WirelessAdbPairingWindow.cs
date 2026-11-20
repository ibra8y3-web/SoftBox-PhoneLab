using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Interactive Android Wireless debugging pairing using the phone's temporary pairing code.
/// The code is never saved in SoftBox settings or output files.
/// </summary>
public sealed class WirelessAdbPairingWindow : Window
{
    private readonly IAdbCommandRunner _adb;
    private readonly WirelessAdbPairingService _service;
    private readonly TextBox _host = InputBox();
    private readonly TextBox _pairingPort = InputBox();
    private readonly TextBox _connectionPort = InputBox();
    private readonly PasswordBox _pairingCode = new()
    {
        Height = 36,
        Padding = new Thickness(10, 7, 10, 7),
        Background = Brush("#071625"),
        Foreground = Brush("#EAF3FF"),
        BorderBrush = Brush("#35516F"),
        FontSize = 13,
        MaxLength = 6
    };
    private readonly TextBox _output = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = Brush("#071625"),
        Foreground = Brush("#DCEBFA"),
        BorderBrush = Brush("#263D58"),
        Padding = new Thickness(12),
        FontFamily = new FontFamily("Consolas"),
        FontSize = 10,
        Text = "لم يتم تنفيذ أي عملية بعد."
    };
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("#9FC7E8")
    };
    private readonly ProgressBar _progress = new()
    {
        Height = 4,
        IsIndeterminate = true,
        Visibility = Visibility.Collapsed,
        Foreground = Brush("#38BDF8")
    };
    private readonly Button _pairButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _operation;

    public WirelessAdbPairingWindow(IAdbCommandRunner adb)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _service = new WirelessAdbPairingService(_adb);

        Title = "SoftBox | اتصال ADB لاسلكي";
        Width = 900;
        Height = 760;
        MinWidth = 720;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _pairButton = ButtonControl("اقتران واتصال", "#14517A", Pair_Click);
        _cancelButton = ButtonControl("إلغاء العملية", "#6B3D22", Cancel_Click);
        _cancelButton.IsEnabled = false;

        Content = BuildContent();
        _status.Text = _adb.IsAvailable
            ? "جاهز. أدخل بيانات شاشة Pair device with pairing code من الهاتف."
            : "أداة ADB غير متاحة. ضع adb.exe داخل Tools/platform-tools أو أضفه إلى PATH.";
        _pairButton.IsEnabled = _adb.IsAvailable;
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "اتصال ADB لاسلكي",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "ANDROID WIRELESS DEBUGGING • PAIRING CODE • ADB PAIR / CONNECT",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "يتطلب Android 11 أو أحدث للهواتف، وتوصيل الهاتف والكمبيوتر على شبكة Wi-Fi نفسها مع تفعيل Wireless debugging من خيارات المطور.",
            FontSize = 11,
            Foreground = Brush("#B7C9DE"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Margin = new Thickness(0, 0, 0, 6);
        footer.Children.Add(_status);
        footer.Children.Add(_progress);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var form = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        form.ColumnDefinitions.Add(new ColumnDefinition());
        form.ColumnDefinitions.Add(new ColumnDefinition());
        for (var index = 0; index < 4; index++)
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddField(form, "عنوان IP للهاتف", _host, 0, 0, 2);
        AddField(form, "منفذ الاقتران (من نافذة Pair using pairing code)", _pairingPort, 1, 0);
        AddField(form, "منفذ Wireless debugging للاتصال", _connectionPort, 1, 1);
        AddField(form, "رمز الاقتران المؤقت (6 أرقام)", _pairingCode, 2, 0, 2);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 10)
        };
        actions.Children.Add(_pairButton);
        _cancelButton.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(_cancelButton);
        Grid.SetRow(actions, 3);
        Grid.SetColumnSpan(actions, 2);
        form.Children.Add(actions);

        DockPanel.SetDock(form, Dock.Top);
        root.Children.Add(form);

        var resultPanel = new DockPanel();
        var resultHeader = new TextBlock
        {
            Text = "مخرجات ADB الفعلية",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#7DD3FC"),
            Margin = new Thickness(0, 0, 0, 7)
        };
        DockPanel.SetDock(resultHeader, Dock.Top);
        resultPanel.Children.Add(resultHeader);
        resultPanel.Children.Add(_output);
        root.Children.Add(resultPanel);

        return root;
    }

    private static void AddField(Grid grid, string label, Control input, int row, int column, int columnSpan = 1)
    {
        input.Margin = new Thickness(0, 6, 8, 12);
        if (input is TextBox textBox)
            textBox.ToolTip = label;
        var field = new StackPanel();
        field.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush("#9FC7E8"),
            FontSize = 11
        });
        field.Children.Add(input);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, column);
        Grid.SetColumnSpan(field, columnSpan);
        grid.Children.Add(field);
    }

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;

        if (!_adb.IsAvailable)
        {
            _status.Text = "ADB غير موجود. لم يتم تنفيذ أي أمر.";
            return;
        }

        if (!TryReadPort(_pairingPort.Text, out var pairingPort))
        {
            _status.Text = "أدخل منفذ الاقتران الصحيح الذي يعرضه الهاتف (1–65535).";
            return;
        }

        if (!TryReadPort(_connectionPort.Text, out var connectionPort))
        {
            _status.Text = "أدخل منفذ Wireless debugging الصحيح للاتصال (1–65535).";
            return;
        }

        var code = _pairingCode.Password;
        if (!WirelessAdbPairingService.IsValidPairingCode(code))
        {
            _status.Text = "رمز الاقتران يجب أن يتكون من 6 أرقام إنجليزية كما يظهر على الهاتف.";
            return;
        }

        var operation = new CancellationTokenSource();
        _operation = operation;
        SetBusy(true, "جارٍ تنفيذ adb pair ثم adb connect باستخدام المنافذ التي أدخلتها...");
        _output.Clear();

        try
        {
            var result = await _service.PairAndConnectAsync(
                _host.Text,
                pairingPort,
                code,
                connectionPort,
                operation.Token);

            _output.Text =
                "PAIR ENDPOINT: " + result.PairingEndpoint + Environment.NewLine +
                "PAIR RESULT:" + Environment.NewLine + result.PairOutput + Environment.NewLine + Environment.NewLine +
                "CONNECT ENDPOINT: " + result.ConnectEndpoint + Environment.NewLine +
                "CONNECT RESULT:" + Environment.NewLine + result.ConnectOutput + Environment.NewLine;

            SetBusy(false,
                "أكد ADB نجاح الاقتران والاتصال. سيظهر الجهاز في قائمة الأجهزة عند تحديث اكتشاف ADB.");
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "تم إلغاء العملية. لم تُرسل أوامر إضافية بعد الإلغاء.");
            _output.Text = "تم إلغاء عملية الاقتران أو الاتصال.";
        }
        catch (Exception ex)
        {
            SetBusy(false, "لم تكتمل عملية ADB: " + ex.Message);
            _output.Text = ex.ToString();
        }
        finally
        {
            _pairingCode.Clear();
            operation.Dispose();
            if (ReferenceEquals(_operation, operation))
                _operation = null;
            _cancelButton.IsEnabled = false;
            _pairButton.IsEnabled = _adb.IsAvailable;
            _host.IsEnabled = true;
            _pairingPort.IsEnabled = true;
            _connectionPort.IsEnabled = true;
            _pairingCode.IsEnabled = true;
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _pairButton.IsEnabled = !busy && _adb.IsAvailable;
        _cancelButton.IsEnabled = busy;
        _host.IsEnabled = !busy;
        _pairingPort.IsEnabled = !busy;
        _connectionPort.IsEnabled = !busy;
        _pairingCode.IsEnabled = !busy;
    }

    private static bool TryReadPort(string value, out int port)
        => int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
           port is >= 1 and <= 65535;

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => _operation?.Cancel();

    private static TextBox InputBox()
        => new()
        {
            Height = 36,
            Padding = new Thickness(10, 7, 10, 7),
            Background = Brush("#071625"),
            Foreground = Brush("#EAF3FF"),
            BorderBrush = Brush("#35516F"),
            FontSize = 13
        };

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(14, 8, 14, 8),
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
