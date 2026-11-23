using System.IO;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed class ComponentSource
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    public string License { get; init; } = "";
}

public sealed class DeviceComponentRecord
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string Details { get; init; } = "";
    /// <summary>published | single-source | teardown | unknown</summary>
    public string Confidence { get; init; } = "unknown";
    public string[] Sources { get; init; } = [];

    public string ConfidenceLabel => Confidence.ToLowerInvariant() switch
    {
        "published" => "منشور في عدة مصادر",
        "single-source" => "منشور في مصدر واحد",
        "teardown" => "من تفكيك/مخطط موثّق",
        _ => "غير موثّق"
    };
}

public sealed class DeviceComponentProfile
{
    public int SchemaVersion { get; init; } = 1;
    public string ProfileId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string[] ModelNumbers { get; init; } = [];
    public string[] DeviceCodes { get; init; } = [];
    public DateTimeOffset UpdatedUtc { get; init; }
    public string Notes { get; init; } = "";
    public List<ComponentSource> Sources { get; init; } = [];
    public List<DeviceComponentRecord> Components { get; init; } = [];

    public DeviceComponentRecord? Find(string id) =>
        Components.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<ComponentSource> SourcesFor(DeviceComponentRecord record) =>
        Sources.Where(s => record.Sources.Contains(s.Id, StringComparer.OrdinalIgnoreCase));

    public int DocumentedCount => Components.Count(c => !c.Confidence.Equals("unknown", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Per-model component knowledge (SoC, memory, battery, display ...) with sources and confidence.
/// Bundled files live in Data\device-components; users can add or override files in
/// %LOCALAPPDATA%\SoftBox\PhoneLab\device-components. Nothing is ever guessed.
/// </summary>
public sealed class DeviceComponentCatalog
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, MaxDepth = 16 };
    private readonly Dictionary<string, DeviceComponentProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public DeviceComponentCatalog()
    {
        LoadDirectory(Path.Combine(AppContext.BaseDirectory, "Data", "device-components"));
        LoadDirectory(UserDirectory);
    }

    public static string UserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoftBox", "PhoneLab", "device-components");

    public int Count => _profiles.Count;

    private void LoadDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length > 2_000_000) continue;
                    var profile = JsonSerializer.Deserialize<DeviceComponentProfile>(File.ReadAllText(file), Json);
                    if (profile is null || profile.SchemaVersion != 1 || string.IsNullOrWhiteSpace(profile.ProfileId)) continue;
                    _profiles[profile.ProfileId] = profile; // later directory (user) overrides bundled
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public DeviceComponentProfile? Find(DeviceProfile? profile, DeviceSnapshot? snapshot)
    {
        if (profile is not null && _profiles.TryGetValue(profile.Id, out var byId)) return byId;

        var models = new List<string>();
        if (profile is not null) models.AddRange(profile.ModelNumbers);
        if (snapshot is not null) models.Add(snapshot.ModelNumber);
        var normalizedModels = models.Select(NormalizeModel).Where(m => m.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var codes = new List<string>();
        if (profile is not null) codes.AddRange(profile.DeviceCodes);
        if (snapshot is not null) codes.Add(snapshot.DeviceCode);

        foreach (var candidate in _profiles.Values)
        {
            if (candidate.ModelNumbers.Any(m => normalizedModels.Contains(NormalizeModel(m)))) return candidate;
        }
        foreach (var candidate in _profiles.Values)
        {
            if (candidate.DeviceCodes.Any(c => codes.Contains(c, StringComparer.OrdinalIgnoreCase))) return candidate;
        }
        return null;
    }

    /// <summary>"SM-A315F/DS" and "sm-a315f" both become "SM-A315F".</summary>
    private static string NormalizeModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Contains("غير متاح", StringComparison.Ordinal)) return "";
        var value = model.Trim().ToUpperInvariant();
        var slash = value.IndexOf('/');
        return slash > 0 ? value[..slash] : value;
    }
}
