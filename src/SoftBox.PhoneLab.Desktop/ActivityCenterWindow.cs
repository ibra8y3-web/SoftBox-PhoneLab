using SoftBox.PhoneLab.Core.Activity;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Displays local per-device repair history and a persistent, read-state notification inbox.
/// </summary>
public sealed class ActivityCenterWindow : Window
{
    private readonly LocalActivityStore _store;
    private readonly string? _serial;
    private readonly TabControl _tabs = new();
    private readonly ObservableCollection<ActivityDisplayItem> _history = [];
    private readonly ObservableCollection<ActivityDisplayItem> _notifications = [];
    private readonly ListBox _historyList = new();
    private readonly ListBox _notificationList = new();
    private readonly TextBox _historyDetails = new();
    private readonly TextBox _notificationDetails = new();
    private readonly TextBlock _status = new();
    private readonly Button _refreshButton;
    private readonly Button _markReadButton;
    private readonly Button _markAllReadButton;
    private CancellationTokenSource? _operation;

    public ActivityCenterWindow(LocalActivityStore store, string? serial = null, int initialTab = 0)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (serial is not null && !AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        _serial = serial;

        Title = "SoftBox | سجل الأجهزة والإشعارات";
        Width = 1050;
        Height = 760;
        MinWidth = 800;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _refreshButton = ButtonControl("تحديث السجل", "#14517A", Refresh_Click);
        _markReadButton = ButtonControl("تعليم الإشعار كمقروء", "#176044", MarkRead_Click);
        _markAllReadButton = ButtonControl("تعليم الكل كمقروء", "#243B56", MarkAllRead_Click);

        Content = BuildContent();
        Loaded += async (_, _) =>
        {
            if (initialTab is >= 0 and < 2)
                _tabs.SelectedIndex = initialTab;
            await RefreshAsync();
        };
        Closed += (_, _) => _operation?.Cancel();
        _historyList.SelectionChanged += (_, _) => ShowSelectedHistory();
        _notificationList.SelectionChanged += (_, _) => ShowSelectedNotification();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "سجل الصيانة والإشعارات",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "LOCAL DEVICE HISTORY • HASHED DEVICE ID • PERSISTENT NOTIFICATIONS",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = _serial is null
                ? "السجل المحلي لجميع الأجهزة. تُخزن بصمة رقمية لمعرف الجهاز بدل النص الأصلي، ويظهر Serial مقنّعًا."
                : "السجل المحلي للجهاز المحدد: " + LocalActivityStore.MaskSerial(_serial) + ". لا يتم عرض رقم ADB الخام في هذه النافذة.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        buttons.Children.Add(_refreshButton);
        buttons.Children.Add(_markReadButton);
        buttons.Children.Add(_markAllReadButton);
        DockPanel.SetDock(buttons, Dock.Top);
        root.Children.Add(buttons);

        _tabs.Background = Brush("#0D1C2E");
        _tabs.BorderBrush = Brush("#263D58");
        _tabs.Items.Add(BuildHistoryTab());
        _tabs.Items.Add(BuildNotificationsTab());
        DockPanel.SetDock(_tabs, Dock.Top);
        root.Children.Add(_tabs);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Text = "جارٍ تحميل السجل المحلي...";
        _status.Foreground = Brush("#9FC7E8");
        _status.FontSize = 11;
        footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        return root;
    }

    private TabItem BuildHistoryTab()
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });

        _historyList.ItemsSource = _history;
        _historyList.DisplayMemberPath = nameof(ActivityDisplayItem.DisplayText);
        _historyList.Background = Brush("#071625");
        _historyList.Foreground = Brush("#DCEBFA");
        _historyList.BorderBrush = Brush("#263D58");
        _historyList.Padding = new Thickness(8);
        Grid.SetColumn(_historyList, 0);
        grid.Children.Add(_historyList);

        var details = CreateDetailsBox(_historyDetails);
        details.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);
        return new TabItem { Header = "Repair History", Content = grid };
    }

    private TabItem BuildNotificationsTab()
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });

        _notificationList.ItemsSource = _notifications;
        _notificationList.DisplayMemberPath = nameof(ActivityDisplayItem.DisplayText);
        _notificationList.Background = Brush("#071625");
        _notificationList.Foreground = Brush("#DCEBFA");
        _notificationList.BorderBrush = Brush("#263D58");
        _notificationList.Padding = new Thickness(8);
        Grid.SetColumn(_notificationList, 0);
        grid.Children.Add(_notificationList);

        var details = CreateDetailsBox(_notificationDetails);
        details.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);
        return new TabItem { Header = "Notification Center", Content = grid };
    }

    private static TextBox CreateDetailsBox(TextBox box)
    {
        box.IsReadOnly = true;
        box.AcceptsReturn = true;
        box.TextWrapping = TextWrapping.Wrap;
        box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        box.Background = Brush("#071625");
        box.Foreground = Brush("#DCEBFA");
        box.BorderBrush = Brush("#263D58");
        box.Padding = new Thickness(10);
        box.FontFamily = new FontFamily("Consolas");
        box.FontSize = 10;
        box.Text = "اختر عنصرًا من القائمة لعرض التفاصيل.";
        return box;
    }

    private async Task RefreshAsync()
    {
        if (_operation is not null)
            return;
        _operation = new CancellationTokenSource();
        _refreshButton.IsEnabled = false;
        try
        {
            var history = await _store.GetDeviceHistoryAsync(_serial, _operation.Token);
            var notifications = await _store.GetNotificationsAsync(false, _operation.Token);

            _history.Clear();
            foreach (var entry in history)
                _history.Add(new ActivityDisplayItem(
                    entry.Id,
                    $"{entry.AtUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss} | {entry.MaskedSerial} | {entry.Manufacturer} {entry.Model} | {entry.EventType}",
                    entry));

            _notifications.Clear();
            foreach (var notification in notifications)
                _notifications.Add(new ActivityDisplayItem(
                    notification.Id,
                    $"{(notification.IsRead ? "✓" : "●")} {notification.AtUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss} | {notification.Title}",
                    notification));

            var unread = notifications.Count(item => !item.IsRead);
            _status.Text = $"التاريخ: {_history.Count:N0} سجل | الإشعارات: {_notifications.Count:N0} | غير مقروء: {unread:N0}";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر قراءة سجل النشاط: " + ex.Message;
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _refreshButton.IsEnabled = true;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void MarkRead_Click(object sender, RoutedEventArgs e)
    {
        if (_notificationList.SelectedItem is not ActivityDisplayItem item ||
            item.Payload is not SoftBoxNotification notification)
            return;

        try
        {
            await _store.MarkNotificationReadAsync(notification.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تحديث حالة الإشعار: " + ex.Message;
        }
    }

    private async void MarkAllRead_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _store.MarkAllNotificationsReadAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تعليم الإشعارات كمقروءة: " + ex.Message;
        }
    }

    private void ShowSelectedHistory()
    {
        if (_historyList.SelectedItem is not ActivityDisplayItem item ||
            item.Payload is not DeviceHistoryEntry entry)
            return;

        _historyDetails.Text = string.Join(Environment.NewLine,
        [
            "DEVICE: " + entry.MaskedSerial,
            "MANUFACTURER: " + entry.Manufacturer,
            "MODEL: " + entry.Model,
            "EVENT: " + entry.EventType,
            "TIME UTC: " + entry.AtUtc.ToString("O"),
            "SUMMARY: " + entry.Summary,
            "EVIDENCE: " + entry.Evidence,
            "DEVICE FINGERPRINT: " + entry.DeviceFingerprint
        ]);
    }

    private void ShowSelectedNotification()
    {
        if (_notificationList.SelectedItem is not ActivityDisplayItem item ||
            item.Payload is not SoftBoxNotification notification)
            return;

        _notificationDetails.Text = string.Join(Environment.NewLine,
        [
            "TITLE: " + notification.Title,
            "CATEGORY: " + notification.Category,
            "TIME UTC: " + notification.AtUtc.ToString("O"),
            "MESSAGE: " + notification.Message,
            "MODULE: " + notification.ModuleId,
            "DEVICE: " + (notification.DeviceFingerprint ?? "Global"),
            "READ: " + notification.IsRead
        ]);
    }

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 100,
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

    private sealed record ActivityDisplayItem(Guid Id, string DisplayText, object Payload);
}
