namespace SoftBox.PhoneLab.Core.Activity;

public sealed record DashboardFrequency(string Label, int Count);

public sealed record DashboardDailyActivity(
    string DayUtc,
    int HistoryEvents,
    int Notifications);

public sealed record DashboardAnalyticsSnapshot(
    int KnownDevices,
    int TotalHistoryEvents,
    int EventsLast24Hours,
    int EventsLast7Days,
    int EventsLast30Days,
    int TotalNotifications,
    int UnreadNotifications,
    int NotificationsLast7Days,
    IReadOnlyList<DashboardFrequency> MostCommonEvents,
    IReadOnlyList<DashboardFrequency> NotificationCategories,
    IReadOnlyList<DashboardDailyActivity> DailyActivity);

/// <summary>
/// Builds dashboard indicators from the persisted, local device history and notification inbox.
/// These metrics describe recorded application activity, not a background job queue.
/// </summary>
public static class DashboardAnalyticsService
{
    public static DashboardAnalyticsSnapshot Analyze(
        LocalActivitySnapshot snapshot,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var now = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var history = snapshot.DeviceHistory ?? [];
        var notifications = snapshot.Notifications ?? [];

        var last24 = now.AddHours(-24);
        var last7 = now.AddDays(-7);
        var last30 = now.AddDays(-30);

        var history24 = history.Where(item => IsWithin(item.AtUtc, last24, now)).ToArray();
        var history7 = history.Where(item => IsWithin(item.AtUtc, last7, now)).ToArray();
        var history30 = history.Where(item => IsWithin(item.AtUtc, last30, now)).ToArray();
        var notifications7 = notifications.Where(item => IsWithin(item.AtUtc, last7, now)).ToArray();

        var startDay = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-13);
        var daily = Enumerable.Range(0, 14)
            .Select(index => startDay.AddDays(index))
            .Select(day =>
            {
                var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                var dayEnd = dayStart.AddDays(1);
                return new DashboardDailyActivity(
                    day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    history.Count(item => IsWithin(item.AtUtc, dayStart, dayEnd)),
                    notifications.Count(item => IsWithin(item.AtUtc, dayStart, dayEnd)));
            })
            .ToArray();

        return new DashboardAnalyticsSnapshot(
            history.Select(item => item.DeviceFingerprint)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            history.Count,
            history24.Length,
            history7.Length,
            history30.Length,
            notifications.Count,
            notifications.Count(item => !item.IsRead),
            notifications7.Length,
            CountBy(history.Select(item => item.EventType), 10),
            CountBy(notifications.Select(item => item.Category), 10),
            daily);
    }

    private static bool IsWithin(DateTimeOffset timestamp, DateTimeOffset start, DateTimeOffset end)
    {
        var value = timestamp.ToUniversalTime();
        return value >= start && value < end;
    }

    private static IReadOnlyList<DashboardFrequency> CountBy(IEnumerable<string?> values, int limit)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DashboardFrequency(group.Key, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
}
