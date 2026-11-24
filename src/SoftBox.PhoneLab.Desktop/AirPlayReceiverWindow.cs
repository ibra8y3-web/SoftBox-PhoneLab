using System.IO;
using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Settings;
using SoftBox.PhoneLab.Desktop.Services;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// User-managed entry point for an independent GPL-3 AirPlay receiver.
/// AirPlay is viewing-only; this window does not send touch or passcode input to iOS.
/// </summary>
public sealed class AirPlayReceiverWindow : Window
{
    private const string UpstreamUrl = "https://github.com/moieric11/AirPlay-Windows";
    private readonly PhoneLabSettingsService _settingsService;
    private readonly AirPlayReceiverService _receiver;
    private readonly AirPlayReceiverBootstrapService _bootstrap;
    private PhoneLabSettings _settings;
    private readonly SoftBox.PhoneLab.Desktop.Interop.ScrcpyWindowHost _screenHost = new();
    private readonly Func<Task> _stopExistingScreenSession;
    private readonly TextBox _executablePath = new();
    private readonly TextBox _fingerprint = new();
    private readonly TextBox _log = new();
    private readonly TextBlock _status = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private bool _starting;

    public AirPlayReceiverWindow(
        PhoneLabSettings settings,
        PhoneLabSettingsService settingsService,
        AirPlayReceiverService receiver,
        AirPlayReceiverBootstrapService bootstrap,
        Func<Task> stopExistingScreenSession)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _stopExistingScreenSession = stopExistingScreenSession ?? throw new ArgumentNullException(nameof(stopExistingScreenSession));
        Title = "SoftBox | iPhone AirPlay Screen Receiver";
        Width = 900;
        Height = 720;
        MinWidth = 700;
        MinHeight = 580;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _executablePath.Text = settings.AirPlayReceiverExecutablePath ?? string.Empty;
        _executablePath.IsReadOnly = true;
        _fingerprint.IsReadOnly = true;
        _log.IsReadOnly = true;
        _log.AcceptsReturn = true;
        _log.TextWrapping = TextWrapping.Wrap;
        _log.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _log.Background = Brush("#071625");
        _log.Foreground = Brush("#DCEBFA");
        _log.MinHeight = 170;

        _status.Text = "لم يتم تشغيل مستقبل AirPlay.";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#9FC7E8");

        _start.Content = "تشغيل مستقبل AirPlay";
        _start.Padding = new Thickness(12, 8, 12, 8);
        _start.Background = Brush("#14517A");
        _start.Foreground = Brushes.White;
        _start.Click += async (_, _) => await StartReceiverAsync();

        _stop.Content = "إيقاف المستقبل";
        _stop.Padding = new Thickness(12, 8, 12, 8);
        _stop.Background = Brush("#512A32");
        _stop.Foreground = Brushes.White;
        _stop.IsEnabled = false;
        _stop.Click += async (_, _) => await StopReceiverAsync();

        _receiver.OutputReceived += OnReceiverOutput;
        if (_receiver.IsRunning)
        {
            _start.IsEnabled = false;
            _stop.IsEnabled = true;
            _status.Text = "مستقبل AirPlay يعمل بالفعل داخل نافذة iPhone المستقلة.";
            _log.Text = _receiver.RecentOutput;
        }
        Content = BuildContent();
        Closed += async (_, _) =>
        {
            _receiver.OutputReceived -= OnReceiverOutput;
            await _receiver.StopAsync();
        };
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "عرض شاشة iPhone عبر AirPlay",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        root.Children.Add(Text(
            "هذا مسار استقبال منفصل تمامًا عن ADB/scrcpy. نافذة AirPlay هذه تملك مساحة عرض أصلية مستقلة ولا تستخدم لوحة Android أو مضيف شاشة scrcpy. AirPlay هنا للعرض فقط ولا يرسل لمسًا أو إدخال رمز القفل إلى iPhone.",
            "#B7C9DE", 11));

        var license = new Border
        {
            Background = Brush("#30251B"),
            BorderBrush = Brush("#8B6238"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11),
            Margin = new Thickness(0, 12, 0, 12),
            Child = Text(
                "SoftBox يبحث تلقائيًا عن مستقبل AirPlay-Windows، وإذا لم يجده ينزّل أحدث حزمة Windows x64 من مستودع المطور الرسمي، ويتحقق من SHA-256 المنشور في GitHub قبل فكها. المكوّن مستقل ومفتوح المصدر GPL-3.0 وتُحفظ ملفاته في مجلد أدوات SoftBox؛ لا تحتاج لاختيار EXE أو تنزيله يدويًا.",
                "#FFE0B7", 10)
        };
        root.Children.Add(license);
        root.Children.Add(Text("مسار ملف airplay-windows.exe", "#7DD3FC", 10));
        var pathRow = new DockPanel { Margin = new Thickness(0, 5, 0, 8) };
        _executablePath.Padding = new Thickness(8);
        _executablePath.Background = Brush("#0F1E32");
        _executablePath.Foreground = Brush("#EAF3FF");
        _executablePath.BorderBrush = Brush("#36516D");
        pathRow.Children.Add(_executablePath);
        var browse = Button("اكتشاف / تثبيت المستقبل تلقائيًا", async (_, _) => await EnsureReceiverInstalledAsync());
        DockPanel.SetDock(browse, Dock.Left);
        pathRow.Children.Add(browse);
        root.Children.Add(pathRow);

        root.Children.Add(Text("الإصدار ومصدر التثبيت", "#7DD3FC", 10));
        _fingerprint.Padding = new Thickness(8);
        _fingerprint.Background = Brush("#0F1E32");
        _fingerprint.Foreground = Brush("#AFC7DF");
        _fingerprint.BorderBrush = Brush("#36516D");
        root.Children.Add(_fingerprint);

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        actions.Children.Add(_start);
        actions.Children.Add(_stop);
        actions.Children.Add(Button("فتح المصدر والترخيص", (_, _) => OpenUpstream()));
        root.Children.Add(actions);
        root.Children.Add(_status);

        root.Children.Add(Text("شاشة iPhone المستقلة", "#7DD3FC", 13));
        root.Children.Add(new Border
        {
            Height = 360,
            MinHeight = 260,
            Background = Brush("#050B13"),
            BorderBrush = Brush("#2B4764"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(5),
            Margin = new Thickness(0, 5, 0, 12),
            Child = _screenHost
        });

        root.Children.Add(Text("خطوات الاتصال", "#7DD3FC", 12));
        root.Children.Add(Text(
            "1) SoftBox يكتشف المستقبل أو يحمّله ويثبت ملفاته تلقائيًا عند الحاجة.\n" +
            "2) اجعل iPhone والكمبيوتر على شبكة Wi-Fi المحلية نفسها.\n" +
            "3) يبدأ مستقبل Windows تلقائيًا عند فتح SoftBox إذا كان الخيار مفعّلًا.\n" +
            "4) إذا كانت شاشة iPhone واللمس يعملان، ابدأ Screen Mirroring من الهاتف. إذا كان اللمس لا يعمل، استخدم مسار HDMI Capture إذا كان الموديل يدعمه.\n" +
            "5) نافذة AirPlay تعرض الصورة والصوت فقط؛ ولا ترسل لمسًا أو رمز قفل إلى iPhone.\n" +
            "6) لا تتوقع عرض تطبيقات البث المحمية بـFairPlay DRM.",
            "#B7C9DE", 10));

        root.Children.Add(Text("سجل المستقبل", "#7DD3FC", 11));
        root.Children.Add(_log);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async void BrowseReceiver()
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختر airplay-windows.exe من إصدار AirPlay-Windows الرسمي",
            Filter = "AirPlay-Windows receiver (airplay-windows.exe)|airplay-windows.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var fullPath = AirPlayReceiverService.ValidateReceiverPath(dialog.FileName);
            await using var stream = File.OpenRead(fullPath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            _executablePath.Text = fullPath;
            _fingerprint.Text = hash;
            var current = await _settingsService.LoadAsync();
            await _settingsService.SaveAsync(current with { AirPlayReceiverExecutablePath = fullPath });
            _status.Text = "تم حفظ مسار المستقبل وبصمة الملف. البصمة لا تثبت هوية الناشر أو سلامة المصدر.";
        }
        catch (Exception ex)
        {
            _status.Text = "رفض اختيار المستقبل: " + ex.Message;
        }
    }

    /// <summary>Starts the saved receiver after its independent WPF screen host has completed loading.</summary>
    public async Task StartReceiverFromStartupAsync()
    {
        if (!IsLoaded || !_screenHost.IsLoaded)
        {
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler? onLoaded = null;
            EventHandler? onClosed = null;
            onLoaded = (_, _) => ready.TrySetResult(true);
            onClosed = (_, _) => ready.TrySetCanceled();
            _screenHost.Loaded += onLoaded;
            Closed += onClosed;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try
                {
                    await ready.Task.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (IsVisible)
                        _status.Text = "لم تجهز نافذة العرض خلال المهلة؛ لم يبدأ مستقبل AirPlay. افتح النافذة مجددًا ثم أعد المحاولة.";
                    return;
                }
            }
            finally
            {
                _screenHost.Loaded -= onLoaded;
                Closed -= onClosed;
            }
        }

        if (!IsVisible || !IsLoaded || !_screenHost.IsLoaded)
            return;
        await StartReceiverAsync(automatic: true);
    }

    private async Task EnsureReceiverInstalledAsync()
    {
        _start.IsEnabled = false;
        _status.Text = "جارٍ البحث عن مستقبل AirPlay-Windows في أدوات SoftBox والتثبيتات المعروفة...";
        try
        {
            var installation = await _bootstrap.EnsureInstalledAsync(_executablePath.Text);
            var latest = await _settingsService.LoadAsync();
            _settings = PhoneLabSettingsService.Validate(latest with
            {
                AirPlayReceiverExecutablePath = installation.ExecutablePath
            });
            await _settingsService.SaveAsync(_settings);
            _executablePath.Text = installation.ExecutablePath;
            _fingerprint.Text = installation.Version +
                (installation.InstalledNow ? " • تم تنزيل الحزمة والتحقق من SHA-256" : " • تم العثور على تثبيت موجود");
            _status.Text = installation.InstalledNow
                ? "تم تنزيل AirPlay-Windows من المصدر الرسمي والتحقق من بصمة ZIP ثم فك ملفات المكوّن تلقائيًا."
                : "تم اكتشاف تثبيت AirPlay-Windows موجود وحفظ مساره. لا يلزم اختيار ملف يدويًا.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تجهيز مستقبل AirPlay تلقائيًا: " + ex.Message;
        }
        finally
        {
            _start.IsEnabled = !_receiver.IsRunning;
        }
    }

    private async Task EnsureReceiverInstalledAsync()
    {
        _start.IsEnabled = false;
        _status.Text = "جارٍ البحث عن مستقبل AirPlay-Windows في أدوات SoftBox والتثبيتات المعروفة...";
        try
        {
            var installation = await _bootstrap.EnsureInstalledAsync(_executablePath.Text);
            var latest = await _settingsService.LoadAsync();
            _settings = PhoneLabSettingsService.Validate(latest with
            {
                AirPlayReceiverExecutablePath = installation.ExecutablePath
            });
            await _settingsService.SaveAsync(_settings);
            _executablePath.Text = installation.ExecutablePath;
            _fingerprint.Text = installation.Version +
                (installation.InstalledNow ? " • تم تنزيل الحزمة والتحقق من SHA-256" : " • تم العثور على تثبيت موجود");
            _status.Text = installation.InstalledNow
                ? "تم تنزيل AirPlay-Windows من المصدر الرسمي والتحقق من بصمة ZIP ثم فك ملفات المكوّن تلقائيًا."
                : "تم اكتشاف تثبيت AirPlay-Windows موجود وحفظ مساره. لا يلزم اختيار ملف يدويًا.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تجهيز مستقبل AirPlay تلقائيًا: " + ex.Message;
        }
        finally
        {
            _start.IsEnabled = !_receiver.IsRunning;
        }
    }

    private async Task StartReceiverAsync(bool automatic = false)
    {
        if (_starting)
            return;

        _starting = true;
        _start.IsEnabled = false;
        _stop.IsEnabled = _receiver.IsRunning;
        _status.Text = automatic
            ? "تشغيل مستقبل AirPlay المحفوظ تلقائيًا وتجهيز نافذة العرض..."
            : "جارٍ التحقق من ملف المستقبل وتجهيز مسار العرض...";
        try
        {
            // Locate or install the official receiver before stopping any active Android screen session.
            var installation = await _bootstrap.EnsureInstalledAsync(_executablePath.Text);
            var latestSettings = await _settingsService.LoadAsync();
            _settings = PhoneLabSettingsService.Validate(latestSettings with
            {
                AirPlayReceiverExecutablePath = installation.ExecutablePath
            });
            await _settingsService.SaveAsync(_settings);
            _executablePath.Text = installation.ExecutablePath;
            _fingerprint.Text = installation.Version +
                (installation.InstalledNow ? " • تم التحقق من SHA-256" : " • تثبيت موجود");
            var receiverPath = AirPlayReceiverService.ValidateReceiverPath(installation.ExecutablePath);
            if (_receiver.IsRunning)
            {
                _status.Text = "مستقبل AirPlay يعمل بالفعل؛ لم يتم إيقافه أو فصل نافذته. استخدم زر الإيقاف أولًا إذا أردت إعادة التشغيل.";
                return;
            }

            await _stopExistingScreenSession();
            var startedPath = await _receiver.StartAsync(receiverPath, _screenHost);
            _status.Text = automatic
                ? "تم تشغيل مستقبل AirPlay تلقائيًا على الكمبيوتر. لم يبدأ بث iPhone بعد؛ يبدأ العرض عند وصول بث AirPlay من الهاتف."
                : "تم تشغيل مستقبل AirPlay داخل نافذة iPhone المستقلة. لم يتم تأكيد اتصال iPhone بعد؛ اختر AirPlay-Windows من قائمة Screen Mirroring على الهاتف.";
            AppendLog("Started: " + startedPath);
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تشغيل مستقبل AirPlay: " + ex.Message;
        }
        finally
        {
            _starting = false;
            var running = _receiver.IsRunning;
            _start.IsEnabled = !running;
            _stop.IsEnabled = running;
        }
    }

    private async Task StopReceiverAsync()
    {
        await _receiver.StopAsync();
        _start.IsEnabled = true;
        _stop.IsEnabled = false;
        _status.Text = "تم إيقاف مستقبل AirPlay.";
    }

    private void OnReceiverOutput(string line)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            AppendLog(line);
            if (_receiver.IsRunning)
            {
                _start.IsEnabled = false;
                _stop.IsEnabled = true;
            }
            else if (line.Contains("exited unexpectedly", StringComparison.OrdinalIgnoreCase))
            {
                _start.IsEnabled = true;
                _stop.IsEnabled = false;
                _status.Text = "توقفت عملية AirPlay بشكل غير متوقع. راجع سجل الأخطاء ثم أعد التشغيل بعد معالجة السبب.";
            }
            else if (line.Contains("stopped", StringComparison.OrdinalIgnoreCase))
            {
                _start.IsEnabled = true;
                _stop.IsEnabled = false;
                _status.Text = "تم إيقاف مستقبل AirPlay.";
            }
        });
    }

    private void AppendLog(string line)
    {
        var lines = (_log.Text + Environment.NewLine + line)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        _log.Text = string.Join(Environment.NewLine, lines.TakeLast(120));
        _log.ScrollToEnd();
    }

    private void OpenUpstream()
    {
        try { Process.Start(new ProcessStartInfo(UpstreamUrl) { UseShellExecute = true }); }
        catch (Exception ex) { _status.Text = "تعذر فتح صفحة المشروع: " + ex.Message; }
    }

    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 0), Background = Brush("#20364F"), Foreground = Brushes.White, BorderBrush = Brush("#3E7096") };
        button.Click += handler;
        return button;
    }

    private static TextBlock Text(string value, string color, double size)
        => new() { Text = value, Foreground = Brush(color), FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 7) };

    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value)!);
}
