using SoftBox.PhoneLab.Core.Activity;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DashboardAnalyticsServiceTests
{
    [Fact]
    public void AnalyzeBuildsDeviceEventNotificationAndFourteenDayMetrics()
    {
        var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new LocalActivitySnapshot(
            [
                History("device-a", "Connected", now.AddHours(-2)),
                History("device-a", "Connected", now.AddDays(-2)),
                History("device-b", "Inspection", now.AddDays(-8)),
                History("device-a", "Old event", now.AddDays(-40)),
                History("device-c", "Future event", now.AddHours(1))
            ],
            [
                Notification("device", now.AddHours(-1), false),
                Notification("device", now.AddDays(-3), true),
                Notification("system", now.AddDays(-10), false),
                Notification("old", now.AddDays(-31), false)
            ]);

        var report = DashboardAnalyticsService.Analyze(snapshot, now);

        Assert.Equal(3, report.KnownDevices);
        Assert.Equal(5, report.TotalHistoryEvents);
        Assert.Equal(1, report.EventsLast24Hours);
        Assert.Equal(2, report.EventsLast7Days);
        Assert.Equal(3, report.EventsLast30Days);
        Assert.Equal(4, report.TotalNotifications);
        Assert.Equal(3, report.UnreadNotifications);
        Assert.Equal(2, report.NotificationsLast7Days);
        Assert.Equal("Connected", report.MostCommonEvents[0].Label);
        Assert.Equal(2, report.MostCommonEvents[0].Count);
        Assert.Equal("device", report.NotificationCategories[0].Label);
        Assert.Equal(2, report.NotificationCategories[0].Count);
        Assert.Equal(14, report.DailyActivity.Count);
        Assert.Equal(1, report.DailyActivity[^1].HistoryEvents);
        Assert.Equal(1, report.DailyActivity[^1].Notifications);
    }

    private static DeviceHistoryEntry History(string fingerprint, string type, DateTimeOffset at)
        => new(Guid.NewGuid(), fingerprint, "••••1234", "Test", "Model", type, "Recorded event", "", at);

    private static SoftBoxNotification Notification(string category, DateTimeOffset at, bool isRead)
        => new(Guid.NewGuid(), at, category, "Title", "Message", "dashboard", null, isRead);
}
