using System.Text.Json;
using SoftBox.PhoneLab.Core.Compatibility;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record DiagnosticEngineSettingsLoadResult(
    DiagnosticEngineSettings Settings,
    IReadOnlyList<string> Warnings);

/// <summary>Stores non-secret diagnostic policy settings in the current Windows user's local app data.</summary>
public sealed class DeviceDiagnosticEngineSettingsStore
{
    private const long MaximumSettingsFileBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;

    public DeviceDiagnosticEngineSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "diagnostic-engine-settings.json");
    }

    public async Task<DiagnosticEngineSettingsLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return new(new DiagnosticEngineSettings(), []);

        try
        {
            var info = new FileInfo(_path);
            if (info.Length is <= 0 or > MaximumSettingsFileBytes)
                throw new InvalidDataException("Diagnostic settings file size is invalid.");

            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            var settings = JsonSerializer.Deserialize<DiagnosticEngineSettings>(json, JsonOptions)
                ?? throw new InvalidDataException("Diagnostic settings file is empty.");
            var errors = settings.Validate();
            if (errors.Count > 0)
                throw new InvalidDataException(string.Join(" ", errors));

            return new(settings, []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return new(
                new DiagnosticEngineSettings(),
                ["تعذر تحميل إعدادات محرك التشخيص؛ تم استخدام الإعدادات الافتراضية الآمنة. السبب: " + ex.Message]);
        }
    }

    public async Task SaveAsync(
        DiagnosticEngineSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = settings.Validate();
        if (errors.Count > 0)
            throw new ArgumentOutOfRangeException(nameof(settings), string.Join(" ", errors));

        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))
            ?? throw new InvalidOperationException("Diagnostic settings directory is invalid.");
        Directory.CreateDirectory(directory);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(temporary, json, cancellationToken);
            var info = new FileInfo(temporary);
            if (info.Length > MaximumSettingsFileBytes)
                throw new InvalidDataException("Diagnostic settings file exceeds its size limit.");
            File.Move(temporary, _path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }
}
