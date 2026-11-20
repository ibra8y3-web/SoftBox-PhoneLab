using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;
using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class WorkshopInventoryIntegrationTests
{
    [Fact]
    public async Task ConsumingPartAtomicallyDeductsStockAndLinksCostAndMovementToOrder()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new WorkshopStore(root);
            var order = await store.CreateRepairOrderAsync(new RepairOrder
            {
                CustomerName = "Workshop Customer",
                CustomerPhone = "01000000000",
                DeviceModel = "Samsung SM-A525F",
                IntakeSymptoms = "Battery drains rapidly",
                TechnicianName = "Technician A"
            });
            await store.UpsertInventoryItemAsync(new InventoryItem
            {
                Sku = "BAT-A525",
                Name = "Battery SM-A525F",
                Quantity = 5,
                MinimumQuantity = 1,
                UnitCostEgp = 250,
                SalePriceEgp = 320,
                Category = "Battery",
                Supplier = "Authorized local supplier"
            });

            var usage = await store.ConsumeInventoryPartForOrderAsync(
                order.Id, "BAT-A525", 2, "Technician A", "Battery replacement");

            var snapshot = await store.GetSnapshotAsync();
            var savedOrder = Assert.Single(snapshot.RepairOrders);
            var item = Assert.Single(snapshot.Inventory);
            var movement = Assert.Single(snapshot.InventoryMovements, entry => entry.RepairOrderId == order.Id);

            Assert.Equal(3, item.Quantity);
            Assert.Equal("BAT-A525", usage.Sku);
            Assert.Equal(500m, savedOrder.PartsCostEgp);
            Assert.Equal(2, Assert.Single(savedOrder.PartsUsed).Quantity);
            Assert.Equal(-2, movement.Delta);
            Assert.Equal(3, movement.BalanceAfter);
            Assert.Equal(order.TicketNumber, movement.RepairTicketNumber);
            Assert.Equal("Technician A", movement.TechnicianName);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InsufficientStockDoesNotChangeInventoryOrOrder()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new WorkshopStore(root);
            var order = await store.CreateRepairOrderAsync(new RepairOrder
            {
                CustomerName = "Workshop Customer",
                DeviceModel = "Pixel 7",
                IntakeSymptoms = "Charging issue"
            });
            await store.UpsertInventoryItemAsync(new InventoryItem
            {
                Sku = "USB-C-PORT",
                Name = "USB-C charging port",
                Quantity = 1,
                MinimumQuantity = 0,
                UnitCostEgp = 75
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ConsumeInventoryPartForOrderAsync(order.Id, "USB-C-PORT", 2, "Tech"));

            var snapshot = await store.GetSnapshotAsync();
            Assert.Equal(1, Assert.Single(snapshot.Inventory).Quantity);
            var savedOrder = Assert.Single(snapshot.RepairOrders);
            Assert.Empty(savedOrder.PartsUsed);
            Assert.Equal(0m, savedOrder.PartsCostEgp);
            Assert.DoesNotContain(snapshot.InventoryMovements, entry => entry.RepairOrderId == order.Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SchemaV2DatabaseMigratesToV5WithoutLosingExistingOrdersOrStock()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "workshop.json");
            await File.WriteAllTextAsync(path, """
                {
                  "SchemaVersion": 2,
                  "UpdatedAtUtc": "2026-01-01T00:00:00+00:00",
                  "Settings": { "BusinessName": "Existing Workshop", "CurrencyCode": "EGP", "CultureName": "ar-EG", "DefaultWarrantyMonths": 3, "TimeZoneId": "UTC" },
                  "Customers": [],
                  "RepairOrders": [
                    { "Id": "11111111-1111-1111-1111-111111111111", "TicketNumber": "SB-OLD-001", "CustomerName": "Old Customer", "DeviceModel": "Old Phone", "IntakeSymptoms": "Old issue", "Status": "تم الاستلام", "WarrantyMonths": 3, "Timeline": [] }
                  ],
                  "Inventory": [
                    { "Id": "22222222-2222-2222-2222-222222222222", "Sku": "OLD-PART", "Name": "Old part", "Quantity": 7, "MinimumQuantity": 1, "UnitCostEgp": 15, "Location": "A1" }
                  ],
                  "Events": []
                }
                """);

            var store = new WorkshopStore(root);
            var snapshot = await store.GetSnapshotAsync();

            Assert.Single(snapshot.RepairOrders);
            Assert.Equal("SB-OLD-001", snapshot.RepairOrders[0].TicketNumber);
            Assert.Empty(snapshot.RepairOrders[0].PartsUsed);
            Assert.Equal(0m, snapshot.RepairOrders[0].PartsCostEgp);
            Assert.Equal(7, Assert.Single(snapshot.Inventory).Quantity);
            Assert.NotNull(snapshot.InventoryMovements);
            Assert.NotNull(snapshot.InventoryPurchaseReceipts);
            Assert.Empty(snapshot.InventoryPurchaseReceipts);
            Assert.Empty(snapshot.RepairOrders[0].QuoteDecisionHistory);
            Assert.Empty(snapshot.RepairOrders[0].Payments);
            Assert.Equal(5, snapshot.SchemaVersion);
            Assert.True(WindowsUserDataProtection.IsProtected(await File.ReadAllBytesAsync(path)));

            var reloaded = await new WorkshopStore(root).GetSnapshotAsync();
            Assert.Equal(5, reloaded.SchemaVersion);
            Assert.Equal("Old part", Assert.Single(reloaded.Inventory).Name);
            Assert.Single(reloaded.RepairOrders);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SupplierReceiptAddsStockAndUpdatesWeightedAverageCostAtomically()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new WorkshopStore(root);
            await store.UpsertInventoryItemAsync(new InventoryItem
            {
                Sku = "USB-C-PORT",
                Name = "USB-C charging port",
                Quantity = 10,
                MinimumQuantity = 2,
                UnitCostEgp = 50m,
                Supplier = "Previous supplier"
            });

            var receipt = await store.ReceiveSupplierStockAsync(
                "USB-C-PORT",
                quantity: 4,
                purchaseUnitCostEgp: 80m,
                supplier: "New Parts Supplier",
                invoiceReference: "INV-2026-105",
                technicianName: "Receiving Technician");

            var snapshot = await store.GetSnapshotAsync();
            var item = Assert.Single(snapshot.Inventory);
            var savedReceipt = Assert.Single(snapshot.InventoryPurchaseReceipts);
            var purchaseMovement = Assert.Single(snapshot.InventoryMovements, movement =>
                movement.Reason.Contains("New Parts Supplier", StringComparison.Ordinal));

            Assert.Equal(14, item.Quantity);
            Assert.Equal("New Parts Supplier", item.Supplier);
            Assert.Equal(58.57m, item.UnitCostEgp);
            Assert.Equal(4, receipt.Quantity);
            Assert.Equal(320m, receipt.TotalCostEgp);
            Assert.Equal("INV-2026-105", savedReceipt.InvoiceReference);
            Assert.Equal(58.57m, savedReceipt.WeightedAverageUnitCostAfterEgp);
            Assert.Equal(14, savedReceipt.BalanceAfter);
            Assert.Equal(4, purchaseMovement.Delta);
            Assert.Equal(14, purchaseMovement.BalanceAfter);
            Assert.Equal(80m, purchaseMovement.UnitCostEgp);
            Assert.Contains("INV-2026-105", purchaseMovement.Reason);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidSupplierReceiptDoesNotChangeInventoryOrCreateReceipt()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new WorkshopStore(root);
            await store.UpsertInventoryItemAsync(new InventoryItem
            {
                Sku = "DISPLAY-001",
                Name = "Display assembly",
                Quantity = 2,
                UnitCostEgp = 900m
            });

            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.ReceiveSupplierStockAsync(
                    "DISPLAY-001", 3, 850m, "", "INV-1", "Technician"));

            var snapshot = await store.GetSnapshotAsync();
            Assert.Equal(2, Assert.Single(snapshot.Inventory).Quantity);
            Assert.Empty(snapshot.InventoryPurchaseReceipts);
            Assert.Single(snapshot.InventoryMovements);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public async Task WorkshopBackupIsEncryptedAndRestoresToAnotherLocalStore()
    {
        var sourceRoot = CreateTempDirectory();
        var targetRoot = CreateTempDirectory();
        try
        {
            var source = new WorkshopStore(sourceRoot);
            await source.CreateRepairOrderAsync(new RepairOrder
            {
                CustomerName = "Private Backup Customer",
                CustomerPhone = "01000000000",
                DeviceModel = "Pixel 8",
                IntakeSymptoms = "Battery issue"
            });

            var backupPath = Path.Combine(sourceRoot, "manual.sbxbackup");
            await source.CreateBackupAsync(backupPath);
            var backupBytes = await File.ReadAllBytesAsync(backupPath);
            Assert.True(WindowsUserDataProtection.IsProtected(backupBytes));
            Assert.DoesNotContain("Private Backup Customer", System.Text.Encoding.UTF8.GetString(backupBytes));

            var target = new WorkshopStore(targetRoot);
            await target.RestoreBackupAsync(backupPath);
            var restored = await target.GetSnapshotAsync();
            Assert.Equal("Private Backup Customer", Assert.Single(restored.RepairOrders).CustomerName);
            Assert.True(WindowsUserDataProtection.IsProtected(await File.ReadAllBytesAsync(target.DatabasePath)));
        }
        finally
        {
            if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, recursive: true);
            if (Directory.Exists(targetRoot)) Directory.Delete(targetRoot, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-workshop-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
