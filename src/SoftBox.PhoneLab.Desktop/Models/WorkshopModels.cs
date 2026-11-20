namespace SoftBox.PhoneLab.Desktop.Models;

public sealed class WorkshopData
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<CustomerRecord> Customers { get; set; } = [];
    public List<RepairOrder> RepairOrders { get; set; } = [];
    public List<InventoryItem> Inventory { get; set; } = [];
    public List<WorkshopEvent> Events { get; set; } = [];
}

public sealed class CustomerRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RepairOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TicketNumber { get; set; } = "";
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string DeviceModel { get; set; } = "";
    public string SerialOrImeiNote { get; set; } = "";
    public string IntakeSymptoms { get; set; } = "";
    public string FindingsBeforeRepair { get; set; } = "";
    public string WorkPerformed { get; set; } = "";
    public string ResultsAfterRepair { get; set; } = "";
    public string Status { get; set; } = "تم الاستلام";
    public decimal EstimatedCostEgp { get; set; }
    public decimal FinalCostEgp { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeliveredAtUtc { get; set; }
}

public sealed class InventoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public int MinimumQuantity { get; set; }
    public decimal UnitCostEgp { get; set; }
    public string Location { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool NeedsRestock => Quantity <= MinimumQuantity;
}

public sealed class WorkshopEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Category { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Description { get; set; } = "";
}
