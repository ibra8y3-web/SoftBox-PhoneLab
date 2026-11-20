using SoftBox.PhoneLab.Core.Android;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Real Android Runtime permission manager backed by PackageManager pm grant/revoke.
/// Only a revalidated third-party app and permissions present in its Runtime permission
/// section can be changed. Every mutation is confirmed and verified by reading PackageManager again.
/// </summary>
public sealed class AndroidRuntimePermissionsWindow : Window
{
    private readonly AndroidDeviceToolsService _service;
    private readonly string _serial;
    private readonly string _packageName;
    private readonly ObservableCollection<AndroidRuntimePermission> _items = [];
    private readonly ListBox _list = new();
    private readonly TextBlock _status = new();
    private readonly Button _refreshButton;
    private readonly Button _grantButton;
    private readonly Button _revokeButton;
    private bool _busy;

    public AndroidRuntimePermissionsWindow(
        AndroidDeviceToolsService service,
        string serial,
        string packageName)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        if (!AndroidDeviceToolsService.IsSafePackageName(packageName))
            throw new ArgumentException("اسم الحزمة غير صالح.", nameof(packageName));
        _serial = serial;
        _packageName = packageName;

        Title = "SoftBox | مدير صلاحيات التطبيق";
        Width = 920;
        Height = 700;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _refreshButton = ButtonControl("إعادة قراءة الصلاحيات", "#14517A", async (_, _) => await RefreshAsync());
        _grantButton = ButtonControl("منح الصلاحية المحددة", "#176044", async (_, _) => await SetPermissionAsync(granted: true));
        _revokeButton = ButtonControl("سحب الصلاحية المحددة", "#713B43", async (_, _) => await SetPermissionAsync(granted: false));
        _grantButton.IsEnabled = false;
        _revokeButton.IsEnabled = false;

        Content = BuildContent();
        _list.SelectionChanged += (_, _) => UpdateButtons();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "مدير صلاحيات التطبيق",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = $"PACKAGE: {_packageName}\nDEVICE: {Mask(_serial)}",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = Brush("#AFC7DE"),
            Margin = new Thickness(0, 6, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "يعرض الصلاحيات التي طلبها التطبيق وظهرت ضمن Runtime Permissions في PackageManager فقط. عند المنح أو السحب يعيد SoftBox قراءة الحالة بعد تنفيذ pm grant / pm revoke. صلاحيات التثبيت والنظام وPrivileged permissions غير قابلة للتعديل من هذه الشاشة.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(_refreshButton);
        actions.Children.Add(_grantButton);
        actions.Children.Add(_revokeButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _list.ItemsSource = _items;
        _list.Background = Brush("#071625");
        _list.Foreground = Brush("#DCEBFA");
        _list.BorderBrush = Brush("#263D58");
        _list.Padding = new Thickness(10);
        _list.FontFamily = new FontFamily("Consolas");
        _list.FontSize = 11;
        root.Children.Add(_list);

        _status.Text = "جارٍ قراءة الصلاحيات من الهاتف...";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private async Task RefreshAsync()
    {
        await RunAsync("جارٍ إعادة قراءة Runtime Permissions من PackageManager...", async () =>
        {
            var permissions = await _service.GetThirdPartyRuntimePermissionsAsync(_serial, _packageName);
            _items.Clear();
            foreach (var permission in permissions)
                _items.Add(permission);

            _list.SelectedIndex = -1;
            return permissions.Count == 0
                ? "لم يعرض PackageManager صلاحيات Runtime قابلة للإدارة لهذا التطبيق. قد تكون كل الصلاحيات تثبيتية أو غير مطلوبة أو أن تنسيق Android مختلف."
                : $"تم جلب {permissions.Count:N0} صلاحية Runtime فعلية للتطبيق. اختر صلاحية لعرض إجراء المنح أو السحب.";
        });
    }

    private async Task SetPermissionAsync(bool granted)
    {
        if (_list.SelectedItem is not AndroidRuntimePermission selected)
            return;
        if (selected.IsConflicting || !selected.IsGranted.HasValue)
        {
            _status.Text = "لا يمكن تغيير صلاحية بحالة متعارضة أو غير معروفة. أعد قراءة الحالة من الهاتف.";
            return;
        }
        if (selected.IsGranted.Value == granted)
        {
            _status.Text = granted ? "الصلاحية ممنوحة بالفعل." : "الصلاحية مرفوضة بالفعل.";
            return;
        }

        var action = granted ? "منح" : "سحب";
        var confirmation = MessageBox.Show(this,
            $"الجهاز: {_serial}\nالتطبيق: {_packageName}\nالصلاحية: {selected.Name}\n\n" +
            $"سيتم تنفيذ الأمر الحقيقي pm { (granted ? "grant" : "revoke") } على الهاتف، ثم إعادة قراءة PackageManager للتحقق من النتيجة.\nهل تؤكد {action} الصلاحية؟",
            $"تأكيد {action} صلاحية Android",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            _status.Text = "أُلغي التغيير؛ لم يتم إرسال أي أمر إلى الهاتف.";
            return;
        }

        await RunAsync($"جارٍ {action} الصلاحية والتحقق من الحالة الجديدة...", async () =>
        {
            var result = await _service.SetThirdPartyRuntimePermissionAsync(
                _serial, _packageName, selected.Name, granted);
            var permissions = await _service.GetThirdPartyRuntimePermissionsAsync(_serial, _packageName);
            _items.Clear();
            foreach (var permission in permissions)
                _items.Add(permission);
            _list.SelectedItem = _items.FirstOrDefault(item => item.Name == selected.Name);
            return result;
        });
    }

    private async Task RunAsync(string message, Func<Task<string>> action)
    {
        if (_busy)
            return;
        _busy = true;
        _refreshButton.IsEnabled = false;
        _grantButton.IsEnabled = false;
        _revokeButton.IsEnabled = false;
        _status.Text = message;
        try
        {
            _status.Text = await action();
        }
        catch (Exception ex)
        {
            _status.Text = "فشلت العملية: " + ex.Message;
            MessageBox.Show(this, ex.Message, "مدير صلاحيات Android",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            _refreshButton.IsEnabled = true;
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        if (_busy || _list.SelectedItem is not AndroidRuntimePermission selected ||
            selected.IsConflicting || !selected.IsGranted.HasValue)
        {
            _grantButton.IsEnabled = false;
            _revokeButton.IsEnabled = false;
            return;
        }
        _grantButton.IsEnabled = !selected.IsGranted.Value;
        _revokeButton.IsEnabled = selected.IsGranted.Value;
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
            MinWidth = 145,
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
