using Xunit;
using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Tests;

public sealed class WorkshopAnalyticsServiceTests
{
    [Fact]
    public void AnalyzeUsesSavedOrderStatesAndDoesNotCountOpenEstimatesAsRevenue()
    {
        var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var data = new WorkshopData
        {
            RepairOrders =
            [
                new RepairOrder
                {
                    TicketNumber = "SB-1", Status = "تم التسليم", DeviceModel = "Pixel 8",
                    IntakeSymptoms = "Broken display", TechnicianName = "Mina",
                    ReceivedAtUtc = now.AddHours(-24), DeliveredAtUtc = now,
                    FinalCostEgp = 200m, EstimatedCostEgp = 180m, PartsCostEgp = 50m,
                    QuoteDecisionHistory = [new CustomerQuoteDecisionRecord { Decision = CustomerQuoteDecision.Approved, QuotedAmountEgp = 200m }],
                    Payments = [new RepairPaymentRecord { AmountEgp = 50m, Method = "نقدًا" }]
                },
                new RepairOrder
                {
                    TicketNumber = "SB-2", Status = "قيد الإصلاح", DeviceModel = "Pixel 8",
                    IntakeSymptoms = "Broken display", TechnicianName = "Mina",
                    ReceivedAtUtc = now.AddDays(-2), EstimatedCostEgp = 120m
                },
                new RepairOrder
                {
                    TicketNumber = "SB-3", Status = "ملغي", DeviceModel = "Pixel 8",
                    IntakeSymptoms = "Battery drains", TechnicianName = "Mina",
                    EstimatedCostEgp = 400m
                },
                new RepairOrder
                {
                    TicketNumber = "SB-4", Status = "Delivered", DeviceModel = "Galaxy A54",
                    IntakeSymptoms = "Camera focus issue", TechnicianName = "Sara",
                    FinalCostEgp = 300m, PartsCostEgp = 100m,
                    QuoteDecisionHistory = [new CustomerQuoteDecisionRecord { Decision = CustomerQuoteDecision.Approved, QuotedAmountEgp = 300m }]
                }
            ],
            Inventory =
            [
                new InventoryItem { Sku = "A", Name = "Part A", Quantity = 2, MinimumQuantity = 2, UnitCostEgp = 25m },
                new InventoryItem { Sku = "B", Name = "Part B", Quantity = 8, MinimumQuantity = 2, UnitCostEgp = 10m }
            ]
        };

        var report = WorkshopAnalyticsService.Analyze(data, now);

        Assert.Equal(4, report.TotalOrders);
        Assert.Equal(1, report.OpenOrders);
        Assert.Equal(2, report.DeliveredOrders);
        Assert.Equal(1, report.CancelledOrders);
        Assert.Equal(500m, report.RecordedDeliveredRevenue);
        Assert.Equal(120m, report.EstimatedOpenWorkValue);
        Assert.Equal(150m, report.RecordedPartsCost);
        Assert.Equal(50m, report.TotalRecordedPayments);
        Assert.Equal(450m, report.ApprovedOutstandingBalance);
        Assert.Equal(24d, report.AverageDeliveryHours.GetValueOrDefault(), 0.001d);
        Assert.Equal(1, report.LowStockItems);
        Assert.Equal(130m, report.InventoryValueAtCost);
        Assert.Equal("Broken display", report.FrequentReportedFaults[0].Label);
        Assert.Equal(2, report.FrequentReportedFaults[0].Count);
        Assert.Equal("Pixel 8", report.MostServicedModels[0].Label);
        Assert.Equal(3, report.MostServicedModels[0].Count);
        Assert.Equal(3, report.TechnicianPerformance[0].AssignedOrders);
        Assert.Equal(1, report.TechnicianPerformance[0].DeliveredOrders);
        Assert.Equal(200m, report.TechnicianPerformance[0].RecordedDeliveredRevenue);
        Assert.Equal("2026-11", report.MonthlyDeliveredRevenue[^1].Month);
        Assert.Equal(1, report.MonthlyDeliveredRevenue[^1].DeliveredOrders);
        Assert.Equal(200m, report.MonthlyDeliveredRevenue[^1].RecordedRevenue);
    }

    [Fact]
    public async Task ExportCsvEscapesUserTextAndIncludesCurrencyAndAnalytics()
    {
        var root = Path.Combine(Path.GetTempPath(), "softbox-analytics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "analytics.csv");
        try
        {
            var data = new WorkshopData
            {
                Settings = new WorkshopSettings { CurrencyCode = "EGP" },
                RepairOrders =
                [
                    new RepairOrder
                    {
                        TicketNumber = "SB-1", Status = "تم التسليم",
                        DeviceModel = @"=HYPERLINK(""bad"")", IntakeSymptoms = "+Formula",
                        FinalCostEgp = 250m, DeliveredAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            };

            await WorkshopAnalyticsService.ExportCsvAsync(data, path);

            var csv = await File.ReadAllTextAsync(path);
            Assert.Contains("\"EGP\"", csv);
            Assert.Contains("Recorded delivered revenue", csv);
            Assert.Contains("Total manually recorded payments", csv);
            Assert.Contains("Outstanding approved quote balances", csv);
            Assert.Contains("'=HYPERLINK", csv);
            Assert.Contains("'+Formula", csv);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
