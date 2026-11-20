using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;
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
            var store = new WorkshopStore(Path.Combine(root, "workshop.json"));
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
            var movement = Assert.Single(snapshot.InventoryMovements.Where(entry => entry.RepairOrderId == order.Id));

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
            var store = new WorkshopStore(Path.Combine(root, "workshop.json"));
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
    public async Task SchemaV2DatabaseMigratesToV3WithoutLosingExistingOrdersOrStock()
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

            var store = new WorkshopStore(Path.Combine(root, "workshop.json"));
            var snapshot = await store.GetSnapshotAsync();

            Assert.Equal(1, snapshot.RepairOrders.Count);
            Assert.Equal("SB-OLD-001", snapshot.RepairOrders[0].TicketNumber);
            Assert.Empty(snapshot.RepairOrders[0].PartsUsed);
            Assert.Equal(0m, snapshot.RepairOrders[0].PartsCostEgp);
            Assert.Equal(7, Assert.Single(snapshot.Inventory).Quantity);
            Assert.NotNull(snapshot.InventoryMovements);
            Assert.Equal(3, snapshot.SchemaVersion);

            var reloaded = await new WorkshopStore(path).GetSnapshotAsync();
            Assert.Equal(3, reloaded.SchemaVersion);
            Assert.Equal("Old part", Assert.Single(reloaded.Inventory).Name);
            Assert.Single(reloaded.RepairOrders);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-workshop-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
