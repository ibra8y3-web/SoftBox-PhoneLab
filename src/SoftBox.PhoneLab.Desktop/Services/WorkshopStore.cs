using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            current.UpdatedAtUtc = DateTimeOffset.UtcNow;
            if (current.Status == "تم التسليم" && current.DeliveredAtUtc is null)
                current.DeliveredAtUtc = DateTimeOffset.UtcNow;
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
        if (item.Quantity < 0 || item.MinimumQuantity < 0 || item.UnitCostEgp < 0)
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
                AddEvent("مخزون", item.Sku, $"إضافة صنف للمخزون: {item.Name}.");
            }
            else
            {
                current.Name = item.Name.Trim();
                current.Quantity = item.Quantity;
                current.MinimumQuantity = item.MinimumQuantity;
                current.UnitCostEgp = item.UnitCostEgp;
                current.Location = item.Location.Trim();
                current.UpdatedAtUtc = DateTimeOffset.UtcNow;
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
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var item = _cache!.Inventory.FirstOrDefault(x => x.Sku.Equals(sku.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("الصنف غير موجود بالمخزون.");
            if (item.Quantity + delta < 0) throw new InvalidOperationException("الكمية المطلوبة أكبر من الرصيد المتاح.");
            item.Quantity += delta;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddEvent("حركة مخزون", item.Sku, $"{(delta > 0 ? "إضافة" : "صرف")} {Math.Abs(delta)} — {reason.Trim()}.");
            await SaveLockedAsync(cancellationToken);
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
            Validate(restored);
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
            $"Estimated cost EGP: {order.EstimatedCostEgp:0.00}",
            $"Final cost EGP: {order.FinalCostEgp:0.00}",
            "",
            "This report records technician-entered observations. It is not a certification of hardware safety or repair quality."
        ]);
        await File.WriteAllTextAsync(destinationPath, text, cancellationToken);
    }

    private async Task EnsureLoadedLockedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null) return;
        if (!File.Exists(DatabasePath)) { _cache = new WorkshopData(); return; }
        await using var stream = File.OpenRead(DatabasePath);
        _cache = await JsonSerializer.DeserializeAsync<WorkshopData>(stream, _json, cancellationToken)
            ?? new WorkshopData();
        Validate(_cache);
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

    private static void Validate(WorkshopData data)
    {
        if (data.SchemaVersion != 1) throw new InvalidDataException($"إصدار قاعدة البيانات غير مدعوم: {data.SchemaVersion}.");
        data.Customers ??= [];
        data.RepairOrders ??= [];
        data.Inventory ??= [];
        data.Events ??= [];
        if (data.Customers.Count > 100000 || data.RepairOrders.Count > 100000 || data.Inventory.Count > 100000)
            throw new InvalidDataException("ملف البيانات يتجاوز الحدود الآمنة.");
    }
}
