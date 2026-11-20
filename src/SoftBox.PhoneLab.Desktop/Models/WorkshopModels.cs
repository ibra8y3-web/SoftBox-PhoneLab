namespace SoftBox.PhoneLab.Desktop.Models;

public sealed class WorkshopData
{
    public int SchemaVersion { get; set; } = 2;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public WorkshopSettings Settings { get; set; } = new();
    public List<CustomerRecord> Customers { get; set; } = [];
    public List<RepairOrder> RepairOrders { get; set; } = [];
    public List<InventoryItem> Inventory { get; set; } = [];
    public List<WorkshopEvent> Events { get; set; } = [];
}

public sealed class WorkshopSettings
{
    public string BusinessName { get; set; } = "SoftBox PhoneLab Repair Center";
    public string Country { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string TaxRegistrationNumber { get; set; } = "";
    public string CurrencyCode { get; set; } = "EGP";
    public string CultureName { get; set; } = "ar-EG";
    public string ReportLanguage { get; set; } = "ar";
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public int DefaultWarrantyMonths { get; set; } = 3;
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
    public string TechnicianName { get; set; } = "";
    public decimal EstimatedCostEgp { get; set; }
    public decimal FinalCostEgp { get; set; }
    public int WarrantyMonths { get; set; }
    public DateTimeOffset? WarrantyEndsAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public List<RepairOrderNote> Timeline { get; set; } = [];
}

public sealed class RepairOrderNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string TechnicianName { get; set; } = "";
    public string Note { get; set; } = "";
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
