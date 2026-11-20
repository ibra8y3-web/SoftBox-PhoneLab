using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Offline-first workshop repository. All customer, repair and stock data stays on this PC.
/// Writes are serialized and committed through an atomic temporary-file replacement.
/// </summary>
public sealed class WorkshopStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private WorkshopData? _cache;

    public string DatabasePath { get; }

    public WorkshopStore(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "Workshop");
        Directory.CreateDirectory(root);
        DatabasePath = Path.Combine(root, "workshop.json");
    }

    public async Task<WorkshopData> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            return Clone(_cache!);
        }
        finally { _gate.Release(); }
    }

    public async Task<CustomerRecord> UpsertCustomerAsync(CustomerRecord customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (string.IsNullOrWhiteSpace(customer.Name)) throw new ArgumentException("اسم العميل مطلوب.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var existing = _cache!.Customers.FirstOrDefault(x => x.Id == customer.Id ||
                (!string.IsNullOrWhiteSpace(customer.Phone) && x.Phone.Equals(customer.Phone, StringComparison.OrdinalIgnoreCase)));
            if (existing is null)
            {
                customer.Id = Guid.NewGuid();
                customer.CreatedAtUtc = DateTimeOffset.UtcNow;
                _cache.Customers.Add(customer);
            }
            else
            {
                existing.Name = customer.Name.Trim();
                existing.Phone = customer.Phone.Trim();
                existing.Notes = customer.Notes.Trim();
                customer = existing;
            }
            AddEvent("عميل", customer.Id.ToString("N"), "تم حفظ بيانات العميل محليًا.");
            await SaveLockedAsync(cancellationToken);
            return customer;
        }
        finally { _gate.Release(); }
    }

    public async Task<RepairOrder> CreateRepairOrderAsync(RepairOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (string.IsNullOrWhiteSpace(order.CustomerName)) throw new ArgumentException("اسم العميل مطلوب.");
        if (string.IsNullOrWhiteSpace(order.DeviceModel)) throw new ArgumentException("موديل الجهاز مطلوب.");
        if (string.IsNullOrWhiteSpace(order.IntakeSymptoms)) throw new ArgumentException("وصف العطل عند الاستلام مطلوب.");
        if (order.EstimatedCostEgp < 0 || order.FinalCostEgp < 0) throw new ArgumentOutOfRangeException(nameof(order), "التكلفة لا يمكن أن تكون سالبة.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var customer = _cache!.Customers.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(order.CustomerPhone) &&
                x.Phone.Equals(order.CustomerPhone.Trim(), StringComparison.OrdinalIgnoreCase));
            if (customer is null)
            {
                customer = new CustomerRecord
                {
                    Name = order.CustomerName.Trim(),
                    Phone = order.CustomerPhone.Trim()
                };
                _cache.Customers.Add(customer);
            }
            else
            {
                customer.Name = order.CustomerName.Trim();
            }

            order.Id = Guid.NewGuid();
            order.CustomerId = customer.Id;
            order.TicketNumber = $"SB-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            order.CustomerName = customer.Name;
            order.CustomerPhone = customer.Phone;
            order.DeviceModel = order.DeviceModel.Trim();
            order.IntakeSymptoms = order.IntakeSymptoms.Trim();
            order.Status = "تم الاستلام";
            if (order.WarrantyMonths is < 0 or > 120) order.WarrantyMonths = _cache.Settings.DefaultWarrantyMonths;
            order.Timeline ??= [];
            order.PartsUsed ??= [];
            order.PartsCostEgp = order.PartsUsed.Sum(part => part.TotalCostEgp);
            order.Timeline.Add(new RepairOrderNote { TechnicianName = order.TechnicianName, Note = "Device checked in." });
            order.ReceivedAtUtc = DateTimeOffset.UtcNow;
            order.UpdatedAtUtc = order.ReceivedAtUtc;
            _cache.RepairOrders.Add(order);
            AddEvent("أمر صيانة", order.TicketNumber, "إنشاء أمر صيانة جديد وتسجيل حالة الجهاز عند الاستلام.");
            await SaveLockedAsync(cancellationToken);
            return order;
        }
        finally { _gate.Release(); }
    }

    public async Task UpdateRepairOrderAsync(RepairOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var current = _cache!.RepairOrders.FirstOrDefault(x => x.Id == order.Id)
                ?? throw new KeyNotFoundException("أمر الصيانة غير موجود.");
            var previousStatus = current.Status;
            var nextEstimated = Math.Max(0, order.EstimatedCostEgp);
            var nextFinal = Math.Max(0, order.FinalCostEgp);
            var nextPaymentTarget = nextFinal > 0 ? nextFinal : nextEstimated;
            var alreadyPaid = (current.Payments ?? []).Sum(payment => payment.AmountEgp);
            if (alreadyPaid > nextPaymentTarget)
                throw new InvalidOperationException("لا يمكن خفض إجمالي أمر الصيانة إلى أقل من الدفعات المسجلة. سجّل تسوية مالية منفصلة قبل تعديل القيمة.");
            if (order.Status == "ملغي" && alreadyPaid > 0)
                throw new InvalidOperationException("لا يمكن إلغاء أمر يحتوي على دفعات مسجلة قبل تنفيذ تسوية أو رد دفعة موثق.");
            current.Status = order.Status;
            current.FindingsBeforeRepair = order.FindingsBeforeRepair;
            current.WorkPerformed = order.WorkPerformed;
            current.ResultsAfterRepair = order.ResultsAfterRepair;
            current.EstimatedCostEgp = nextEstimated;
            current.FinalCostEgp = nextFinal;
            current.SerialOrImeiNote = order.SerialOrImeiNote;
            current.TechnicianName = order.TechnicianName;
            if (order.WarrantyMonths is < 0 or > 120) throw new ArgumentOutOfRangeException(nameof(order));
            current.WarrantyMonths = order.WarrantyMonths;
            current.UpdatedAtUtc = DateTimeOffset.UtcNow;
            if (current.Status == "تم التسليم" && current.DeliveredAtUtc is null) current.DeliveredAtUtc = DateTimeOffset.UtcNow;
            if (current.Status == "تم التسليم")
                current.WarrantyEndsAtUtc = WorkshopInternationalRules.CalculateWarrantyEnd(current.DeliveredAtUtc, current.WarrantyMonths);
            else
            {
                current.DeliveredAtUtc = null;
                current.WarrantyEndsAtUtc = null;
            }
            AddEvent("أمر صيانة", current.TicketNumber,
                previousStatus == current.Status ? "تحديث تفاصيل ونتيجة الإصلاح." : $"تغيير الحالة من «{previousStatus}» إلى «{current.Status}».");
            await SaveLockedAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<CustomerQuoteDecisionRecord> RecordQuoteDecisionAsync(
        Guid orderId,
        CustomerQuoteDecision decision,
        string contactMethod,
        string recordedBy,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(decision))
            throw new ArgumentOutOfRangeException(nameof(decision), "قرار العميل غير صالح.");

        contactMethod = (contactMethod ?? "").Trim();
        recordedBy = (recordedBy ?? "").Trim();
        note = (note ?? "").Trim();
        if (contactMethod.Length is < 2 or > 80 || contactMethod.Any(char.IsControl))
            throw new ArgumentException("طريقة توثيق تواصل العميل مطلوبة وبحد أقصى 80 حرفًا.", nameof(contactMethod));
        if (recordedBy.Length is < 2 or > 120 || recordedBy.Any(char.IsControl))
            throw new ArgumentException("اسم الفني الذي يسجل القرار مطلوب وبحد أقصى 120 حرفًا.", nameof(recordedBy));
        if (note.Length > 1000 || note.Any(char.IsControl))
            throw new ArgumentException("ملاحظة القرار غير صالحة أو أطول من 1000 حرف.", nameof(note));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var order = _cache!.RepairOrders.FirstOrDefault(item => item.Id == orderId)
                ?? throw new KeyNotFoundException("أمر الصيانة غير موجود.");
            order.QuoteDecisionHistory ??= [];
            order.Timeline ??= [];
            if (string.Equals(order.Status, "ملغي", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("لا يمكن تسجيل قرار عرض سعر على أمر صيانة ملغي.");
            if (order.QuoteDecisionHistory.Count >= 10000)
                throw new InvalidOperationException("تم بلوغ الحد الأقصى لسجل قرارات العرض لهذا الأمر.");

            var quotedAmount = order.CurrentQuoteAmountEgp;
            if (quotedAmount <= 0 || quotedAmount > 100_000_000m)
                throw new InvalidOperationException("يجب تسجيل قيمة عرض سعر موجبة وصالحة قبل توثيق قرار العميل.");

            var alreadyPaid = (order.Payments ?? []).Sum(payment => payment.AmountEgp);
            if (decision == CustomerQuoteDecision.Rejected && alreadyPaid > 0)
                throw new InvalidOperationException("لا يمكن تسجيل رفض العرض بعد استلام دفعات. سجّل تسوية أو رد دفعة موثق أولًا.");
            if (decision == CustomerQuoteDecision.Approved && quotedAmount < alreadyPaid)
                throw new InvalidOperationException("قيمة العرض الجديد أقل من الدفعات المسجلة بالفعل.");

            var previousUpdatedAt = order.UpdatedAtUtc;
            var previousDatabaseUpdatedAt = _cache.UpdatedAtUtc;
            var previousTimelineCount = order.Timeline.Count;
            var previousEvents = _cache.Events.ToArray();
            var decisionRecord = new CustomerQuoteDecisionRecord
            {
                Decision = decision,
                QuotedAmountEgp = quotedAmount,
                ContactMethod = contactMethod,
                Note = note,
                RecordedBy = recordedBy,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            order.QuoteDecisionHistory.Add(decisionRecord);
            order.UpdatedAtUtc = decisionRecord.RecordedAtUtc;
            order.Timeline.Add(new RepairOrderNote
            {
                AtUtc = decisionRecord.RecordedAtUtc,
                TechnicianName = recordedBy,
                Note = decision == CustomerQuoteDecision.Approved
                    ? $"تم تسجيل موافقة العميل على العرض بقيمة {decisionRecord.QuotedAmountEgp:0.00} عبر {contactMethod}."
                    : $"تم تسجيل رفض العميل للعرض بقيمة {decisionRecord.QuotedAmountEgp:0.00} عبر {contactMethod}."
            });
            AddEvent("قرار عرض سعر", order.TicketNumber,
                $"{decision} | {contactMethod} | بواسطة {recordedBy}");
            try
            {
                await SaveLockedAsync(cancellationToken);
            }
            catch
            {
                order.QuoteDecisionHistory.Remove(decisionRecord);
                if (order.Timeline.Count > previousTimelineCount)
                    order.Timeline.RemoveRange(previousTimelineCount, order.Timeline.Count - previousTimelineCount);
                order.UpdatedAtUtc = previousUpdatedAt;
                _cache.UpdatedAtUtc = previousDatabaseUpdatedAt;
                _cache.Events.Clear();
                _cache.Events.AddRange(previousEvents);
                throw;
            }
            return decisionRecord;
        }
        finally { _gate.Release(); }
    }

    public async Task<RepairPaymentRecord> RecordPaymentAsync(
        Guid orderId,
        decimal amountEgp,
        string method,
        string recordedBy,
        string? reference = null,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (amountEgp <= 0 || amountEgp > 100_000_000m || decimal.Round(amountEgp, 2) != amountEgp)
            throw new ArgumentOutOfRangeException(nameof(amountEgp), "قيمة الدفعة يجب أن تكون موجبة وبحد أقصى منزلتين عشريتين.");
        method = (method ?? "").Trim();
        recordedBy = (recordedBy ?? "").Trim();
        reference = (reference ?? "").Trim();
        note = (note ?? "").Trim();
        if (method.Length is < 2 or > 80 || method.Any(char.IsControl))
            throw new ArgumentException("طريقة الدفع مطلوبة وبحد أقصى 80 حرفًا.", nameof(method));
        if (recordedBy.Length is < 2 or > 120 || recordedBy.Any(char.IsControl))
            throw new ArgumentException("اسم الفني الذي يسجل الدفعة مطلوب وبحد أقصى 120 حرفًا.", nameof(recordedBy));
        if (reference.Length > 100 || reference.Any(char.IsControl))
            throw new ArgumentException("مرجع الدفع غير صالح أو أطول من 100 حرف.", nameof(reference));
        if (note.Length > 500 || note.Any(char.IsControl))
            throw new ArgumentException("ملاحظة الدفع غير صالحة أو أطول من 500 حرف.", nameof(note));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var order = _cache!.RepairOrders.FirstOrDefault(item => item.Id == orderId)
                ?? throw new KeyNotFoundException("أمر الصيانة غير موجود.");
            if (string.Equals(order.Status, "ملغي", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("لا يمكن تسجيل دفعة على أمر صيانة ملغي.");
            order.Payments ??= [];
            if (order.Payments.Count >= 100000)
                throw new InvalidOperationException("تم بلوغ الحد الأقصى لسجل الدفعات لهذا الأمر.");
            if (!string.IsNullOrWhiteSpace(reference) &&
                order.Payments.Any(payment => string.Equals(payment.Reference, reference, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("مرجع الدفع مسجل بالفعل في أمر الصيانة.");
            if (!order.HasCurrentQuoteApproval)
                throw new InvalidOperationException("يجب تسجيل موافقة العميل على السعر الحالي قبل تسجيل دفعة.");

            var paymentTarget = order.PaymentTargetEgp;
            var alreadyPaid = (order.Payments ?? []).Sum(payment => payment.AmountEgp);
            var remaining = paymentTarget - alreadyPaid;
            if (paymentTarget <= 0)
                throw new InvalidOperationException("سجّل قيمة عرض السعر قبل تسجيل الدفعات.");
            if (remaining <= 0)
                throw new InvalidOperationException("تم سداد القيمة المعتمدة بالكامل بالفعل.");
            if (amountEgp > remaining)
                throw new InvalidOperationException($"قيمة الدفعة تتجاوز الرصيد المتبقي ({remaining:0.00}).");

            order.Timeline ??= [];
            var previousUpdatedAt = order.UpdatedAtUtc;
            var previousDatabaseUpdatedAt = _cache.UpdatedAtUtc;
            var previousTimelineCount = order.Timeline.Count;
            var previousEvents = _cache.Events.ToArray();
            var payment = new RepairPaymentRecord
            {
                AmountEgp = amountEgp,
                Method = method,
                Reference = reference,
                Note = note,
                RecordedBy = recordedBy,
                ReceivedAtUtc = DateTimeOffset.UtcNow,
                BalanceAfterPaymentEgp = remaining - amountEgp
            };
            order.Payments.Add(payment);
            order.UpdatedAtUtc = payment.ReceivedAtUtc;
            order.Timeline.Add(new RepairOrderNote
            {
                AtUtc = payment.ReceivedAtUtc,
                TechnicianName = recordedBy,
                Note = $"تم تسجيل دفعة بقيمة {amountEgp:0.00} عبر {method}. الرصيد بعد الدفعة: {payment.BalanceAfterPaymentEgp:0.00}."
            });
            AddEvent("دفعة أمر صيانة", order.TicketNumber,
                $"{amountEgp:0.00} {method} | المتبقي {payment.BalanceAfterPaymentEgp:0.00} | بواسطة {recordedBy}");
            try
            {
                await SaveLockedAsync(cancellationToken);
            }
            catch
            {
                order.Payments.Remove(payment);
                if (order.Timeline.Count > previousTimelineCount)
                    order.Timeline.RemoveRange(previousTimelineCount, order.Timeline.Count - previousTimelineCount);
                order.UpdatedAtUtc = previousUpdatedAt;
                _cache.UpdatedAtUtc = previousDatabaseUpdatedAt;
                _cache.Events.Clear();
                _cache.Events.AddRange(previousEvents);
                throw;
            }
            return payment;
        }
        finally { _gate.Release(); }
    }

    public async Task<InventoryItem> UpsertInventoryItemAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Sku) || string.IsNullOrWhiteSpace(item.Name))
            throw new ArgumentException("كود الصنف واسمه مطلوبان.");
        if (item.Quantity < 0 || item.MinimumQuantity < 0 || item.UnitCostEgp < 0 || item.SalePriceEgp < 0)
            throw new ArgumentOutOfRangeException(nameof(item), "قيم المخزون والتكلفة يجب ألا تكون سالبة.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var current = _cache!.Inventory.FirstOrDefault(x => x.Sku.Equals(item.Sku.Trim(), StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                item.Id = Guid.NewGuid();
                item.Sku = item.Sku.Trim();
                item.Name = item.Name.Trim();
                item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                _cache.Inventory.Add(item);
                current = item;
                _cache.InventoryMovements.Add(new InventoryMovement { Sku = item.Sku, ItemName = item.Name, Delta = item.Quantity, BalanceAfter = item.Quantity, UnitCostEgp = item.UnitCostEgp, Reason = "رصيد افتتاحي / إنشاء صنف" });
                AddEvent("مخزون", item.Sku, $"إضافة صنف للمخزون: {item.Name}.");
            }
            else
            {
                var quantityDelta = item.Quantity - current.Quantity;
                current.Name = item.Name.Trim();
                current.Quantity = item.Quantity;
                current.MinimumQuantity = item.MinimumQuantity;
                current.UnitCostEgp = item.UnitCostEgp;
                current.SalePriceEgp = item.SalePriceEgp;
                current.Category = item.Category.Trim();
                current.Supplier = item.Supplier.Trim();
                current.Location = item.Location.Trim();
                current.UpdatedAtUtc = DateTimeOffset.UtcNow;
                if (quantityDelta != 0)
                    _cache.InventoryMovements.Add(new InventoryMovement
                    {
                        Sku = current.Sku,
                        ItemName = current.Name,
                        Delta = quantityDelta,
                        BalanceAfter = current.Quantity,
                        UnitCostEgp = current.UnitCostEgp,
                        Reason = "تعديل الرصيد من نموذج المخزون"
                    });
                AddEvent("مخزون", current.Sku, $"تحديث بيانات الصنف: {current.Name}.");
            }
            await SaveLockedAsync(cancellationToken);
            return current;
        }
        finally { _gate.Release(); }
    }

    public async Task<InventoryPurchaseReceipt> ReceiveSupplierStockAsync(
        string sku,
        int quantity,
        decimal purchaseUnitCostEgp,
        string supplier,
        string invoiceReference,
        string technicianName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sku) || sku.Length > 80 || sku.Any(char.IsControl))
            throw new ArgumentException("كود الصنف غير صالح.", nameof(sku));
        if (quantity <= 0 || quantity > 100000)
            throw new ArgumentOutOfRangeException(nameof(quantity), "كمية الاستلام يجب أن تكون بين 1 و100000.");
        if (purchaseUnitCostEgp < 0 || purchaseUnitCostEgp > 1_000_000_000m)
            throw new ArgumentOutOfRangeException(nameof(purchaseUnitCostEgp), "تكلفة الشراء خارج الحدود المسموحة.");

        supplier = (supplier ?? "").Trim();
        invoiceReference = (invoiceReference ?? "").Trim();
        technicianName = (technicianName ?? "").Trim();
        if (supplier.Length is < 2 or > 120 || supplier.Any(char.IsControl))
            throw new ArgumentException("اسم المورد مطلوب وبحد أقصى 120 حرفًا.", nameof(supplier));
        if (invoiceReference.Length > 100 || invoiceReference.Any(char.IsControl))
            throw new ArgumentException("مرجع الفاتورة يجب ألا يتجاوز 100 حرف.", nameof(invoiceReference));
        if (technicianName.Length > 120 || technicianName.Any(char.IsControl))
            throw new ArgumentException("اسم المستلم غير صالح.", nameof(technicianName));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var stock = _cache!.Inventory.FirstOrDefault(item =>
                item.Sku.Equals(sku.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("الصنف غير موجود في المخزون. أنشئ الصنف أولًا ثم سجّل استلام الشراء.");

            var previousQuantity = stock.Quantity;
            var previousUnitCost = stock.UnitCostEgp;
            var previousSupplier = stock.Supplier;
            var previousUpdatedAt = stock.UpdatedAtUtc;
            var nextQuantity = checked(previousQuantity + quantity);
            var previousValue = previousQuantity * previousUnitCost;
            var receivedValue = quantity * purchaseUnitCostEgp;
            var weightedCost = nextQuantity == 0
                ? purchaseUnitCostEgp
                : decimal.Round((previousValue + receivedValue) / nextQuantity, 2, MidpointRounding.AwayFromZero);
            var receivedAt = DateTimeOffset.UtcNow;

            var movement = new InventoryMovement
            {
                AtUtc = receivedAt,
                Sku = stock.Sku,
                ItemName = stock.Name,
                Delta = quantity,
                BalanceAfter = nextQuantity,
                UnitCostEgp = purchaseUnitCostEgp,
                Reason = $"استلام شراء من المورد: {supplier}" +
                         (string.IsNullOrWhiteSpace(invoiceReference) ? "" : $" | فاتورة: {invoiceReference}"),
                TechnicianName = technicianName
            };
            var receipt = new InventoryPurchaseReceipt
            {
                ReceivedAtUtc = receivedAt,
                Sku = stock.Sku,
                ItemName = stock.Name,
                Supplier = supplier,
                InvoiceReference = invoiceReference,
                Quantity = quantity,
                PurchaseUnitCostEgp = purchaseUnitCostEgp,
                WeightedAverageUnitCostAfterEgp = weightedCost,
                BalanceAfter = nextQuantity,
                TechnicianName = technicianName,
                InventoryMovementId = movement.Id
            };

            var previousEvents = _cache.Events.ToArray();
            stock.Quantity = nextQuantity;
            stock.UnitCostEgp = weightedCost;
            stock.Supplier = supplier;
            stock.UpdatedAtUtc = receivedAt;
            _cache.InventoryMovements.Add(movement);
            _cache.InventoryPurchaseReceipts.Add(receipt);
            AddEvent("استلام مشتريات", stock.Sku,
                $"استلام {quantity} × {stock.Name} من {supplier}. الرصيد: {nextQuantity}، متوسط التكلفة: {weightedCost:0.00}.");

            try
            {
                await SaveLockedAsync(cancellationToken);
            }
            catch
            {
                stock.Quantity = previousQuantity;
                stock.UnitCostEgp = previousUnitCost;
                stock.Supplier = previousSupplier;
                stock.UpdatedAtUtc = previousUpdatedAt;
                _cache.InventoryMovements.Remove(movement);
                _cache.InventoryPurchaseReceipts.Remove(receipt);
                _cache.Events.Clear();
                _cache.Events.AddRange(previousEvents);
                throw;
            }

            return receipt;
        }
        finally { _gate.Release(); }
    }

    public async Task AdjustStockAsync(
        string sku,
        int delta,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sku) || delta == 0) throw new ArgumentException("حدد كود الصنف وكمية تغيير غير صفرية.");
        reason = (reason ?? "").Trim();
        if (reason.Length is < 2 or > 300 || reason.Any(char.IsControl))
            throw new ArgumentException("سبب حركة المخزون مطلوب وبحد أقصى 300 حرف.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var item = _cache!.Inventory.FirstOrDefault(x => x.Sku.Equals(sku.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("الصنف غير موجود بالمخزون.");
            if (item.Quantity + delta < 0) throw new InvalidOperationException("الكمية المطلوبة أكبر من الرصيد المتاح.");
            item.Quantity += delta;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            _cache.InventoryMovements.Add(new InventoryMovement { Sku = item.Sku, ItemName = item.Name, Delta = delta, BalanceAfter = item.Quantity, UnitCostEgp = item.UnitCostEgp, Reason = reason.Trim() });
            AddEvent("حركة مخزون", item.Sku, $"{(delta > 0 ? "إضافة" : "صرف")} {Math.Abs(delta)} — {reason.Trim()}.");
            await SaveLockedAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<RepairOrderPartUsage> ConsumeInventoryPartForOrderAsync(
        Guid orderId,
        string sku,
        int quantity,
        string technicianName,
        string reason = "استخدام قطعة في أمر الصيانة",
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty) throw new ArgumentException("أمر الصيانة مطلوب.", nameof(orderId));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("كود الصنف مطلوب.", nameof(sku));
        if (quantity <= 0 || quantity > 10000) throw new ArgumentOutOfRangeException(nameof(quantity), "الكمية يجب أن تكون بين 1 و10000.");
        reason = (reason ?? "").Trim();
        if (reason.Length > 300 || reason.Any(char.IsControl)) throw new ArgumentException("سبب الصرف غير صالح.", nameof(reason));
        technicianName = (technicianName ?? "").Trim();
        if (technicianName.Length > 120 || technicianName.Any(char.IsControl)) throw new ArgumentException("اسم الفني غير صالح.", nameof(technicianName));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var order = _cache!.RepairOrders.FirstOrDefault(item => item.Id == orderId)
                ?? throw new KeyNotFoundException("أمر الصيانة غير موجود.");
            if (order.Status is "تم التسليم" or "ملغي")
                throw new InvalidOperationException("لا يمكن صرف قطع لأمر صيانة تم تسليمه أو إلغاؤه.");
            var stock = _cache.Inventory.FirstOrDefault(item => item.Sku.Equals(sku.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("قطعة الغيار غير موجودة في المخزون.");
            if (stock.Quantity < quantity)
                throw new InvalidOperationException($"الرصيد غير كافٍ. المتاح: {stock.Quantity}، المطلوب: {quantity}.");

            var usedAt = DateTimeOffset.UtcNow;
            var usage = new RepairOrderPartUsage
            {
                Sku = stock.Sku,
                PartName = stock.Name,
                Quantity = quantity,
                UnitCostEgp = stock.UnitCostEgp,
                UsedAtUtc = usedAt,
                TechnicianName = technicianName
            };

            // All changes are held under the same lock and committed through one atomic JSON replacement.
            stock.Quantity -= quantity;
            stock.UpdatedAtUtc = usedAt;
            order.PartsUsed ??= [];
            order.PartsUsed.Add(usage);
            order.PartsCostEgp = order.PartsUsed.Sum(part => part.TotalCostEgp);
            order.UpdatedAtUtc = usedAt;
            order.Timeline ??= [];
            order.Timeline.Add(new RepairOrderNote
            {
                AtUtc = usedAt,
                TechnicianName = technicianName,
                Note = $"صرف {quantity} × {stock.Name} ({stock.Sku}) — {reason}."
            });
            _cache.InventoryMovements.Add(new InventoryMovement
            {
                AtUtc = usedAt,
                Sku = stock.Sku,
                ItemName = stock.Name,
                Delta = -quantity,
                BalanceAfter = stock.Quantity,
                UnitCostEgp = stock.UnitCostEgp,
                Reason = reason,
                RepairOrderId = order.Id,
                RepairTicketNumber = order.TicketNumber,
                TechnicianName = technicianName
            });
            AddEvent("صرف قطعة لأمر صيانة", order.TicketNumber,
                $"صرف {quantity} × {stock.Name} ({stock.Sku}). الرصيد المتبقي: {stock.Quantity}.");
            await SaveLockedAsync(cancellationToken);
            return usage;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<InventoryMovement>> GetInventoryMovementsAsync(
        string? sku = null,
        Guid? repairOrderId = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            IEnumerable<InventoryMovement> movements = _cache!.InventoryMovements;
            if (!string.IsNullOrWhiteSpace(sku))
                movements = movements.Where(item => item.Sku.Equals(sku.Trim(), StringComparison.OrdinalIgnoreCase));
            if (repairOrderId.HasValue)
                movements = movements.Where(item => item.RepairOrderId == repairOrderId);
            return movements.OrderByDescending(item => item.AtUtc).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<string> CreateBackupAsync(string? destinationPath = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            await SaveLockedAsync(cancellationToken);
            var path = destinationPath ?? Path.Combine(
                Path.GetDirectoryName(DatabasePath)!,
                $"PhoneLab-Workshop-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.Copy(DatabasePath, path, overwrite: true);
            return path;
        }
        finally { _gate.Release(); }
    }

    public async Task RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("ملف النسخة الاحتياطية غير موجود.", sourcePath);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.OpenRead(sourcePath);
            var restored = await JsonSerializer.DeserializeAsync<WorkshopData>(stream, _json, cancellationToken)
                ?? throw new InvalidDataException("ملف النسخة الاحتياطية فارغ أو غير صالح.");
            MigrateAndValidate(restored);
            _cache = restored;
            await SaveLockedAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task ExportOrderAsync(string ticketNumber, string destinationPath, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var order = snapshot.RepairOrders.FirstOrDefault(x => x.TicketNumber == ticketNumber)
            ?? throw new KeyNotFoundException("أمر الصيانة غير موجود.");
        var currency = WorkshopInternationalRules.NormalizeCurrencyCode(snapshot.Settings.CurrencyCode);
        var text = string.Join(Environment.NewLine,
        [
            "SOFTBOX PHONELAB PROFESSIONAL — REPAIR REPORT",
            $"Ticket: {order.TicketNumber}",
            $"Received UTC: {order.ReceivedAtUtc:O}",
            $"Status: {order.Status}",
            $"Customer: {order.CustomerName}",
            $"Phone: {order.CustomerPhone}",
            $"Device: {order.DeviceModel}",
            $"Serial / IMEI note: {order.SerialOrImeiNote}",
            $"Reported symptoms: {order.IntakeSymptoms}",
            $"Findings before repair: {order.FindingsBeforeRepair}",
            $"Work performed: {order.WorkPerformed}",
            $"Results after repair: {order.ResultsAfterRepair}",
            $"Estimated cost {currency}: {order.EstimatedCostEgp:0.00}",
            $"Final cost {currency}: {order.FinalCostEgp:0.00}",
            $"Parts cost {currency}: {order.PartsCostEgp:0.00}",
            $"Parts used: {string.Join("; ", (order.PartsUsed ?? []).Select(part => $"{part.Sku} × {part.Quantity} {part.PartName} ({part.TotalCostEgp:0.00})"))}",
            $"Warranty months: {order.WarrantyMonths}",
            $"Warranty ends UTC: {order.WarrantyEndsAtUtc:O}",
            "",
            "This report records technician-entered observations. It is not a certification of hardware safety or repair quality."
        ]);
        await File.WriteAllTextAsync(destinationPath, text, cancellationToken);
    }

    public async Task SaveSettingsAsync(WorkshopSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.BusinessName = (settings.BusinessName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(settings.BusinessName)) throw new ArgumentException("Business name is required.");
        settings.CurrencyCode = WorkshopInternationalRules.NormalizeCurrencyCode(settings.CurrencyCode);
        settings.CultureName = WorkshopInternationalRules.NormalizeCultureName(settings.CultureName);
        if (settings.DefaultWarrantyMonths is < 0 or > 120) throw new ArgumentOutOfRangeException(nameof(settings));
        if (string.IsNullOrWhiteSpace(settings.TimeZoneId)) throw new ArgumentException("Time zone ID is required.", nameof(settings));
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new ArgumentException("Time zone ID is not supported on this computer.", nameof(settings), ex); }
        await _gate.WaitAsync(cancellationToken);
        try { await EnsureLoadedLockedAsync(cancellationToken); _cache!.Settings = settings; AddEvent("Workshop settings", settings.BusinessName, "Settings updated."); await SaveLockedAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task AddTimelineNoteAsync(Guid orderId, string technicianName, string note, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(note)) throw new ArgumentException("Timeline note is required.", nameof(note));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var order = _cache!.RepairOrders.FirstOrDefault(x => x.Id == orderId) ?? throw new KeyNotFoundException("Repair order not found.");
            order.Timeline ??= [];
            var entry = new RepairOrderNote { TechnicianName = (technicianName ?? "").Trim(), Note = note.Trim(), AtUtc = DateTimeOffset.UtcNow };
            order.Timeline.Add(entry); order.UpdatedAtUtc = entry.AtUtc;
            AddEvent("Technician note", order.TicketNumber, entry.TechnicianName + ": " + entry.Note);
            await SaveLockedAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }
    private async Task EnsureLoadedLockedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null) return;
        if (!File.Exists(DatabasePath)) { _cache = new WorkshopData(); return; }
        await using (var stream = File.OpenRead(DatabasePath))
        {
            _cache = await JsonSerializer.DeserializeAsync<WorkshopData>(stream, _json, cancellationToken)
                ?? new WorkshopData();
        }

        // Close the legacy database handle before replacing the file during schema migration (required on Windows).
        var schemaNeedsUpgrade = _cache.SchemaVersion != 5;
        MigrateAndValidate(_cache);
        if (schemaNeedsUpgrade) await SaveLockedAsync(cancellationToken);
    }

    private async Task SaveLockedAsync(CancellationToken cancellationToken)
    {
        _cache!.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var temp = DatabasePath + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, _cache, _json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temp, DatabasePath, overwrite: true);
    }

    private void AddEvent(string category, string reference, string description)
    {
        _cache!.Events.Add(new WorkshopEvent { Category = category, Reference = reference, Description = description });
        if (_cache.Events.Count > 5000) _cache.Events.RemoveRange(0, _cache.Events.Count - 5000);
    }

    private static WorkshopData Clone(WorkshopData data)
        => JsonSerializer.Deserialize<WorkshopData>(JsonSerializer.Serialize(data)) ?? new WorkshopData();

    private static void MigrateAndValidate(WorkshopData data)
    {
        if (data.SchemaVersion is < 1 or > 5) throw new InvalidDataException($"Unsupported workshop database schema: {data.SchemaVersion}.");
        data.Settings ??= new WorkshopSettings();
        data.Settings.CurrencyCode = WorkshopInternationalRules.NormalizeCurrencyCode(data.Settings.CurrencyCode);
        data.Settings.CultureName = WorkshopInternationalRules.NormalizeCultureName(data.Settings.CultureName);
        data.Customers ??= []; data.RepairOrders ??= []; data.Inventory ??= []; data.Events ??= []; data.InventoryMovements ??= []; data.InventoryPurchaseReceipts ??= [];
        foreach (var order in data.RepairOrders)
        {
            order.Timeline ??= [];
            order.PartsUsed ??= [];
            order.QuoteDecisionHistory ??= [];
            order.Payments ??= [];
            if (order.QuoteDecisionHistory.Count > 10000 || order.Payments.Count > 100000)
                throw new InvalidDataException("Repair order commercial history exceeds safe size limits.");
            if (order.QuoteDecisionHistory.Any(decision => !Enum.IsDefined(decision.Decision) || decision.QuotedAmountEgp < 0 || decision.QuotedAmountEgp > 100_000_000m))
                throw new InvalidDataException("Repair order contains an invalid quote decision.");
            if (order.Payments.Any(payment => payment.AmountEgp <= 0 || payment.AmountEgp > 100_000_000m || decimal.Round(payment.AmountEgp, 2) != payment.AmountEgp))
                throw new InvalidDataException("Repair order contains an invalid recorded payment amount.");
            order.PartsCostEgp = order.PartsUsed.Sum(part => part.TotalCostEgp);
            if (order.WarrantyMonths is < 0 or > 120) order.WarrantyMonths = data.Settings.DefaultWarrantyMonths;
            if (order.DeliveredAtUtc is not null && order.WarrantyEndsAtUtc is null)
                order.WarrantyEndsAtUtc = WorkshopInternationalRules.CalculateWarrantyEnd(order.DeliveredAtUtc, order.WarrantyMonths);
        }
        if (data.SchemaVersion < 5) data.SchemaVersion = 5;
        if (data.Customers.Count > 100000 || data.RepairOrders.Count > 100000 || data.Inventory.Count > 100000 || data.InventoryMovements.Count > 500000 || data.InventoryPurchaseReceipts.Count > 500000)
            throw new InvalidDataException("Workshop database exceeds safe size limits.");
    }
}
