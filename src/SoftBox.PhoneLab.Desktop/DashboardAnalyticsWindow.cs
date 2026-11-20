using SoftBox.PhoneLab.Core.Activity;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Read-only operational dashboard over persisted local device history and notifications.
/// </summary>
public sealed class DashboardAnalyticsWindow : Window
{
    private readonly LocalActivityStore _store;
    private readonly TextBlock _summary = new();
    private readonly DataGrid _events = GridView();
    private readonly DataGrid _categories = GridView();
    private readonly DataGrid _daily = GridView();
    private readonly Button _refresh = ButtonControl("تحديث المؤشرات");
    private CancellationTokenSource? _refreshCts;

    public DashboardAnalyticsWindow(LocalActivityStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Title = "SoftBox | لوحة العمليات والتحليلات";
        Width = 1180;
        Height = 800;
        MinWidth = 900;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _refresh.Click += async (_, _) => await RefreshAsync();
        Content = BuildContent();
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) =>
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
        };
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "لوحة العمليات والتحليلات",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "LOCAL DEVICE ACTIVITY • PERSISTENT NOTIFICATIONS • LAST 14 DAYS",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 8)
        });
        header.Children.Add(new TextBlock
        {
            Text = "تُحسب المؤشرات من سجل الأجهزة والإشعارات المحليين. لا تمثل هذه الشاشة طابور مهام خلفيًا أو عمليات لم تُسجل في سجل النشاط.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _summary.TextWrapping = TextWrapping.Wrap;
        _summary.Foreground = Brush("#9FC7E8");
        _summary.FontSize = 12;
        footer.Children.Add(_summary);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.Children.Add(_refresh);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var content = new StackPanel();
        _events.Columns.Add(Column("نوع العملية", nameof(DashboardFrequency.Label), 2));
        _events.Columns.Add(Column("العدد المسجل", nameof(DashboardFrequency.Count), .7));
        _categories.Columns.Add(Column("فئة الإشعار", nameof(DashboardFrequency.Label), 2));
        _categories.Columns.Add(Column("العدد", nameof(DashboardFrequency.Count), .7));
        _daily.Columns.Add(Column("اليوم UTC", nameof(DashboardDailyActivity.DayUtc), 1));
        _daily.Columns.Add(Column("أحداث الأجهزة", nameof(DashboardDailyActivity.HistoryEvents), .8));
        _daily.Columns.Add(Column("الإشعارات", nameof(DashboardDailyActivity.Notifications), .8));

        content.Children.Add(Section("أكثر العمليات المسجلة", _events, 190));
        content.Children.Add(Section("الإشعارات حسب الفئة", _categories, 170));
        content.Children.Add(Section("النشاط اليومي — آخر 14 يومًا", _daily, 240));

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(scroll);
        return root;
    }

    private async Task RefreshAsync()
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        _refresh.IsEnabled = false;
        try
        {
            var activity = await _store.GetSnapshotAsync(_refreshCts.Token);
            var report = DashboardAnalyticsService.Analyze(activity);
            _events.ItemsSource = report.MostCommonEvents;
            _categories.ItemsSource = report.NotificationCategories;
            _daily.ItemsSource = report.DailyActivity;
            _summary.Text =
                $"أجهزة مسجلة في السجل: {report.KnownDevices}  |  إجمالي الأحداث: {report.TotalHistoryEvents}  |  آخر 24 ساعة: {report.EventsLast24Hours}  |  آخر 7 أيام: {report.EventsLast7Days}  |  آخر 30 يومًا: {report.EventsLast30Days}\n" +
                $"الإشعارات: {report.TotalNotifications}  |  غير مقروءة: {report.UnreadNotifications}  |  إشعارات آخر 7 أيام: {report.NotificationsLast7Days}\n" +
                $"آخر تحديث: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC • البيانات محلية ومشفرة بحساب Windows الحالي.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _summary.Text = "تعذر قراءة سجل العمليات المحلي: " + ex.Message;
            _summary.Foreground = Brush("#FCA5A5");
        }
        finally
        {
            if (IsVisible) _refresh.IsEnabled = true;
        }
    }

    private static UIElement Section(string title, DataGrid grid, double height)
    {
        grid.Height = height;
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#B7DDF7"),
            Margin = new Thickness(5, 3, 5, 3)
        });
        panel.Children.Add(grid);
        return panel;
    }

    private static DataGrid GridView() => new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        IsReadOnly = true,
        Background = Brush("#0D1C2E"),
        Foreground = Brush("#EAF3FF"),
        RowBackground = Brush("#12243A"),
        AlternatingRowBackground = Brush("#172B43"),
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        Margin = new Thickness(0, 5, 0, 0)
    };

    private static DataGridTextColumn Column(string header, string property, double weight)
        => new()
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(property),
            Width = new DataGridLength(weight, DataGridLengthUnitType.Star),
            IsReadOnly = true
        };

    private static Button ButtonControl(string label)
        => new()
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            MinWidth = 135,
            Background = Brush("#14517A"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#2F83B5"),
            FontWeight = FontWeights.SemiBold
        };

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
