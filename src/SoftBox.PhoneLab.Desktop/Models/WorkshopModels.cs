using System.Text.Json.Serialization;

namespace SoftBox.PhoneLab.Desktop.Models;

public enum CustomerQuoteDecision
{
    Approved,
    Rejected
}

public sealed class CustomerQuoteDecisionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CustomerQuoteDecision Decision { get; set; }
    public decimal QuotedAmountEgp { get; set; }
    public string ContactMethod { get; set; } = "";
    public string Note { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RepairPaymentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public decimal AmountEgp { get; set; }
    public string Method { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Note { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public decimal BalanceAfterPaymentEgp { get; set; }
}

public sealed class WorkshopData
{
    public int SchemaVersion { get; set; } = 5;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public WorkshopSettings Settings { get; set; } = new();
    public List<CustomerRecord> Customers { get; set; } = [];
    public List<RepairOrder> RepairOrders { get; set; } = [];
    public List<InventoryItem> Inventory { get; set; } = [];
    public List<InventoryMovement> InventoryMovements { get; set; } = [];
    public List<InventoryPurchaseReceipt> InventoryPurchaseReceipts { get; set; } = [];
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
    public decimal PartsCostEgp { get; set; }
    public List<RepairOrderPartUsage> PartsUsed { get; set; } = [];
    public int WarrantyMonths { get; set; }
    public DateTimeOffset? WarrantyEndsAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public List<RepairOrderNote> Timeline { get; set; } = [];
    public List<CustomerQuoteDecisionRecord> QuoteDecisionHistory { get; set; } = [];
    public List<RepairPaymentRecord> Payments { get; set; } = [];

    [JsonIgnore]
    public CustomerQuoteDecisionRecord? LatestQuoteDecision
        => (QuoteDecisionHistory ?? []).OrderByDescending(item => item.RecordedAtUtc).FirstOrDefault();

    [JsonIgnore]
    public decimal TotalPaidEgp => (Payments ?? []).Sum(payment => payment.AmountEgp);

    [JsonIgnore]
    public decimal CurrentQuoteAmountEgp => FinalCostEgp > 0 ? FinalCostEgp : EstimatedCostEgp;

    [JsonIgnore]
    public bool HasCurrentQuoteApproval
        => LatestQuoteDecision is { Decision: CustomerQuoteDecision.Approved } decision &&
           decision.QuotedAmountEgp == CurrentQuoteAmountEgp;

    [JsonIgnore]
    public decimal PaymentTargetEgp => HasCurrentQuoteApproval ? LatestQuoteDecision!.QuotedAmountEgp : 0m;

    [JsonIgnore]
    public decimal OutstandingBalanceEgp => Math.Max(0, PaymentTargetEgp - TotalPaidEgp);

    [JsonIgnore]
    public string PaymentStatus
        => LatestQuoteDecision is null ? "بانتظار موافقة العميل"
            : LatestQuoteDecision.Decision == CustomerQuoteDecision.Rejected ? "العرض مرفوض"
            : !HasCurrentQuoteApproval ? "يتطلب اعتماد السعر المعدل"
            : TotalPaidEgp <= 0 ? "غير مدفوع"
            : OutstandingBalanceEgp > 0 ? "مدفوع جزئيًا"
            : "مدفوع";
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
    public string Category { get; set; } = "";
    public string Supplier { get; set; } = "";
    public decimal UnitCostEgp { get; set; }
    public decimal SalePriceEgp { get; set; }
    public string Location { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool NeedsRestock => Quantity <= MinimumQuantity;
}


public sealed class RepairOrderPartUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sku { get; set; } = "";
    public string PartName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitCostEgp { get; set; }
    public decimal TotalCostEgp => UnitCostEgp * Quantity;
    public DateTimeOffset UsedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string TechnicianName { get; set; } = "";
}


public sealed class InventoryPurchaseReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Sku { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string InvoiceReference { get; set; } = "";
    public int Quantity { get; set; }
    public decimal PurchaseUnitCostEgp { get; set; }
    public decimal TotalCostEgp => Quantity * PurchaseUnitCostEgp;
    public decimal WeightedAverageUnitCostAfterEgp { get; set; }
    public int BalanceAfter { get; set; }
    public string TechnicianName { get; set; } = "";
    public Guid InventoryMovementId { get; set; }
}

public sealed class InventoryMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Sku { get; set; } = "";
    public string ItemName { get; set; } = "";
    public int Delta { get; set; }
    public int BalanceAfter { get; set; }
    public decimal UnitCostEgp { get; set; }
    public string Reason { get; set; } = "";
    public Guid? RepairOrderId { get; set; }
    public string RepairTicketNumber { get; set; } = "";
    public string TechnicianName { get; set; } = "";
}

public sealed class WorkshopEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Category { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Description { get; set; } = "";
}
