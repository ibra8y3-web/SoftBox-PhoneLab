using System.IO;
using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Settings;
using SoftBox.PhoneLab.Desktop.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Independent, real HDMI capture-card viewer for iPhones that support wired video output.
/// </summary>
public sealed class IPhoneHdmiCaptureWindow : Window
{
    private readonly PhoneLabSettingsService _settingsService;
    private readonly HdmiCaptureService _capture = new();
    private PhoneLabSettings _settings;
    private readonly TextBox _ffmpegPath = new();
    private readonly ComboBox _devices = new();
    private readonly Button _scan = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly Image _preview = new();
    private readonly TextBlock _noSignal = new();
    private readonly TextBlock _status = new();
    private readonly TextBox _log = new();
    private bool _receivedFrame;
    private int _frameDispatchPending;

    public IPhoneHdmiCaptureWindow(PhoneLabSettings settings, PhoneLabSettingsService settingsService)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        Title = "SoftBox | iPhone HDMI Screen Recovery";
        Width = 1050;
        Height = 820;
        MinWidth = 820;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _ffmpegPath.Text = settings.FfmpegExecutablePath;
        _ffmpegPath.IsReadOnly = true;
        _devices.MinWidth = 330;
        _devices.Background = Brush("#0F1E32");
        _devices.Foreground = Brush("#EAF3FF");
        _devices.BorderBrush = Brush("#36516D");

        _preview.Stretch = Stretch.Uniform;
        _noSignal.Text = "لا توجد صورة بعد\nوصّل محول AV المناسب من iPhone إلى HDMI، ثم كارت التقاط UVC إلى الكمبيوتر، وبعدها افحص أجهزة الفيديو.";
        _noSignal.TextAlignment = TextAlignment.Center;
        _noSignal.TextWrapping = TextWrapping.Wrap;
        _noSignal.FontSize = 16;
        _noSignal.Foreground = Brush("#9AB4CE");
        _noSignal.Margin = new Thickness(26);

        _status.Text = "هذا المسار لا يحتاج بدء AirPlay على الهاتف، لكنه يتطلب إخراج فيديو سلكيًا مدعومًا من موديل iPhone.";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#F3D4A6");

        _log.IsReadOnly = true;
        _log.AcceptsReturn = true;
        _log.TextWrapping = TextWrapping.Wrap;
        _log.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _log.Background = Brush("#06111D");
        _log.Foreground = Brush("#DCEBFA");
        _log.MinHeight = 105;

        _scan.Content = "فحص كروت الالتقاط المتصلة";
        StyleButton(_scan, "#203B58");
        _scan.Click += async (_, _) => await ScanDevicesAsync();

        _start.Content = "بدء العرض السلكي";
        StyleButton(_start, "#176044");
        _start.IsEnabled = false;
        _start.Click += async (_, _) => await StartCaptureAsync();

        _stop.Content = "إيقاف العرض";
        StyleButton(_stop, "#512A32");
        _stop.IsEnabled = false;
        _stop.Click += async (_, _) => await StopCaptureAsync();

        _capture.FrameReceived += OnFrameReceived;
        _capture.OutputReceived += OnCaptureOutput;
        Content = BuildContent();
        Loaded += async (_, _) =>
        {
            if (File.Exists(_ffmpegPath.Text))
                await ScanDevicesAsync();
        };
        Closed += async (_, _) => await _capture.DisposeAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(Text("استعادة عرض شاشة iPhone عبر HDMI Capture", "#7DD3FC", 24, FontWeights.Bold));
        header.Children.Add(Text(
            "مسار سلكي مستقل عن AirPlay وADB/scrcpy. تُعرض إطارات حقيقية واردة من كارت التقاط HDMI ظاهر في Windows كجهاز فيديو DirectShow/UVC. لا تُنشأ صورة تجريبية عند غياب الإشارة.",
            "#B7C9DE", 11));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var setup = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        setup.Children.Add(Text("مسار ffmpeg.exe (إصدار يدعم DirectShow)", "#7DD3FC", 11, FontWeights.SemiBold));
        var pathRow = new DockPanel { Margin = new Thickness(0, 5, 0, 8) };
        _ffmpegPath.Padding = new Thickness(8);
        _ffmpegPath.Background = Brush("#0F1E32");
        _ffmpegPath.Foreground = Brush("#EAF3FF");
        _ffmpegPath.BorderBrush = Brush("#36516D");
        pathRow.Children.Add(_ffmpegPath);
        var browse = Button("اختيار FFmpeg", "#203B58", (_, _) => BrowseFfmpeg());
        DockPanel.SetDock(browse, Dock.Left);
        pathRow.Children.Add(browse);
        setup.Children.Add(pathRow);

        setup.Children.Add(Text("جهاز التقاط HDMI / UVC الذي رصده Windows", "#7DD3FC", 11));
        var deviceRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        deviceRow.Children.Add(_devices);
        deviceRow.Children.Add(_scan);
        deviceRow.Children.Add(_start);
        deviceRow.Children.Add(_stop);
        setup.Children.Add(deviceRow);
        DockPanel.SetDock(setup, Dock.Top);
        root.Children.Add(setup);

        var compatibility = new Border
        {
            Background = Brush("#211D2A"),
            BorderBrush = Brush("#66516B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = Text(
                "التوصيل: iPhone ← محول AV متوافق مع منفذه (30-pin أو Lightning أو USB-C) ← HDMI ← كارت التقاط UVC ← USB الكمبيوتر. دعم USB-C للفيديو يختلف حسب الموديل؛ لا تفترض أن كل منفذ USB-C يخرج DisplayPort. أجهزة لا تدعم الفيديو السلكي تستخدم AirPlay عند إمكان بدء البث من الهاتف. كارت الالتقاط لا يفك القفل ولا يرسل لمسًا إلى iPhone.",
                "#FFE0B7", 10)
        };
        DockPanel.SetDock(compatibility, Dock.Top);
        root.Children.Add(compatibility);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(120) });
        var view = new Border
        {
            Background = Brush("#030810"),
            BorderBrush = Brush("#294761"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Child = new Grid()
        };
        var canvas = (Grid)view.Child;
        canvas.Children.Add(_preview);
        canvas.Children.Add(_noSignal);
        Grid.SetRow(view, 0);
        body.Children.Add(view);
        _log.Margin = new Thickness(0, 9, 0, 0);
        Grid.SetRow(_log, 1);
        body.Children.Add(_log);

        DockPanel.SetDock(body, Dock.Top);
        root.Children.Add(body);
        _status.Margin = new Thickness(0, 9, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private async void BrowseFfmpeg()
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختر ffmpeg.exe من إصدار يدعم DirectShow",
            Filter = "FFmpeg executable (ffmpeg.exe)|ffmpeg.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var selected = HdmiCaptureService.ValidateFfmpegPath(dialog.FileName);
            _settings = _settings with { FfmpegExecutablePath = selected };
            await _settingsService.SaveAsync(_settings);
            _ffmpegPath.Text = selected;
            _status.Text = "تم حفظ مسار FFmpeg. لا يتم تنزيل البرنامج أو تشغيله قبل طلب فحص أجهزة الالتقاط.";
            await ScanDevicesAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر قبول FFmpeg: " + ex.Message;
        }
    }

    private async Task ScanDevicesAsync()
    {
        _scan.IsEnabled = false;
        _start.IsEnabled = false;
        _status.Text = "جارٍ فحص أجهزة الفيديو الفعلية عبر DirectShow...";
        try
        {
            var devices = await _capture.EnumerateVideoDevicesAsync(_ffmpegPath.Text);
            _devices.ItemsSource = devices;
            if (devices.Count > 0) _devices.SelectedIndex = 0;
            _start.IsEnabled = devices.Count > 0 && !_capture.IsRunning;
            _status.Text = $"تم رصد {devices.Count} جهاز فيديو من Windows. اختر كارت التقاط HDMI الصحيح ثم ابدأ العرض.";
            AppendLog("Enumerated DirectShow video devices: " + string.Join(" | ", devices));
        }
        catch (Exception ex)
        {
            _devices.ItemsSource = null;
            _status.Text = "فشل اكتشاف كارت الالتقاط: " + ex.Message;
            AppendLog("Device discovery failed: " + ex.Message);
        }
        finally { _scan.IsEnabled = true; }
    }

    private async Task StartCaptureAsync()
    {
        if (_devices.SelectedItem is not string deviceName)
        {
            _status.Text = "اختر جهاز التقاط فيديو أولًا.";
            return;
        }
        _start.IsEnabled = false;
        _scan.IsEnabled = false;
        _receivedFrame = false;
        _noSignal.Visibility = Visibility.Visible;
        _preview.Source = null;
        _status.Text = "جارٍ تشغيل الالتقاط الحقيقي. انتظر وصول أول إطار من HDMI...";
        try
        {
            await _capture.StartAsync(_ffmpegPath.Text, deviceName);
            _stop.IsEnabled = true;
            _status.Text = "عملية الالتقاط تعمل؛ لم يصل إطار بعد. تحقق من محول iPhone وكابل HDMI ومصدر الإدخال في كارت الالتقاط.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر بدء HDMI Capture: " + ex.Message;
            _start.IsEnabled = true;
            _scan.IsEnabled = true;
        }
    }

    private async Task StopCaptureAsync()
    {
        await _capture.StopAsync();
        _start.IsEnabled = _devices.Items.Count > 0;
        _scan.IsEnabled = true;
        _stop.IsEnabled = false;
        _status.Text = "تم إيقاف التقاط HDMI وتحرير عملية FFmpeg.";
    }

    private void OnFrameReceived(byte[] jpeg)
    {
        // Keep at most one UI render queued. Slow computers drop stale frames instead of
        // accumulating an unbounded dispatcher queue and showing a delayed image.
        if (Interlocked.CompareExchange(ref _frameDispatchPending, 1, 0) != 0)
            return;

        _ = Dispatcher.InvokeAsync(() =>
        {
            try
            {
                using var stream = new MemoryStream(jpeg, writable: false);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                _preview.Source = bitmap;
                _noSignal.Visibility = Visibility.Collapsed;
                if (!_receivedFrame)
                {
                    _receivedFrame = true;
                    _status.Text = "تم استقبال وعرض إطار فيديو حقيقي من كارت HDMI Capture.";
                    AppendLog("First real HDMI capture frame received.");
                }
            }
            catch (Exception ex)
            {
                AppendLog("Could not render captured JPEG frame: " + ex.Message);
                _status.Text = "وصل إطار من كارت الالتقاط لكن تعذر فك الصورة: " + ex.Message;
            }
            finally
            {
                Volatile.Write(ref _frameDispatchPending, 0);
            }
        });
    }

    private void OnCaptureOutput(string line)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            AppendLog(line);
            if (line.Contains("exited unexpectedly", StringComparison.OrdinalIgnoreCase))
            {
                _stop.IsEnabled = false;
                _start.IsEnabled = true;
                _scan.IsEnabled = true;
                _status.Text = "توقفت عملية التقاط HDMI بشكل مفاجئ. راجع سجل FFmpeg.";
            }
        });
    }

    private void AppendLog(string line)
    {
        var lines = (_log.Text + Environment.NewLine + line)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        _log.Text = string.Join(Environment.NewLine, lines.TakeLast(100));
        _log.ScrollToEnd();
    }

    private static Button Button(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 7, 0),
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
        button.Margin = new Thickness(0, 0, 7, 0);
        button.Background = Brush(color);
        button.Foreground = Brushes.White;
        button.BorderBrush = Brush("#3E7096");
    }

    private static TextBlock Text(string text, string color, double size, FontWeight? weight = null)
        => new()
        {
            Text = text,
            Foreground = Brush(color),
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 3)
        };

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color)!);
}
