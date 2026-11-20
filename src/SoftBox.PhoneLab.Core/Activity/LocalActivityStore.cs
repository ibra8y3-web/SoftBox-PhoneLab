using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SoftBox.PhoneLab.Core.Activity;

public sealed record DeviceHistoryEntry(
    Guid Id,
    string DeviceFingerprint,
    string MaskedSerial,
    string Manufacturer,
    string Model,
    string EventType,
    string Summary,
    string Evidence,
    DateTimeOffset AtUtc);

public sealed record SoftBoxNotification(
    Guid Id,
    DateTimeOffset AtUtc,
    string Category,
    string Title,
    string Message,
    string ModuleId,
    string? DeviceFingerprint,
    bool IsRead);

public sealed record LocalActivitySnapshot(
    IReadOnlyList<DeviceHistoryEntry> DeviceHistory,
    IReadOnlyList<SoftBoxNotification> Notifications);

/// <summary>
/// Offline-first local device history and notification inbox.
/// Stores a one-way SHA-256 device fingerprint and a masked serial instead of the raw ADB serial.
/// The JSON store is not encrypted; protect the Windows user profile and review exported data.
/// </summary>
public sealed class LocalActivityStore
{
    private const int SchemaVersion = 1;
    private const int MaximumDeviceHistory = 20_000;
    private const int MaximumNotifications = 5_000;
    private const int MaximumTextLength = 4_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _databasePath;
    private ActivityData? _data;

    public string DatabasePath => _databasePath;

    public LocalActivityStore(string? rootDirectory = null)
    {
        var root = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "Activity"));
        Directory.CreateDirectory(root);
        _databasePath = Path.Combine(root, "activity.json");
    }

    public static string CreateDeviceFingerprint(string serial)
    {
        if (!IsValidSerial(serial))
            throw new ArgumentException("Invalid device serial.", nameof(serial));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serial)))
            .ToLowerInvariant();
    }

    public static string MaskSerial(string serial)
    {
        if (!IsValidSerial(serial))
            throw new ArgumentException("Invalid device serial.", nameof(serial));
        return serial.Length > 4 ? "••••" + serial[^4..] : "••••";
    }

    public async Task<DeviceHistoryEntry> AddDeviceHistoryAsync(
        string serial,
        string manufacturer,
        string model,
        string eventType,
        string summary,
        string evidence = "",
        CancellationToken cancellationToken = default)
    {
        var entry = new DeviceHistoryEntry(
            Guid.NewGuid(),
            CreateDeviceFingerprint(serial),
            MaskSerial(serial),
            Normalize(manufacturer, 120),
            Normalize(model, 160),
            NormalizeRequired(eventType, 120),
            NormalizeRequired(summary, MaximumTextLength),
            Normalize(evidence, MaximumTextLength),
            DateTimeOffset.UtcNow);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            _data!.DeviceHistory.Add(entry);
            if (_data.DeviceHistory.Count > MaximumDeviceHistory)
                _data.DeviceHistory.RemoveRange(0, _data.DeviceHistory.Count - MaximumDeviceHistory);
            await SaveLockedAsync(cancellationToken);
            return entry;
        }
        finally { _gate.Release(); }
    }

    public async Task<SoftBoxNotification> AddNotificationAsync(
        string category,
        string title,
        string message,
        string moduleId,
        string? serial = null,
        CancellationToken cancellationToken = default)
    {
        var notification = new SoftBoxNotification(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            NormalizeRequired(category, 80),
            NormalizeRequired(title, 160),
            NormalizeRequired(message, MaximumTextLength),
            NormalizeRequired(moduleId, 80),
            serial is null ? null : CreateDeviceFingerprint(serial),
            false);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            _data!.Notifications.Add(notification);
            if (_data.Notifications.Count > MaximumNotifications)
                _data.Notifications.RemoveRange(0, _data.Notifications.Count - MaximumNotifications);
            await SaveLockedAsync(cancellationToken);
            return notification;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<DeviceHistoryEntry>> GetDeviceHistoryAsync(
        string? serial = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            IEnumerable<DeviceHistoryEntry> query = _data!.DeviceHistory;
            if (!string.IsNullOrWhiteSpace(serial))
            {
                var fingerprint = CreateDeviceFingerprint(serial);
                query = query.Where(item => item.DeviceFingerprint == fingerprint);
            }
            return query.OrderByDescending(item => item.AtUtc).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<SoftBoxNotification>> GetNotificationsAsync(
        bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            IEnumerable<SoftBoxNotification> query = _data!.Notifications;
            if (unreadOnly)
                query = query.Where(item => !item.IsRead);
            return query.OrderByDescending(item => item.AtUtc).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task MarkNotificationReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var index = _data!.Notifications.FindIndex(item => item.Id == id);
            if (index < 0)
                throw new KeyNotFoundException("الإشعار غير موجود.");
            _data.Notifications[index] = _data.Notifications[index] with { IsRead = true };
            await SaveLockedAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task MarkAllNotificationsReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            for (var i = 0; i < _data!.Notifications.Count; i++)
                _data.Notifications[i] = _data.Notifications[i] with { IsRead = true };
            await SaveLockedAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<LocalActivitySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            return new LocalActivitySnapshot(
                _data!.DeviceHistory.OrderByDescending(item => item.AtUtc).ToArray(),
                _data.Notifications.OrderByDescending(item => item.AtUtc).ToArray());
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureLoadedLockedAsync(CancellationToken cancellationToken)
    {
        if (_data is not null)
            return;
        if (!File.Exists(_databasePath))
        {
            _data = new ActivityData();
            return;
        }

        var info = new FileInfo(_databasePath);
        if (info.Length > 128 * 1024 * 1024)
            throw new InvalidDataException("ملف سجل النشاط أكبر من الحد الآمن.");

        await using var stream = new FileStream(_databasePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        _data = await JsonSerializer.DeserializeAsync<ActivityData>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("ملف سجل النشاط غير صالح.");
        if (_data.SchemaVersion != SchemaVersion ||
            _data.DeviceHistory is null || _data.Notifications is null ||
            _data.DeviceHistory.Count > MaximumDeviceHistory ||
            _data.Notifications.Count > MaximumNotifications)
            throw new InvalidDataException("مخطط أو حجم سجل النشاط غير مدعوم.");
    }

    private async Task SaveLockedAsync(CancellationToken cancellationToken)
    {
        var temporary = _databasePath + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                FileShare.None, 32 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, _data, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, _databasePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }

    private static string NormalizeRequired(string? value, int maximum)
    {
        var result = Normalize(value, maximum);
        if (string.IsNullOrWhiteSpace(result))
            throw new ArgumentException("هذا الحقل مطلوب.");
        return result;
    }

    private static string Normalize(string? value, int maximum)
    {
        var result = (value ?? "").Trim();
        if (result.Length > maximum || result.Any(char.IsControl))
            throw new ArgumentException("إحدى قيم السجل طويلة جدًا أو غير صالحة.");
        return result;
    }

    private static bool IsValidSerial(string? serial)
        => !string.IsNullOrWhiteSpace(serial) &&
           serial.Length <= 128 &&
           serial.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

    private sealed class ActivityData
    {
        public int SchemaVersion { get; set; } = LocalActivityStore.SchemaVersion;
        public List<DeviceHistoryEntry> DeviceHistory { get; set; } = [];
        public List<SoftBoxNotification> Notifications { get; set; } = [];
    }
}
