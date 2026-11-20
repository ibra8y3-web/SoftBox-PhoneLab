using Microsoft.Win32;
using SoftBox.PhoneLab.Desktop.Interop;
using SoftBox.PhoneLab.Desktop.Services;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Records the selected authorized Android screen using the verified scrcpy binary.
/// Closing the recording window posts WM_CLOSE to scrcpy so the container can be finalized.
/// </summary>
public sealed class ScreenRecordingWindow : Window
{
    private readonly ScreenMirrorService _mirror;
    private readonly string _serial;
    private readonly string _model;
    private readonly ScrcpyWindowHost _host = new();
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly TextBlock _status = new();
    private string? _recordingPath;
    private bool _allowClose;

    public ScreenRecordingWindow(ScreenMirrorService mirror, string serial, string model)
    {
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        _serial = serial;
        _model = model ?? "Android";

        Title = "SoftBox | تسجيل شاشة الهاتف";
        Width = 1040;
        Height = 760;
        MinWidth = 780;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _startButton = ButtonControl("بدء التسجيل", "#14517A", Start_Click);
        _stopButton = ButtonControl("إيقاف التسجيل وحفظ الملف", "#176044", Stop_Click);
        _stopButton.IsEnabled = false;

        Content = BuildContent();
        Closing += OnClosing;
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "تسجيل شاشة الهاتف",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = $"الجهاز: {_model} | ADB: {Mask(_serial)} | SCRCPY SCREEN CAPTURE",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "يتم تسجيل بث الشاشة الفعلي من scrcpy إلى MP4 أو MKV. لا يتم تصوير سطح مكتب Windows ولا استخدام بيانات تجريبية. إيقاف التسجيل يغلق جلسة عرض scrcpy الحالية؛ يمكنك فتح العرض مرة أخرى من Phone Link Studio.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(_startButton);
        actions.Children.Add(_stopButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _host.Background = Brush("#050B12");
        root.Children.Add(_host);

        _status.Text = "اضغط بدء التسجيل واختر مكان حفظ الفيديو.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_mirror.IsRecording)
        {
            MessageBox.Show(this,
                "يوجد تسجيل آخر نشط بالفعل. أوقفه واحفظه قبل بدء تسجيل جديد.",
                "تسجيل الشاشة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "حفظ تسجيل شاشة Android",
            Filter = "MP4 Video (*.mp4)|*.mp4|Matroska Video (*.mkv)|*.mkv",
            FilterIndex = 1,
            FileName = $"SoftBox-Screen-{DateTime.Now:yyyyMMdd-HHmmss}.mp4",
            AddExtension = true,
            DefaultExt = ".mp4",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        if (File.Exists(dialog.FileName))
        {
            MessageBox.Show(this, "الملف موجود. اختر اسمًا جديدًا حفاظًا على التسجيلات القديمة.",
                "تسجيل الشاشة", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmation = MessageBox.Show(this,
            $"سيبدأ scrcpy تسجيل الشاشة الحقيقية للجهاز {_model} إلى:\n{dialog.FileName}\n\nسيتم استبدال جلسة العرض الحالية إن كانت مفتوحة. هل تتابع؟",
            "تأكيد بدء التسجيل", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (confirmation != MessageBoxResult.Yes)
            return;

        SetBusy(true, "جارٍ تجهيز scrcpy وبدء تسجيل شاشة الهاتف...");
        try
        {
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            if (_host.NativeHostHandle == IntPtr.Zero)
                throw new InvalidOperationException("لم يتم إنشاء مساحة عرض الهاتف.");

            await _mirror.StartEmbeddedRecordingAsync(_serial, _model, _host, dialog.FileName);
            _recordingPath = Path.GetFullPath(dialog.FileName);
            _status.Text = "التسجيل نشط الآن. استخدم زر إيقاف التسجيل وحفظ الملف لإنهاء الفيديو بطريقة سليمة.";
            _startButton.IsEnabled = false;
            _stopButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر بدء تسجيل الشاشة: " + ex.Message;
            MessageBox.Show(this, ex.Message, "تسجيل الشاشة",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!_mirror.IsRecording)
                SetBusy(false, _status.Text);
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (!_mirror.IsRecording || _mirror.ActiveSerial != _serial)
        {
            _status.Text = "لا يوجد تسجيل نشط لهذا الجهاز.";
            return;
        }

        _stopButton.IsEnabled = false;
        _startButton.IsEnabled = false;
        SetBusy(true, "جارٍ إنهاء جلسة scrcpy وإغلاق ملف الفيديو...");
        try
        {
            var result = await _mirror.StopRecordingAsync();
            _recordingPath = null;
            _status.Text = $"تم حفظ التسجيل والتحقق من الملف: {result.FullName}\nالحجم: {result.Length:N0} بايت. تم إيقاف عرض scrcpy ويمكن إعادة فتحه من Phone Link Studio.";
            _startButton.IsEnabled = true;
            _stopButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تأكيد اكتمال ملف التسجيل: " + ex.Message;
            MessageBox.Show(this, ex.Message, "إيقاف التسجيل",
                MessageBoxButton.OK, MessageBoxImage.Error);
            _startButton.IsEnabled = !_mirror.IsRecording;
            _stopButton.IsEnabled = _mirror.IsRecording;
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        if (_mirror.IsRecording && _mirror.ActiveSerial == _serial)
        {
            e.Cancel = true;
            var answer = MessageBox.Show(this,
                "التسجيل ما زال يعمل. هل تريد إيقافه وحفظ ملف الفيديو قبل إغلاق النافذة؟",
                "تسجيل نشط", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;

            _stopButton.IsEnabled = false;
            _startButton.IsEnabled = false;
            try
            {
                var result = await _mirror.StopRecordingAsync();
                _recordingPath = null;
                _status.Text = $"تم حفظ التسجيل: {result.FullName} ({result.Length:N0} bytes).";
                _allowClose = true;
                Close();
            }
            catch (Exception ex)
            {
                _status.Text = "تعذر إيقاف التسجيل بأمان: " + ex.Message;
                MessageBox.Show(this, ex.Message, "تسجيل الشاشة",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _stopButton.IsEnabled = true;
            }
            return;
        }

        if (_mirror.ActiveSerial == _serial)
            _mirror.Stop();
    }

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        if (busy)
        {
            _startButton.IsEnabled = false;
            _stopButton.IsEnabled = false;
        }
    }

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
