using System.IO;
using System.Text.Json;

namespace SoftBox.PhoneLab.Core.Settings;

public sealed record PhoneLabSettings(
    int SchemaVersion = 1,
    bool AutoDiscoverDevices = true,
    int DiscoveryIntervalSeconds = 3,
    bool AutoOpenScreenMirror = true,
    bool AutoInstallAgent = true,
    bool AutoInspectOnConnect = true,
    int MaximumBatchConcurrency = 3);

/// <summary>
/// Local user preferences for connection automation and batch limits.
/// Does not store credentials, device serials or secrets.
/// </summary>
public sealed class PhoneLabSettingsService
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _settingsPath;

    public string SettingsPath => _settingsPath;

    public PhoneLabSettingsService(string? settingsPath = null)
    {
        _settingsPath = Path.GetFullPath(settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "settings.json"));
    }

    public async Task<PhoneLabSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_settingsPath))
                return new PhoneLabSettings();

            var info = new FileInfo(_settingsPath);
            if (info.Length > 64 * 1024)
                throw new InvalidDataException("ملف إعدادات SoftBox أكبر من الحد الآمن.");

            await using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                8 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer.DeserializeAsync<PhoneLabSettings>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("ملف إعدادات SoftBox غير صالح.");
            return Validate(settings);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(PhoneLabSettings settings, CancellationToken cancellationToken = default)
    {
        var validated = Validate(settings);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 8 * 1024, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(stream, validated, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                File.Move(temporary, _settingsPath, overwrite: true);
            }
            catch
            {
                try { File.Delete(temporary); } catch { }
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public static PhoneLabSettings Validate(PhoneLabSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"إصدار إعدادات SoftBox غير مدعوم: {settings.SchemaVersion}.");
        if (settings.DiscoveryIntervalSeconds is < 3 or > 120)
            throw new ArgumentOutOfRangeException(nameof(settings.DiscoveryIntervalSeconds), "فترة اكتشاف الأجهزة يجب أن تكون من 3 إلى 120 ثانية.");
        if (settings.MaximumBatchConcurrency is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(settings.MaximumBatchConcurrency), "حد التوازي يجب أن يكون بين 1 و8.");
        return settings;
    }
}
