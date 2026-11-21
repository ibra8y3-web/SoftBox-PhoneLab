using SoftBox.PhoneLab.Desktop.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Builds a fresh, fail-closed multi-screen start list from the current ADB discovery.
/// Only selected serials still present as authorized devices are returned.
/// </summary>
public sealed record MultiScreenSessionHealth(
    string Serial,
    bool IsCurrentlyAuthorized,
    bool IsProcessAlive,
    bool HasEmbeddedWindow);

public static class MultiScreenSessionsPolicy
{
    public const int MaximumConcurrentSessions = 4;

    public static bool IsAuthorizedAdbState(string? state)
        => string.Equals(state?.Trim(), "device", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> FindUnhealthySessions(IEnumerable<MultiScreenSessionHealth> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return sessions
            .Where(session => !AdbClient.IsSafeSerial(session.Serial) ||
                              !session.IsCurrentlyAuthorized ||
                              !session.IsProcessAlive ||
                              !session.HasEmbeddedWindow)
            .Select(session => session.Serial)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<DeviceListItem> SelectAuthorizedDevices(
        IEnumerable<string> requestedSerials,
        IReadOnlyList<DeviceListItem> currentDiscovery,
        int maximumSessions = MaximumConcurrentSessions)
    {
        ArgumentNullException.ThrowIfNull(requestedSerials);
        ArgumentNullException.ThrowIfNull(currentDiscovery);
        if (maximumSessions is < 1 or > MaximumConcurrentSessions)
            throw new ArgumentOutOfRangeException(nameof(maximumSessions));

        var currentlyAuthorized = currentDiscovery
            .Where(device => device is not null &&
                             device.IsAuthorized &&
                             AdbClient.IsSafeSerial(device.Serial))
            .GroupBy(device => device.Serial, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var selected = new List<DeviceListItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var serial in requestedSerials)
        {
            if (!AdbClient.IsSafeSerial(serial) || !seen.Add(serial))
                continue;
            if (currentlyAuthorized.TryGetValue(serial, out var device))
                selected.Add(device);
        }

        if (selected.Count > maximumSessions)
            throw new InvalidOperationException(
                $"يمكن تشغيل {maximumSessions} جلسات شاشة متزامنة كحد أقصى في SoftBox.");

        return selected;
    }
}
