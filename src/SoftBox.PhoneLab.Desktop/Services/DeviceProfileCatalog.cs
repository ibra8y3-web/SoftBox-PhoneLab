using System.IO;
using System.Net.Http;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Compatibility;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Loads a curated identity layer plus a provenance-checked community snapshot.
/// Community identities remain unverified and can never supply PCB geometry.
/// </summary>
public sealed class DeviceProfileCatalog
{
    private const string SoftBoxCatalogUrl = "https://raw.githubusercontent.com/ibra8y3-web/SoftBox-PhoneLab/main/data/device-profiles/catalog.json";
    private const string CommunityProfileCatalogUrl = "https://raw.githubusercontent.com/ibra8y3-web/SoftBox-PhoneLab/main/data/device-profiles/community-catalog.json";
    private const string LegacyCommunityCatalogUrl = "https://raw.githubusercontent.com/bsthen/device-models/main/devices.json";
    private const string CommunitySource = "Google Play supported-device identifiers via bsthen/device-models (Apache-2.0)";
    private const string CommunitySourceUrl = "https://github.com/bsthen/device-models";
    private const int MaxCommunityCatalogCharacters = 30_000_000;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true, MaxDepth = 32 };

    private string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoftBox", "PhoneLab");
    private string CachePath => Path.Combine(CacheDirectory, "device-profiles.json");
    private string CommunityProfileCachePath => Path.Combine(CacheDirectory, "community-device-profiles.json");
    private string LegacyCommunityCachePath => Path.Combine(CacheDirectory, "google-play-device-models.json");

    public async Task<DeviceProfileCatalogDocument> LoadAsync()
    {
        var curated = await ReadCuratedCatalogAsync();
        var merged = curated.Profiles.ToList();
        var community = await ReadCommunityCatalogAsync();
        var loadedCommunityDocument = community is not null;

        if (community is not null)
        {
            foreach (var profile in community.Profiles)
                MergeOrAppend(merged, profile);
        }
        else
        {
            foreach (var profile in await ReadLegacyGoogleModelCatalogAsync())
                MergeOrAppend(merged, profile);
        }

        var sources = curated.Sources.Concat(community?.Sources ?? []).ToList();
        var sourceText = curated.Source;
        if (community is not null)
            sourceText += " | " + community.Source;
        else if (!loadedCommunityDocument && File.Exists(LegacyCommunityCachePath))
            sourceText += " | " + CommunitySource;

        return new DeviceProfileCatalogDocument
        {
            SchemaVersion = 1,
            CatalogVersion = curated.CatalogVersion + (community is null ? "" : "+" + community.CatalogVersion),
            Source = sourceText,
            UpdatedAtUtc = community?.UpdatedAtUtc > curated.UpdatedAtUtc ? community.UpdatedAtUtc : curated.UpdatedAtUtc,
            GeneratedAtUtc = community?.GeneratedAtUtc ?? curated.GeneratedAtUtc,
            Sources = sources,
            Profiles = merged
        };
    }

    public async Task<int> UpdateAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(CacheDirectory);
        var updatedAtLeastOneSource = false;

        try
        {
            var json = await DownloadTextAsync(SoftBoxCatalogUrl, 10_000_000, cancellationToken);
            var document = JsonSerializer.Deserialize<DeviceProfileCatalogDocument>(json, _json)
                ?? throw new InvalidDataException("The SoftBox device catalog is empty.");
            if (document.SchemaVersion != 1 || document.Profiles.Count > 100_000)
                throw new InvalidDataException("The SoftBox device catalog schema or size is not supported.");
            await File.WriteAllTextAsync(CachePath, json, cancellationToken);
            updatedAtLeastOneSource = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidDataException) { }

        try
        {
            var json = await DownloadTextAsync(CommunityProfileCatalogUrl, MaxCommunityCatalogCharacters, cancellationToken);
            var document = JsonSerializer.Deserialize<DeviceProfileCatalogDocument>(json, _json)
                ?? throw new InvalidDataException("The community device catalog is empty.");
            var validation = DeviceProfileCatalogValidator.ValidateCommunityCatalog(document);
            if (!validation.IsValid)
                throw new InvalidDataException("Community device catalog validation failed: " + string.Join(" | ", validation.Errors.Take(5)));
            await File.WriteAllTextAsync(CommunityProfileCachePath, json, cancellationToken);
            updatedAtLeastOneSource = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidDataException) { }

        // Existing installs can still update the original Google Play model-code feed while the new
        // combined Google/LineageOS catalogue is not yet available on the default branch.
        if (!File.Exists(CommunityProfileCachePath))
        {
            try
            {
                var json = await DownloadTextAsync(LegacyCommunityCatalogUrl, MaxCommunityCatalogCharacters, cancellationToken);
                var community = JsonSerializer.Deserialize<Dictionary<string, CommunityDeviceModel>>(json, _json)
                    ?? throw new InvalidDataException("The legacy community device catalog is empty.");
                if (community.Count is 0 or > 250_000 ||
                    !community.Take(100).Any(x => !string.IsNullOrWhiteSpace(x.Key) &&
                        !string.IsNullOrWhiteSpace(x.Value.Brand) && !string.IsNullOrWhiteSpace(x.Value.Name)))
                    throw new InvalidDataException("The legacy community catalog is not a valid device mapping.");
                await File.WriteAllTextAsync(LegacyCommunityCachePath, json, cancellationToken);
                updatedAtLeastOneSource = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidDataException) { }
        }

        if (!updatedAtLeastOneSource)
            throw new HttpRequestException("تعذر تحديث قاعدة الموديلات من المصادر المتاحة. سيستمر PhoneLab باستخدام النسخ المحلية المحفوظة.");

        return (await LoadAsync()).Profiles.Count;
    }

    public async Task<DeviceProfileMatch> MatchAsync(DeviceSnapshot device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var catalog = await LoadAsync();
        return DeviceCapabilityEngine.ResolveIdentity(device, catalog.Profiles).ToLegacyMatch();
    }

    private async Task<DeviceProfileCatalogDocument> ReadCuratedCatalogAsync()
    {
        foreach (var path in new[]
        {
            CachePath,
            Path.Combine(AppContext.BaseDirectory, "Data", "device-profiles", "catalog.json")
        })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var document = JsonSerializer.Deserialize<DeviceProfileCatalogDocument>(
                    await File.ReadAllTextAsync(path), _json);
                if (document is { SchemaVersion: 1, Profiles.Count: <= 100_000 })
                    return document;
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }
        return new DeviceProfileCatalogDocument();
    }

    private async Task<DeviceProfileCatalogDocument?> ReadCommunityCatalogAsync()
    {
        foreach (var path in new[]
        {
            CommunityProfileCachePath,
            Path.Combine(AppContext.BaseDirectory, "Data", "device-profiles", "community-catalog.json")
        })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var document = JsonSerializer.Deserialize<DeviceProfileCatalogDocument>(
                    await File.ReadAllTextAsync(path), _json);
                var validation = DeviceProfileCatalogValidator.ValidateCommunityCatalog(document);
                if (validation.IsValid)
                    return document;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { }
        }
        return null;
    }

    private async Task<IReadOnlyList<DeviceProfile>> ReadLegacyGoogleModelCatalogAsync()
    {
        if (!File.Exists(LegacyCommunityCachePath)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(LegacyCommunityCachePath);
            if (json.Length > MaxCommunityCatalogCharacters) return [];
            var community = JsonSerializer.Deserialize<Dictionary<string, CommunityDeviceModel>>(json, _json);
            if (community is null || community.Count > 250_000) return [];
            var result = new List<DeviceProfile>(community.Count);
            foreach (var entry in community)
            {
                var code = entry.Key.Trim();
                var brand = entry.Value.Brand?.Trim() ?? "";
                var name = entry.Value.Name?.Trim() ?? "";
                if (code.Length is 0 or > 120 || brand.Length is 0 or > 160 ||
                    name.Length is 0 or > 240) continue;
                result.Add(new DeviceProfile
                {
                    Id = "google-play-" + NormalizeCode(code),
                    Manufacturer = brand,
                    MarketingName = name,
                    ModelNumbers = [code],
                    ModelAliases = [name],
                    IdentitySource = CommunitySource,
                    IdentitySourceUrl = CommunitySourceUrl,
                    IdentitySourceLicense = "Apache-2.0",
                    IdentityVerified = false
                });
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }

    private static void MergeOrAppend(List<DeviceProfile> profiles, DeviceProfile incoming)
    {
        if (profiles.Any(existing => existing.Id.Equals(incoming.Id, StringComparison.OrdinalIgnoreCase) &&
                                     !SharesIdentifier(existing, incoming)))
            return;

        var index = profiles.FindIndex(existing =>
            existing.Manufacturer.Equals(incoming.Manufacturer, StringComparison.OrdinalIgnoreCase) &&
            SharesIdentifier(existing, incoming));
        if (index < 0)
        {
            profiles.Add(incoming);
            return;
        }

        profiles[index] = Merge(profiles[index], incoming);
    }

    private static DeviceProfile Merge(DeviceProfile preferred, DeviceProfile extra)
        => new()
        {
            Id = preferred.Id,
            Manufacturer = preferred.Manufacturer,
            MarketingName = First(preferred.MarketingName, extra.MarketingName),
            ModelNumbers = Union(preferred.ModelNumbers, extra.ModelNumbers),
            DeviceCodes = Union(preferred.DeviceCodes, extra.DeviceCodes),
            ProductNames = Union(preferred.ProductNames, extra.ProductNames),
            ModelAliases = Union(preferred.ModelAliases, extra.ModelAliases, [extra.MarketingName]),
            BoardIds = Union(preferred.BoardIds, extra.BoardIds),
            SocAliases = Union(preferred.SocAliases, extra.SocAliases),
            HardwareFamily = First(preferred.HardwareFamily, extra.HardwareFamily),
            DesignBatteryCapacityMah = preferred.DesignBatteryCapacityMah ?? extra.DesignBatteryCapacityMah,
            BoardMapId = preferred.BoardMapId,
            IdentitySource = First(preferred.IdentitySource, extra.IdentitySource),
            IdentitySourceUrl = First(preferred.IdentitySourceUrl, extra.IdentitySourceUrl),
            IdentitySourceLicense = JoinDistinct(preferred.IdentitySourceLicense, extra.IdentitySourceLicense),
            IdentitySourceRevision = First(preferred.IdentitySourceRevision, extra.IdentitySourceRevision),
            IdentityVerified = preferred.IdentityVerified,
            DeviceType = First(preferred.DeviceType, extra.DeviceType),
            ReleaseDate = First(preferred.ReleaseDate, extra.ReleaseDate),
            Cpu = First(preferred.Cpu, extra.Cpu),
            CpuCores = preferred.CpuCores ?? extra.CpuCores,
            CpuFrequency = First(preferred.CpuFrequency, extra.CpuFrequency),
            Gpu = First(preferred.Gpu, extra.Gpu),
            Ram = First(preferred.Ram, extra.Ram),
            Storage = First(preferred.Storage, extra.Storage),
            Display = First(preferred.Display, extra.Display),
            ScreenResolution = First(preferred.ScreenResolution, extra.ScreenResolution),
            ScreenTechnology = First(preferred.ScreenTechnology, extra.ScreenTechnology),
            Wifi = First(preferred.Wifi, extra.Wifi),
            Bluetooth = First(preferred.Bluetooth, extra.Bluetooth),
            Peripherals = Union(preferred.Peripherals, extra.Peripherals),
            SupportedOsVersions = Union(preferred.SupportedOsVersions, extra.SupportedOsVersions),
            CurrentOsBranch = First(preferred.CurrentOsBranch, extra.CurrentOsBranch),
            Sources = preferred.Sources.Concat(extra.Sources)
                .GroupBy(source => source.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToList()
        };

    private static bool SharesIdentifier(DeviceProfile left, DeviceProfile right)
        => left.ModelNumbers.Any(a => right.ModelNumbers.Any(b => NormalizeCode(a) == NormalizeCode(b))) ||
           left.DeviceCodes.Any(a => right.DeviceCodes.Any(b => NormalizeCode(a) == NormalizeCode(b)));

    private static string[] Union(params string[][] arrays)
        => arrays.SelectMany(array => array ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string First(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string JoinDistinct(params string[] values)
        => string.Join(" + ", values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string NormalizeCode(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static async Task<string> DownloadTextAsync(
        string url, int maxCharacters, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maxCharacters)
            throw new InvalidDataException("The requested device catalog exceeds the permitted size.");
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (json.Length > maxCharacters)
            throw new InvalidDataException("The requested device catalog exceeds the permitted size.");
        return json;
    }

    private sealed class CommunityDeviceModel
    {
        public string? Brand { get; init; }
        public string? Name { get; init; }
    }
}
