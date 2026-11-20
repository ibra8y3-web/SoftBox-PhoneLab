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
            current.Status = order.Status;
            current.FindingsBeforeRepair = order.FindingsBeforeRepair;
            current.WorkPerformed = order.WorkPerformed;
            current.ResultsAfterRepair = order.ResultsAfterRepair;
            current.EstimatedCostEgp = Math.Max(0, order.EstimatedCostEgp);
            current.FinalCostEgp = Math.Max(0, order.FinalCostEgp);
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

    public async Task AdjustStockAsync(string sku, int delta, string reason, CancellationToken cancellationToken = default)
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
        var schemaNeedsUpgrade = _cache.SchemaVersion != 3;
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
        if (data.SchemaVersion is < 1 or > 3) throw new InvalidDataException($"Unsupported workshop database schema: {data.SchemaVersion}.");
        data.Settings ??= new WorkshopSettings();
        data.Settings.CurrencyCode = WorkshopInternationalRules.NormalizeCurrencyCode(data.Settings.CurrencyCode);
        data.Settings.CultureName = WorkshopInternationalRules.NormalizeCultureName(data.Settings.CultureName);
        data.Customers ??= []; data.RepairOrders ??= []; data.Inventory ??= []; data.Events ??= []; data.InventoryMovements ??= [];
        foreach (var order in data.RepairOrders)
        {
            order.Timeline ??= [];
            order.PartsUsed ??= [];
            order.PartsCostEgp = order.PartsUsed.Sum(part => part.TotalCostEgp);
            if (order.WarrantyMonths is < 0 or > 120) order.WarrantyMonths = data.Settings.DefaultWarrantyMonths;
            if (order.DeliveredAtUtc is not null && order.WarrantyEndsAtUtc is null)
                order.WarrantyEndsAtUtc = WorkshopInternationalRules.CalculateWarrantyEnd(order.DeliveredAtUtc, order.WarrantyMonths);
        }
        if (data.SchemaVersion < 3) data.SchemaVersion = 3;
        if (data.Customers.Count > 100000 || data.RepairOrders.Count > 100000 || data.Inventory.Count > 100000 || data.InventoryMovements.Count > 500000)
            throw new InvalidDataException("Workshop database exceeds safe size limits.");
    }
}
