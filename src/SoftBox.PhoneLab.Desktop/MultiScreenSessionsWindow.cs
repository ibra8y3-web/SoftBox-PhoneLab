using SoftBox.PhoneLab.Desktop.Interop;
using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Manages up to four independent scrcpy processes, each embedded in its own WPF host.
/// A separate ScreenMirrorService instance owns each device process and window handle.
/// </summary>
public sealed class MultiScreenSessionsWindow : Window
{
    private readonly AdbClient _adb;
    private readonly PhoneDiscoveryService _discovery;
    private readonly StackPanel _deviceChoices = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _sessionCards = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _status = new();
    private readonly Button _refreshButton;
    private readonly Button _startButton;
    private readonly Button _stopAllButton;
    private readonly Dictionary<string, ActiveSession> _sessions = new(StringComparer.Ordinal);
    private CancellationTokenSource? _operation;
    private bool _closing;

    public MultiScreenSessionsWindow(AdbClient adb)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _discovery = new PhoneDiscoveryService(_adb);

        Title = "SoftBox | جلسات عرض متعددة للأجهزة";
        Width = 1400;
        Height = 920;
        MinWidth = 1040;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _refreshButton = ButtonControl("تحديث الأجهزة", "#243B56", async (_, _) => await RefreshDevicesAsync());
        _startButton = ButtonControl("فتح الجلسات المحددة", "#14517A", async (_, _) => await StartSelectedAsync());
        _stopAllButton = ButtonControl("إيقاف كل الجلسات", "#713B43", (_, _) => StopAllSessions());
        _stopAllButton.IsEnabled = false;

        Content = BuildContent();
        Loaded += async (_, _) => await RefreshDevicesAsync();
        Closing += (_, _) =>
        {
            _closing = true;
            _operation?.Cancel();
        };
        Closed += (_, _) => StopAllSessions();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "جلسات عرض متعددة",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "MULTI-DEVICE SCREEN SESSIONS • INDEPENDENT SCRCPY PROCESSES",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "اختر حتى أربعة هواتف مصرحًا بها عبر ADB. لكل هاتف عملية scrcpy مستقلة ونافذة مدمجة يمكن إيقافها دون التأثير على بقية الجلسات. لا يتم تسجيل الشاشة تلقائيًا ولا نقل ملفات الجهاز.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var controls = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var buttons = new WrapPanel();
        buttons.Children.Add(_refreshButton);
        buttons.Children.Add(_startButton);
        buttons.Children.Add(_stopAllButton);
        controls.Children.Add(buttons);

        var deviceHeader = new TextBlock
        {
            Text = "الأجهزة المكتشفة — اختر الأجهزة التي تريد عرضها",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#B9DDF7"),
            Margin = new Thickness(4, 8, 4, 4)
        };
        controls.Children.Add(deviceHeader);
        var deviceScroll = new ScrollViewer
        {
            Content = _deviceChoices,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Height = 52,
            Background = Brush("#0D1C2E")
        };
        controls.Children.Add(deviceScroll);

        _status.Text = "جارٍ اكتشاف أجهزة ADB...";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(4, 8, 4, 4);
        controls.Children.Add(_status);
        DockPanel.SetDock(controls, Dock.Top);
        root.Children.Add(controls);

        var sessionsScroll = new ScrollViewer
        {
            Content = _sessionCards,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        root.Children.Add(sessionsScroll);
        return root;
    }

    private async Task RefreshDevicesAsync()
    {
        if (!_adb.IsAvailable)
        {
            _status.Text = "أداة ADB غير موجودة. ثبّت Platform-Tools أو استخدم حزمة SoftBox المتكاملة.";
            return;
        }

        _refreshButton.IsEnabled = false;
        try
        {
            var devices = await _discovery.DiscoverAsync();
            var currentAuthorized = devices
                .Where(device => device.IsAuthorized)
                .Select(device => device.Serial)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var stale in _sessions.Keys.Where(serial => !currentAuthorized.Contains(serial)).ToArray())
                StopSession(stale);

            _deviceChoices.Children.Clear();
            foreach (var device in devices)
            {
                var active = _sessions.ContainsKey(device.Serial);
                var check = new CheckBox
                {
                    Tag = device.Serial,
                    Content = $"{device.MaskedSerial} • {device.StateText}",
                    IsEnabled = device.IsAuthorized && !active,
                    IsChecked = false,
                    Margin = new Thickness(10, 7, 10, 7),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Foreground = device.IsAuthorized ? Brush("#CDEBFA") : Brush("#F0C9A5"),
                    ToolTip = device.IsAuthorized
                        ? "الجهاز مصرح به عبر ADB ويمكن اختياره"
                        : "الجهاز يحتاج إلى موافقة USB debugging على الهاتف"
                };
                _deviceChoices.Children.Add(check);
            }

            if (devices.Count == 0)
            {
                _deviceChoices.Children.Add(new TextBlock
                {
                    Text = "لا توجد أجهزة ADB حاليًا. وصّل الهاتف وفعّل USB debugging ثم حدّث القائمة.",
                    Foreground = Brush("#94A3B8"),
                    Margin = new Thickness(10)
                });
            }

            UpdateSessionStatus();
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر اكتشاف الأجهزة: " + ex.Message;
        }
        finally
        {
            if (!_closing)
                _refreshButton.IsEnabled = true;
        }
    }

    private async Task StartSelectedAsync()
    {
        if (_operation is not null || _closing)
            return;

        var requested = _deviceChoices.Children
            .OfType<CheckBox>()
            .Where(check => check.IsChecked == true && check.Tag is string)
            .Select(check => (string)check.Tag)
            .ToArray();

        if (requested.Length == 0)
        {
            _status.Text = "حدد جهازًا واحدًا مصرحًا به على الأقل.";
            return;
        }

        var availableSlots = MultiScreenSessionsPolicy.MaximumConcurrentSessions - _sessions.Count;
        if (availableSlots <= 0)
        {
            _status.Text = "وصلت إلى الحد الأقصى وهو أربع جلسات عرض مستقلة.";
            return;
        }

        _operation = new CancellationTokenSource();
        _refreshButton.IsEnabled = false;
        _startButton.IsEnabled = false;
        var failures = new List<string>();
        try
        {
            // Re-discover immediately before starting; old checkbox state is not treated as authorization.
            var current = await _discovery.DiscoverAsync(_operation.Token);
            var eligible = MultiScreenSessionsPolicy.SelectAuthorizedDevices(requested, current, availableSlots);
            if (eligible.Count == 0)
            {
                _status.Text = "لم يعد أي جهاز محدد مصرحًا به في قائمة ADB الحالية. حدّث الأجهزة ووافق على الهاتف.";
                return;
            }

            foreach (var device in eligible)
            {
                _operation.Token.ThrowIfCancellationRequested();
                try
                {
                    await StartSessionAsync(device, _operation.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failures.Add($"{device.MaskedSerial}: {ex.Message}");
                }
            }

            _status.Text = failures.Count == 0
                ? $"تم تشغيل {eligible.Count} جلسات مستقلة بنجاح. الجلسات النشطة: {_sessions.Count}/{MultiScreenSessionsPolicy.MaximumConcurrentSessions}."
                : $"الجلسات النشطة: {_sessions.Count}/{MultiScreenSessionsPolicy.MaximumConcurrentSessions}. تعذّر تشغيل بعض الأجهزة:\n" + string.Join("\n", failures);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء فتح جلسات العرض.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح الجلسات: " + ex.Message;
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            if (!_closing)
            {
                _refreshButton.IsEnabled = true;
                _startButton.IsEnabled = true;
                await RefreshDevicesAsync();
            }
            UpdateSessionStatus();
        }
    }

    private async Task StartSessionAsync(DeviceListItem device, CancellationToken cancellationToken)
    {
        if (_sessions.ContainsKey(device.Serial))
            return;
        if (_sessions.Count >= MultiScreenSessionsPolicy.MaximumConcurrentSessions)
            throw new InvalidOperationException("وصلت إلى الحد الأقصى لأربع جلسات عرض مستقلة.");

        var mirror = new ScreenMirrorService(_adb);
        var host = new ScrcpyWindowHost();
        var status = new TextBlock
        {
            Text = "جارٍ تجهيز جلسة العرض...",
            Foreground = Brush("#9FC7E8"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(3, 4, 3, 6)
        };
        var stop = ButtonControl("إيقاف الجلسة", "#713B43", (_, _) => StopSession(device.Serial));
        var cardBody = new DockPanel();
        var cardHeader = new DockPanel { Margin = new Thickness(6) };
        var title = new TextBlock
        {
            Text = $"{device.DisplayName} • {device.MaskedSerial}",
            Foreground = Brush("#D8ECFA"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(stop, Dock.Left);
        cardHeader.Children.Add(stop);
        cardHeader.Children.Add(title);
        DockPanel.SetDock(cardHeader, Dock.Top);
        cardBody.Children.Add(cardHeader);
        var hostBorder = new Border
        {
            Background = Brush("#050B12"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            MinHeight = 260,
            Child = host
        };
        cardBody.Children.Add(hostBorder);
        DockPanel.SetDock(status, Dock.Bottom);
        cardBody.Children.Add(status);

        var card = new Border
        {
            Width = 620,
            Margin = new Thickness(5),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#2D4964"),
            BorderThickness = new Thickness(1),
            Child = cardBody
        };

        var active = new ActiveSession(device.Serial, mirror, host, card, status);
        _sessions.Add(device.Serial, active);
        _sessionCards.Children.Add(card);
        UpdateSessionStatus();

        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while ((!host.IsLoaded || host.NativeHostHandle == IntPtr.Zero) && DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken);
            }

            if (!host.IsLoaded || host.NativeHostHandle == IntPtr.Zero)
                throw new TimeoutException("لم يتم إنشاء مساحة العرض لهذا الجهاز.");

            await mirror.StartEmbeddedAsync(
                device.Serial,
                string.IsNullOrWhiteSpace(device.Model) ? device.DisplayName : device.Model,
                host,
                cancellationToken);
            status.Text = "الجلسة نشطة. إيقافها من هذا الإطار لا يؤثر على بقية الهواتف.";
        }
        catch
        {
            StopSession(device.Serial);
            throw;
        }
    }

    private void StopSession(string serial)
    {
        if (!_sessions.Remove(serial, out var active))
            return;

        active.Mirror.Dispose();
        _sessionCards.Children.Remove(active.Card);
        UpdateSessionStatus();
    }

    private void StopAllSessions()
    {
        foreach (var serial in _sessions.Keys.ToArray())
            StopSession(serial);
        _stopAllButton.IsEnabled = false;
    }

    private void UpdateSessionStatus()
    {
        _stopAllButton.IsEnabled = _sessions.Count > 0;
        if (_status is not null && !_closing)
            _status.Text = $"الجلسات النشطة: {_sessions.Count}/{MultiScreenSessionsPolicy.MaximumConcurrentSessions}. كل جلسة تملك عملية scrcpy ومساحة عرض مستقلة.";
    }

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 130,
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

    private sealed record ActiveSession(
        string Serial,
        ScreenMirrorService Mirror,
        ScrcpyWindowHost Host,
        Border Card,
        TextBlock Status);
}
