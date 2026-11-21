using SoftBox.PhoneLab.Core.Activity;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class RepairHistoryQueryTests
{
    [Fact]
    public void FiltersBySearchTextAndEventTypeAndReturnsNewestFirst()
    {
        var older = Entry("Inspection", "Samsung", "Galaxy A", "Battery reading complete",
            DateTimeOffset.Parse("2026-11-19T10:00:00Z"));
        var newer = Entry("Firmware", "Xiaomi", "Redmi Note", "Archive inspected",
            DateTimeOffset.Parse("2026-11-20T10:00:00Z"));
        var newest = Entry("Inspection", "Samsung", "Galaxy S", "Storage inspected",
            DateTimeOffset.Parse("2026-11-21T10:00:00Z"));

        var filtered = RepairHistoryQuery.Filter(
            [older, newer, newest],
            searchText: "samsung",
            eventType: "inspection");

        Assert.Equal(new[] { newest.Id, older.Id }, filtered.Select(item => item.Id));
    }

    [Fact]
    public void CsvContainsMaskedSerialButNeverExportsDeviceFingerprint()
    {
        var entry = Entry("Inspection", "Samsung", "Galaxy A", "Battery reading complete",
            DateTimeOffset.Parse("2026-11-19T10:00:00Z"));
        var csv = RepairHistoryQuery.ToCsv([entry]);

        Assert.Contains("••••1234", csv);
        Assert.DoesNotContain(entry.DeviceFingerprint, csv);
        Assert.DoesNotContain("R58M123456A", csv);
    }

    [Fact]
    public void CsvEscapesQuotesAndNeutralizesSpreadsheetFormulas()
    {
        var entry = Entry("Inspection", "Samsung", "Galaxy A", "=HYPERLINK(\"https://example.invalid\")",
            DateTimeOffset.Parse("2026-11-19T10:00:00Z")) with
        {
            Evidence = "Evidence says \"review\""
        };

        var csv = RepairHistoryQuery.ToCsv([entry]);

        Assert.Contains("\"'=HYPERLINK", csv);
        Assert.Contains("\"\"review\"\"", csv);
    }

    private static DeviceHistoryEntry Entry(
        string eventType,
        string manufacturer,
        string model,
        string summary,
        DateTimeOffset atUtc)
        => new(
            Guid.NewGuid(),
            new string('a', 64),
            "••••1234",
            manufacturer,
            model,
            eventType,
            summary,
            "Evidence",
            atUtc);
}
