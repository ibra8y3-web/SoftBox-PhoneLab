using System.Globalization;
using System.Text;

namespace SoftBox.PhoneLab.Core.Activity;

/// <summary>
/// Filters and exports local device history without exposing raw serials or device fingerprints.
/// </summary>
public static class RepairHistoryQuery
{
    public static IReadOnlyList<DeviceHistoryEntry> Filter(
        IEnumerable<DeviceHistoryEntry> entries,
        string? searchText = null,
        string? eventType = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var search = searchText?.Trim();
        var selectedEvent = eventType?.Trim();

        IEnumerable<DeviceHistoryEntry> query = entries;
        if (!string.IsNullOrWhiteSpace(selectedEvent))
        {
            query = query.Where(entry =>
                entry.EventType.Equals(selectedEvent, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(entry =>
                Contains(entry.MaskedSerial, search) ||
                Contains(entry.Manufacturer, search) ||
                Contains(entry.Model, search) ||
                Contains(entry.EventType, search) ||
                Contains(entry.Summary, search) ||
                Contains(entry.Evidence, search));
        }

        return query.OrderByDescending(entry => entry.AtUtc).ToArray();
    }

    public static string ToCsv(IEnumerable<DeviceHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var builder = new StringBuilder();
        builder.AppendLine("TimeUtc,MaskedSerial,Manufacturer,Model,EventType,Summary,Evidence");

        foreach (var entry in entries.OrderByDescending(item => item.AtUtc))
        {
            builder.AppendLine(string.Join(",", new[]
            {
                CsvField(entry.AtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                CsvField(entry.MaskedSerial),
                CsvField(entry.Manufacturer),
                CsvField(entry.Model),
                CsvField(entry.EventType),
                CsvField(entry.Summary),
                CsvField(entry.Evidence)
            }));
        }

        return builder.ToString();
    }

    private static bool Contains(string? value, string search)
        => (value ?? "").Contains(search, StringComparison.OrdinalIgnoreCase);

    private static string CsvField(string? value)
    {
        var normalized = value ?? "";
        var trimmed = normalized.TrimStart(' ', '\t', '\r', '\n');
        if (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@')
            normalized = "'" + normalized;

        return "\"" + normalized.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
