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
    private readonly SoftBox.PhoneLab.Desktop.Interop.ScrcpyWindowHost _screenHost;
    private readonly TextBox _executablePath = new();
    private readonly TextBox _fingerprint = new();
    private readonly TextBox _log = new();
    private readonly TextBlock _status = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly AirPlayReceiverService _receiver = new();

    public AirPlayReceiverWindow(
        PhoneLabSettings settings,
        PhoneLabSettingsService settingsService,
        SoftBox.PhoneLab.Desktop.Interop.ScrcpyWindowHost screenHost)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _screenHost = screenHost ?? throw new ArgumentNullException(nameof(screenHost));
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
        Content = BuildContent();
        Closed += async (_, _) => await _receiver.DisposeAsync();
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
            "هذا مسار استقبال منفصل عن ADB/scrcpy. بعد تشغيل المستقبل تُدمج نافذته الأصلية في مساحة شاشة الهاتف الجانبية داخل SoftBox. AirPlay هنا للعرض فقط ولا يرسل لمسًا أو إدخال رمز القفل إلى iPhone.",
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
                "ملاحظة ترخيص: SoftBox لا يضمّن أو ينسخ مستقبل AirPlay. استخدم الإصدار المستقل من المشروع المفتوح AirPlay-Windows (GPL-3.0) بعد تنزيله بنفسك من مصدره، واقرأ الترخيص وإشعارات الأطراف الثالثة. لا تنزّل ملفات EXE من مواقع وسيطة.",
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
        var browse = Button("اختيار ملف المستقبل", (_, _) => BrowseReceiver());
        DockPanel.SetDock(browse, Dock.Left);
        pathRow.Children.Add(browse);
        root.Children.Add(pathRow);

        root.Children.Add(Text("بصمة SHA-256 للملف المختار (للتعرّف على الملف فقط؛ ليست إثبات ناشر)", "#7DD3FC", 10));
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

        root.Children.Add(Text("خطوات الاتصال", "#7DD3FC", 12));
        root.Children.Add(Text(
            "1) ثبّت مستقبل AirPlay-Windows المستقل وفك ملفات الإصدار كاملة.\n" +
            "2) اجعل iPhone والكمبيوتر على شبكة Wi-Fi المحلية نفسها.\n" +
            "3) شغّل المستقبل من هنا، ثم افتح Control Center على iPhone واختر Screen Mirroring ثم AirPlay-Windows.\n" +
            "4) نافذة المستقبل تعرض الصورة والصوت فقط؛ التحكم يظل من شاشة iPhone نفسها.\n" +
            "5) تظهر صورة iPhone في المساحة الجانبية الثابتة داخل SoftBox؛ تبقى الأوامر واللمس على الهاتف نفسه.\n" +
            "6) لا تتوقع عرض تطبيقات البث التي تستخدم FairPlay DRM؛ دعمها خارج نطاق مستقبل الشاشة المذكور.",
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

    private async Task StartReceiverAsync()
    {
        try
        {
            var startedPath = await _receiver.StartAsync(_executablePath.Text, _screenHost);
            _start.IsEnabled = false;
            _stop.IsEnabled = true;
            _status.Text = "تم تشغيل مستقبل AirPlay ودمج نافذته في شاشة SoftBox الجانبية. لم يتم تأكيد اتصال iPhone بعد؛ اختر AirPlay-Windows من قائمة Screen Mirroring على الهاتف.";
            AppendLog("Started: " + startedPath);
        }
        catch (Exception ex)
        {
            _start.IsEnabled = true;
            _stop.IsEnabled = false;
            _status.Text = "تعذر تشغيل مستقبل AirPlay: " + ex.Message;
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
        _ = Dispatcher.InvokeAsync(() => AppendLog(line));
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
