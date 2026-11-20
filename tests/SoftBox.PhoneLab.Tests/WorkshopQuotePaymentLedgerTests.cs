using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class WorkshopQuotePaymentLedgerTests
{
    [Fact]
    public async Task ApprovedQuoteSupportsPartialAndFullPaymentsAndPersistsHistory()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new WorkshopStore(root);
            var order = await CreatePricedOrderAsync(store, 1000m);

            var approval = await store.RecordQuoteDecisionAsync(
                order.Id, CustomerQuoteDecision.Approved, "مكالمة هاتفية", "Technician A", "وافق العميل على السعر.");

            Assert.Equal(1000m, approval.QuotedAmountEgp);
            Assert.Equal(CustomerQuoteDecision.Approved, approval.Decision);

            var partial = await store.RecordPaymentAsync(
                order.Id, 300m, "نقدًا", "Technician A", "CASH-001", "دفعة مقدمة");

            Assert.Equal(700m, partial.BalanceAfterPaymentEgp);

            var middle = await store.GetSnapshotAsync();
            var middleOrder = Assert.Single(middle.RepairOrders);
            Assert.Equal(300m, middleOrder.TotalPaidEgp);
            Assert.Equal(700m, middleOrder.OutstandingBalanceEgp);
            Assert.Equal("مدفوع جزئيًا", middleOrder.PaymentStatus);
            Assert.Single(middleOrder.QuoteDecisionHistory);
            Assert.Single(middleOrder.Payments);

            var final = await store.RecordPaymentAsync(
                order.Id, 700m, "تحويل بنكي", "Technician A", "BANK-002");

            Assert.Equal(0m, final.BalanceAfterPaymentEgp);

            var reloaded = await new WorkshopStore(root).GetSnapshotAsync();
            var saved = Assert.Single(reloaded.RepairOrders);
            Assert.Equal(1000m, saved.TotalPaidEgp);
            Assert.Equal(0m, saved.OutstandingBalanceEgp);
            Assert.Equal("مدفوع", saved.PaymentStatus);
            Assert.Equal(2, saved.Payments.Count);
            Assert.Equal(2, saved.Timeline.Count(item => item.Note.Contains("دفعة", StringComparison.Ordinal)));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task PaymentRequiresApprovalAndCannotExceedOutstandingBalance()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new WorkshopStore(root);
            var order = await CreatePricedOrderAsync(store, 500m);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.RecordPaymentAsync(order.Id, 50m, "نقدًا", "Technician A"));

            await store.RecordQuoteDecisionAsync(
                order.Id, CustomerQuoteDecision.Approved, "حضور العميل", "Technician A");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.RecordPaymentAsync(order.Id, 501m, "نقدًا", "Technician A"));

            var snapshot = await store.GetSnapshotAsync();
            Assert.Empty(Assert.Single(snapshot.RepairOrders).Payments);
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task ChangingAnApprovedPriceRequiresNewApprovalAndCannotUndercutRecordedPayments()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new WorkshopStore(root);
            var order = await CreatePricedOrderAsync(store, 100m);
            await store.RecordQuoteDecisionAsync(
                order.Id, CustomerQuoteDecision.Approved, "حضور العميل", "Technician A");
            await store.RecordPaymentAsync(order.Id, 20m, "نقدًا", "Technician A");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.RecordQuoteDecisionAsync(
                    order.Id, CustomerQuoteDecision.Rejected, "مكالمة", "Technician A"));

            var editedOrder = Assert.Single((await store.GetSnapshotAsync()).RepairOrders);
            editedOrder.FinalCostEgp = 110m;
            await store.UpdateRepairOrderAsync(editedOrder);

            var changed = await store.GetSnapshotAsync();
            var changedOrder = Assert.Single(changed.RepairOrders);
            Assert.Equal("يتطلب اعتماد السعر المعدل", changedOrder.PaymentStatus);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.RecordPaymentAsync(order.Id, 10m, "نقدًا", "Technician A"));

            await store.RecordQuoteDecisionAsync(
                order.Id, CustomerQuoteDecision.Approved, "موافقة هاتفية", "Technician A", "تم اعتماد السعر المعدل.");
            var payment = await store.RecordPaymentAsync(order.Id, 90m, "نقدًا", "Technician A");
            Assert.Equal(0m, payment.BalanceAfterPaymentEgp);

            var reducedOrder = Assert.Single((await store.GetSnapshotAsync()).RepairOrders);
            reducedOrder.FinalCostEgp = 10m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateRepairOrderAsync(reducedOrder));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task CancelledOrdersRejectPaymentsAndPaidOrdersCannotBeCancelled()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new WorkshopStore(root);
            var order = await CreatePricedOrderAsync(store, 200m);
            await store.RecordQuoteDecisionAsync(
                order.Id, CustomerQuoteDecision.Approved, "حضور العميل", "Technician A");
            await store.RecordPaymentAsync(order.Id, 50m, "نقدًا", "Technician A");

            var cancelAttempt = Assert.Single((await store.GetSnapshotAsync()).RepairOrders);
            cancelAttempt.Status = "ملغي";
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateRepairOrderAsync(cancelAttempt));

            var snapshot = await store.GetSnapshotAsync();
            Assert.NotEqual("ملغي", Assert.Single(snapshot.RepairOrders).Status);
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task SchemaV4MigratesToV5AndPreservesExistingOrdersWithoutInventingPayments()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "workshop.json");
            await File.WriteAllTextAsync(path, """
                {
                  "SchemaVersion": 4,
                  "UpdatedAtUtc": "2026-01-01T00:00:00+00:00",
                  "Settings": { "BusinessName": "Existing Workshop", "CurrencyCode": "EGP", "CultureName": "ar-EG", "DefaultWarrantyMonths": 3, "TimeZoneId": "UTC" },
                  "Customers": [],
                  "RepairOrders": [
                    { "Id": "11111111-1111-1111-1111-111111111111", "TicketNumber": "SB-OLD-004", "CustomerName": "Old Customer", "DeviceModel": "Old Phone", "IntakeSymptoms": "Old issue", "Status": "تم الاستلام", "EstimatedCostEgp": 450, "WarrantyMonths": 3, "Timeline": [], "PartsUsed": [] }
                  ],
                  "Inventory": [],
                  "InventoryMovements": [],
                  "InventoryPurchaseReceipts": [],
                  "Events": []
                }
                """);

            var snapshot = await new WorkshopStore(root).GetSnapshotAsync();
            Assert.Equal(5, snapshot.SchemaVersion);
            var order = Assert.Single(snapshot.RepairOrders);
            Assert.Equal("SB-OLD-004", order.TicketNumber);
            Assert.Empty(order.QuoteDecisionHistory);
            Assert.Empty(order.Payments);
            Assert.Equal("بانتظار موافقة العميل", order.PaymentStatus);

            var reload = await new WorkshopStore(root).GetSnapshotAsync();
            Assert.Equal(5, reload.SchemaVersion);
            Assert.Single(reload.RepairOrders);
        }
        finally { DeleteDirectory(root); }
    }

    private static async Task<RepairOrder> CreatePricedOrderAsync(WorkshopStore store, decimal estimate)
        => await store.CreateRepairOrderAsync(new RepairOrder
        {
            CustomerName = "Workshop Customer",
            CustomerPhone = "01000000000",
            DeviceModel = "Android device",
            IntakeSymptoms = "Charging issue",
            TechnicianName = "Technician A",
            EstimatedCostEgp = estimate
        });

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-quote-payments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
