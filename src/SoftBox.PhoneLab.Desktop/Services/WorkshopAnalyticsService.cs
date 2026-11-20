using SoftBox.PhoneLab.Core.Models;
using System.Globalization;
using System.IO;
using System.Text;
using SoftBox.PhoneLab.Desktop.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record WorkshopFrequency(string Label, int Count);

public sealed record WorkshopTechnicianMetric(
    string TechnicianName,
    int AssignedOrders,
    int DeliveredOrders,
    decimal RecordedDeliveredRevenue);

public sealed record WorkshopMonthlyRevenue(
    string Month,
    int DeliveredOrders,
    decimal RecordedRevenue);

public sealed record WorkshopAnalyticsSnapshot(
    int TotalOrders,
    int OpenOrders,
    int DeliveredOrders,
    int CancelledOrders,
    decimal RecordedDeliveredRevenue,
    decimal EstimatedOpenWorkValue,
    decimal RecordedPartsCost,
    decimal TotalRecordedPayments,
    decimal ApprovedOutstandingBalance,
    double? AverageDeliveryHours,
    int LowStockItems,
    decimal InventoryValueAtCost,
    IReadOnlyList<WorkshopFrequency> FrequentReportedFaults,
    IReadOnlyList<WorkshopFrequency> MostServicedModels,
    IReadOnlyList<WorkshopTechnicianMetric> TechnicianPerformance,
    IReadOnlyList<WorkshopMonthlyRevenue> MonthlyDeliveredRevenue);

/// <summary>
/// Deterministic workshop reporting based only on saved repair-order and inventory records.
/// Revenue is counted only for orders explicitly marked delivered. This is not profit,
/// tax accounting, payment reconciliation or a statutory accounting report.
/// </summary>
public static class WorkshopAnalyticsService
{
    public static WorkshopAnalyticsSnapshot Analyze(WorkshopData data, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var orders = data.RepairOrders ?? [];
        var inventory = data.Inventory ?? [];
        var delivered = orders.Where(IsDelivered).ToArray();
        var cancelled = orders.Where(IsCancelled).ToArray();
        var open = orders.Where(order => !IsDelivered(order) && !IsCancelled(order)).ToArray();

        var revenue = delivered.Sum(order => NonNegative(order.FinalCostEgp));
        var estimatedOpen = open.Sum(order => NonNegative(order.EstimatedCostEgp));
        var partsCost = orders.Sum(order => NonNegative(order.PartsCostEgp));
        var totalPayments = orders.Sum(order => NonNegative(order.TotalPaidEgp));
        var approvedOutstanding = orders
            .Where(order => order.HasCurrentQuoteApproval)
            .Sum(order => NonNegative(order.OutstandingBalanceEgp));

        var durations = delivered
            .Where(order => order.DeliveredAtUtc.HasValue && order.DeliveredAtUtc.Value >= order.ReceivedAtUtc)
            .Select(order => (order.DeliveredAtUtc!.Value - order.ReceivedAtUtc).TotalHours)
            .Where(hours => double.IsFinite(hours) && hours >= 0)
            .ToArray();

        var now = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var currentMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var firstMonth = currentMonth.AddMonths(-11);
        var monthly = Enumerable.Range(0, 12)
            .Select(index => firstMonth.AddMonths(index))
            .Select(month =>
            {
                var monthOrders = delivered.Where(order =>
                    order.DeliveredAtUtc.HasValue &&
                    order.DeliveredAtUtc.Value.ToUniversalTime().Year == month.Year &&
                    order.DeliveredAtUtc.Value.ToUniversalTime().Month == month.Month).ToArray();
                return new WorkshopMonthlyRevenue(
                    month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    monthOrders.Length,
                    monthOrders.Sum(order => NonNegative(order.FinalCostEgp)));
            })
            .ToArray();

        var technicianMetrics = orders
            .GroupBy(order => NormalizeName(order.TechnicianName, "غير محدد"), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var completed = group.Where(IsDelivered).ToArray();
                return new WorkshopTechnicianMetric(
                    group.Key,
                    group.Count(),
                    completed.Length,
                    completed.Sum(order => NonNegative(order.FinalCostEgp)));
            })
            .OrderByDescending(metric => metric.DeliveredOrders)
            .ThenByDescending(metric => metric.AssignedOrders)
            .ThenBy(metric => metric.TechnicianName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WorkshopAnalyticsSnapshot(
            orders.Count,
            open.Length,
            delivered.Length,
            cancelled.Length,
            revenue,
            estimatedOpen,
            partsCost,
            totalPayments,
            approvedOutstanding,
            durations.Length == 0 ? null : durations.Average(),
            inventory.Count(item => item.NeedsRestock),
            inventory.Sum(item => NonNegative(item.UnitCostEgp) * Math.Max(0, item.Quantity)),
            CountBy(orders, order => order.IntakeSymptoms),
            CountBy(orders, order => order.DeviceModel),
            technicianMetrics,
            monthly);
    }

    public static async Task ExportCsvAsync(
        WorkshopData data,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var report = Analyze(data);
        var settings = data.Settings ?? new WorkshopSettings();
        var currency = WorkshopInternationalRules.NormalizeCurrencyCode(settings.CurrencyCode);
        var rows = new List<string>
        {
            CsvRow("Type", "Name", "Count", "Amount", "Currency", "DurationHours"),
            CsvRow("Summary", "Total orders", report.TotalOrders, "", currency, ""),
            CsvRow("Summary", "Open orders", report.OpenOrders, "", currency, ""),
            CsvRow("Summary", "Delivered orders", report.DeliveredOrders, "", currency, ""),
            CsvRow("Summary", "Cancelled orders", report.CancelledOrders, "", currency, ""),
            CsvRow("Summary", "Recorded delivered revenue", "", report.RecordedDeliveredRevenue, currency, ""),
            CsvRow("Summary", "Estimated open work value", "", report.EstimatedOpenWorkValue, currency, ""),
            CsvRow("Summary", "Recorded parts cost", "", report.RecordedPartsCost, currency, ""),
            CsvRow("Summary", "Total manually recorded payments", "", report.TotalRecordedPayments, currency, ""),
            CsvRow("Summary", "Outstanding approved quote balances", "", report.ApprovedOutstandingBalance, currency, ""),
            CsvRow("Summary", "Low-stock items", report.LowStockItems, "", currency, ""),
            CsvRow("Summary", "Inventory value at cost", "", report.InventoryValueAtCost, currency, ""),
            CsvRow("Summary", "Average delivery hours", "", "", "", report.AverageDeliveryHours?.ToString("0.##", CultureInfo.InvariantCulture) ?? ""),
        };

        rows.AddRange(report.FrequentReportedFaults.Select(item =>
            CsvRow("Reported fault text", item.Label, item.Count, "", currency, "")));
        rows.AddRange(report.MostServicedModels.Select(item =>
            CsvRow("Device model", item.Label, item.Count, "", currency, "")));
        rows.AddRange(report.TechnicianPerformance.Select(item =>
            CsvRow("Technician", item.TechnicianName, item.DeliveredOrders, item.RecordedDeliveredRevenue, currency, "")));
        rows.AddRange(report.MonthlyDeliveredRevenue.Select(item =>
            CsvRow("Monthly delivered revenue", item.Month, item.DeliveredOrders, item.RecordedRevenue, currency, "")));

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, string.Join(Environment.NewLine, rows), new UTF8Encoding(true), cancellationToken);
    }

    private static IReadOnlyList<WorkshopFrequency> CountBy(
        IEnumerable<RepairOrder> orders,
        Func<RepairOrder, string?> selector)
        => orders
            .Select(order => NormalizeName(selector(order), ""))
            .Where(value => value.Length > 0)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WorkshopFrequency(group.Key, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

    private static bool IsDelivered(RepairOrder order)
        => order.Status.Equals("تم التسليم", StringComparison.OrdinalIgnoreCase) ||
           order.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase);

    private static bool IsCancelled(RepairOrder order)
        => order.Status.Equals("ملغي", StringComparison.OrdinalIgnoreCase) ||
           order.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
           order.Status.Equals("Canceled", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static decimal NonNegative(decimal value) => value < 0 ? 0 : value;

    private static string CsvRow(params object?[] values)
        => string.Join(",", values.Select(value => Csv(Convert.ToString(value, CultureInfo.InvariantCulture))));

    private static string Csv(string? value)
    {
        value ??= "";
        if (value.Length > 0 &&
            (value[0] is '=' or '+' or '-' or '@' || value[0] == (char)9 || value[0] == (char)13))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    }
}
