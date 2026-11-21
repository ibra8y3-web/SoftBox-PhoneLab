using System.Security.Cryptography;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Security;

namespace SoftBox.PhoneLab.Core.Help;

public sealed record RepairKnowledgeEntry(
    Guid Id,
    string Manufacturer,
    string Model,
    string Symptom,
    string Diagnosis,
    string Evidence,
    IReadOnlyList<string> SafeSteps,
    string Source,
    string Author,
    bool TechnicianMarkedVerified,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Local, technician-authored repair knowledge. The store contains no customer identity or device serial.
/// The database is encrypted with Windows DPAPI for the current Windows user.
/// </summary>
public sealed class RepairKnowledgeStore
{
    private const int SchemaVersion = 1;
    private const int MaximumEntries = 20_000;
    private const int MaximumFieldLength = 8_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _databasePath;
    private KnowledgeData? _data;

    public string DatabasePath => _databasePath;

    public RepairKnowledgeStore(string? rootDirectory = null)
    {
        var root = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "RepairKnowledge"));
        Directory.CreateDirectory(root);
        _databasePath = Path.Combine(root, "knowledge.sbxknowledge");
    }

    public async Task<IReadOnlyList<RepairKnowledgeEntry>> SearchAsync(
        string? query = null,
        string? manufacturer = null,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            IEnumerable<RepairKnowledgeEntry> entries = _data!.Entries;
            if (!string.IsNullOrWhiteSpace(manufacturer))
                entries = entries.Where(item => Contains(item.Manufacturer, manufacturer));
            if (!string.IsNullOrWhiteSpace(model))
                entries = entries.Where(item => Contains(item.Model, model));
            if (!string.IsNullOrWhiteSpace(query))
                entries = entries.Where(item =>
                    Contains(item.Manufacturer, query) ||
                    Contains(item.Model, query) ||
                    Contains(item.Symptom, query) ||
                    Contains(item.Diagnosis, query) ||
                    Contains(item.Evidence, query) ||
                    item.SafeSteps.Any(step => Contains(step, query)) ||
                    Contains(item.Source, query) ||
                    Contains(item.Author, query));

            return entries.OrderByDescending(item => item.UpdatedAtUtc).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<RepairKnowledgeEntry> AddAsync(
        string manufacturer,
        string model,
        string symptom,
        string diagnosis,
        string evidence,
        IEnumerable<string> safeSteps,
        string source,
        string author,
        bool technicianMarkedVerified,
        CancellationToken cancellationToken = default)
    {
        var normalizedSteps = (safeSteps ?? throw new ArgumentNullException(nameof(safeSteps)))
            .Select(step => NormalizeRequired(step, 1000, "خطوة الفحص"))
            .Distinct(StringComparer.Ordinal)
            .Take(30)
            .ToArray();
        if (normalizedSteps.Length == 0)
            throw new ArgumentException("أدخل خطوة فحص آمنة واحدة على الأقل.", nameof(safeSteps));

        var now = DateTimeOffset.UtcNow;
        var entry = new RepairKnowledgeEntry(
            Guid.NewGuid(),
            NormalizeRequired(manufacturer, 120, "الشركة"),
            NormalizeRequired(model, 160, "الموديل"),
            NormalizeRequired(symptom, MaximumFieldLength, "العَرَض"),
            NormalizeRequired(diagnosis, MaximumFieldLength, "التشخيص"),
            NormalizeOptional(evidence, MaximumFieldLength, "الدليل"),
            normalizedSteps,
            NormalizeOptional(source, 500, "المصدر"),
            NormalizeRequired(author, 120, "الفني"),
            technicianMarkedVerified,
            now,
            now);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            if (_data!.Entries.Count >= MaximumEntries)
                throw new InvalidOperationException("وصلت قاعدة المعرفة المحلية إلى الحد الأقصى.");
            _data.Entries.Add(entry);
            await SaveLockedAsync(cancellationToken);
            return entry;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var index = _data!.Entries.FindIndex(item => item.Id == id);
            if (index < 0)
                throw new KeyNotFoundException("ملاحظة الإصلاح غير موجودة.");
            _data.Entries.RemoveAt(index);
            await SaveLockedAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        => (await SearchAsync(cancellationToken: cancellationToken)).Count;

    private async Task EnsureLoadedLockedAsync(CancellationToken cancellationToken)
    {
        if (_data is not null)
            return;
        if (!File.Exists(_databasePath))
        {
            _data = new KnowledgeData();
            return;
        }

        var info = new FileInfo(_databasePath);
        if (info.Length is <= 0 or > 128 * 1024 * 1024)
            throw new InvalidDataException("ملف قاعدة المعرفة فارغ أو يتجاوز الحد الآمن.");

        var stored = await File.ReadAllBytesAsync(_databasePath, cancellationToken);
        var json = WindowsUserDataProtection.UnprotectOrLegacy(stored, "RepairKnowledgeStore.v1", out var wasProtected);
        CryptographicOperations.ZeroMemory(stored);
        KnowledgeData restored;
        try
        {
            restored = JsonSerializer.Deserialize<KnowledgeData>(json, JsonOptions)
                ?? throw new InvalidDataException("ملف قاعدة المعرفة المحلي غير صالح.");
        }
        finally { CryptographicOperations.ZeroMemory(json); }

        if (restored.SchemaVersion != SchemaVersion || restored.Entries is null ||
            restored.Entries.Count > MaximumEntries ||
            restored.Entries.Any(item => item is null || item.SafeSteps is null || item.SafeSteps.Count is < 1 or > 30))
            throw new InvalidDataException("مخطط قاعدة المعرفة أو عدد عناصرها غير مدعوم.");

        _data = restored;
        if (!wasProtected)
        {
            try { await SaveLockedAsync(cancellationToken); }
            catch { _data = null; throw; }
        }
    }

    private async Task SaveLockedAsync(CancellationToken cancellationToken)
    {
        var temporary = _databasePath + ".tmp";
        byte[]? json = null;
        byte[]? protectedBytes = null;
        try
        {
            json = JsonSerializer.SerializeToUtf8Bytes(_data, JsonOptions);
            protectedBytes = WindowsUserDataProtection.Protect(json, "RepairKnowledgeStore.v1");
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
            File.Move(temporary, _databasePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
        finally
        {
            if (json is not null) CryptographicOperations.ZeroMemory(json);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static bool Contains(string? value, string query)
        => (value ?? "").Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRequired(string? value, int maximum, string field)
    {
        var result = NormalizeOptional(value, maximum, field);
        if (result.Length == 0)
            throw new ArgumentException($"حقل {field} مطلوب.");
        return result;
    }

    private static string NormalizeOptional(string? value, int maximum, string field)
    {
        var result = (value ?? "").Trim();
        if (result.Length > maximum || result.Any(char.IsControl))
            throw new ArgumentException($"حقل {field} طويل جدًا أو يحتوي على محارف غير مسموحة.");
        return result;
    }

    private sealed class KnowledgeData
    {
        public int SchemaVersion { get; set; } = RepairKnowledgeStore.SchemaVersion;
        public List<RepairKnowledgeEntry> Entries { get; set; } = [];
    }
}
