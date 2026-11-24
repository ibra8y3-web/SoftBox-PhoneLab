using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Core.Security;

namespace SoftBox.PhoneLab.Core.Activity;

public sealed record DiagnosticEvidenceEntry(
    Guid Id,
    string DeviceFingerprint,
    string MaskedSerial,
    string Manufacturer,
    string Model,
    string ComponentId,
    string ComponentName,
    string Status,
    string Evidence,
    string Source,
    DateTimeOffset AtUtc);

/// <summary>
/// Retains real component diagnostic snapshots locally under Windows DPAPI CurrentUser protection.
/// Raw device serials and secrets are never persisted; repeated unchanged snapshots are deduplicated.
/// </summary>
public sealed class DiagnosticEvidenceStore
{
    private const int SchemaVersion = 1;
    private const int MaximumEntries = 30_000;
    private const int MaximumTextLength = 6_000;
    private const string Purpose = "DiagnosticEvidenceStore.v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _databasePath;
    private EvidenceData? _data;

    public string DatabasePath => _databasePath;

    public DiagnosticEvidenceStore(string? rootDirectory = null)
    {
        var root = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "Diagnostics"));
        Directory.CreateDirectory(root);
        _databasePath = Path.Combine(root, "diagnostic-evidence.json");
    }

    public async Task<int> RecordSnapshotAsync(
        string serial,
        string manufacturer,
        string model,
        IReadOnlyList<ComponentDiagnostic> components,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(components);
        var fingerprint = LocalActivityStore.CreateDeviceFingerprint(serial);
        var maskedSerial = LocalActivityStore.MaskSerial(serial);
        var normalizedManufacturer = Normalize(manufacturer, 120);
        var normalizedModel = Normalize(model, 160);
        var normalizedSource = NormalizeRequired(source, 160);

        var snapshot = components
            .Where(component => !string.IsNullOrWhiteSpace(component.Id))
            .Select(component => new
            {
                ComponentId = NormalizeRequired(component.Id, 120),
                ComponentName = NormalizeRequired(component.Name, 180),
                Status = component.Status.ToString(),
                Evidence = Normalize(component.Evidence, MaximumTextLength)
            })
            .ToArray();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var added = 0;

            foreach (var component in snapshot)
            {
                var previous = _data!.Entries.LastOrDefault(entry =>
                    entry.DeviceFingerprint == fingerprint &&
                    entry.ComponentId.Equals(component.ComponentId, StringComparison.OrdinalIgnoreCase));

                if (previous is not null &&
                    previous.Status == component.Status &&
                    previous.Evidence == component.Evidence &&
                    previous.Source == normalizedSource &&
                    previous.Manufacturer == normalizedManufacturer &&
                    previous.Model == normalizedModel)
                    continue;

                _data!.Entries.Add(new DiagnosticEvidenceEntry(
                    Guid.NewGuid(),
                    fingerprint,
                    maskedSerial,
                    normalizedManufacturer,
                    normalizedModel,
                    component.ComponentId,
                    component.ComponentName,
                    component.Status,
                    component.Evidence,
                    normalizedSource,
                    now));
                added++;
            }

            if (added > 0)
            {
                if (_data!.Entries.Count > MaximumEntries)
                    _data.Entries.RemoveRange(0, _data.Entries.Count - MaximumEntries);
                await SaveLockedAsync(cancellationToken);
            }

            return added;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<DiagnosticEvidenceEntry>> GetRecentAsync(
        string? serial = null,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            IEnumerable<DiagnosticEvidenceEntry> query = _data!.Entries;
            if (!string.IsNullOrWhiteSpace(serial))
            {
                var fingerprint = LocalActivityStore.CreateDeviceFingerprint(serial);
                query = query.Where(entry => entry.DeviceFingerprint == fingerprint);
            }
            return query.OrderByDescending(entry => entry.AtUtc).Take(limit).ToArray();
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureLoadedLockedAsync(CancellationToken cancellationToken)
    {
        if (_data is not null) return;
        if (!File.Exists(_databasePath))
        {
            _data = new EvidenceData();
            return;
        }

        var info = new FileInfo(_databasePath);
        if (info.Length is <= 0 or > 128 * 1024 * 1024)
            throw new InvalidDataException("ملف أدلة التشخيص فارغ أو أكبر من الحد الآمن.");

        var storedBytes = await File.ReadAllBytesAsync(_databasePath, cancellationToken);
        var jsonBytes = WindowsUserDataProtection.UnprotectOrLegacy(storedBytes, Purpose, out var wasProtected);
        CryptographicOperations.ZeroMemory(storedBytes);
        EvidenceData restored;
        try
        {
            restored = JsonSerializer.Deserialize<EvidenceData>(jsonBytes, JsonOptions)
                ?? throw new InvalidDataException("قاعدة أدلة التشخيص غير صالحة.");
        }
        finally { CryptographicOperations.ZeroMemory(jsonBytes); }

        if (restored.SchemaVersion != SchemaVersion ||
            restored.Entries is null ||
            restored.Entries.Count > MaximumEntries)
            throw new InvalidDataException("مخطط قاعدة أدلة التشخيص غير مدعوم.");

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
        byte[]? jsonBytes = null;
        byte[]? protectedBytes = null;
        try
        {
            jsonBytes = JsonSerializer.SerializeToUtf8Bytes(_data, JsonOptions);
            protectedBytes = WindowsUserDataProtection.Protect(jsonBytes, Purpose);
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
            if (jsonBytes is not null) CryptographicOperations.ZeroMemory(jsonBytes);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static string NormalizeRequired(string? value, int maximum)
    {
        var result = Normalize(value, maximum);
        if (string.IsNullOrWhiteSpace(result))
            throw new ArgumentException("Diagnostic evidence field is required.");
        return result;
    }

    private static string Normalize(string? value, int maximum)
    {
        var result = (value ?? string.Empty).Trim();
        if (result.Length > maximum || result.Any(char.IsControl))
            throw new ArgumentException("Diagnostic evidence field is too long or contains control characters.");
        return result;
    }

    private sealed class EvidenceData
    {
        public int SchemaVersion { get; set; } = DiagnosticEvidenceStore.SchemaVersion;
        public List<DiagnosticEvidenceEntry> Entries { get; set; } = [];
    }
}
