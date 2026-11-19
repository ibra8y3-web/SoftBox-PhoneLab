using System.IO;
using System.Net.Http;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Merges SoftBox's curated profiles with the open Google Play supported-device model-code
/// mapping. Community records improve identity lookup only; they never provide PCB geometry.
/// </summary>
public sealed class DeviceProfileCatalog
{
    private const string SoftBoxCatalogUrl = "https://raw.githubusercontent.com/ibra8y3-web/SoftBox-PhoneLab/main/data/device-profiles/catalog.json";
    private const string CommunityCatalogUrl = "https://raw.githubusercontent.com/bsthen/device-models/main/devices.json";
    private const string CommunitySource = "Google Play supported-device identifiers via bsthen/device-models (Apache-2.0)";
    private const string CommunitySourceUrl = "https://github.com/bsthen/device-models";
    private const int MaxCommunityCatalogCharacters = 30_000_000;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true, MaxDepth = 24 };

    private string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoftBox", "PhoneLab");
    private string CachePath => Path.Combine(CacheDirectory, "device-profiles.json");
    private string CommunityCachePath => Path.Combine(CacheDirectory, "google-play-device-models.json");

    public async Task<DeviceProfileCatalogDocument> LoadAsync()
    {
        DeviceProfileCatalogDocument curated = new();
        foreach (var path in new[] { CachePath, Path.Combine(AppContext.BaseDirectory, "Data", "device-profiles", "catalog.json") })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var document = JsonSerializer.Deserialize<DeviceProfileCatalogDocument>(await File.ReadAllTextAsync(path), _json);
                if (document is { SchemaVersion: 1 })
                {
                    curated = document;
                    break;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }

        var merged = curated.Profiles.ToList();
        var knownCodes = new HashSet<string>(
            merged.SelectMany(x => x.ModelNumbers), StringComparer.OrdinalIgnoreCase);

        if (File.Exists(CommunityCachePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(CommunityCachePath);
                if (json.Length <= MaxCommunityCatalogCharacters)
                {
                    var community = JsonSerializer.Deserialize<Dictionary<string, CommunityDeviceModel>>(json, _json);
                    if (community is not null && community.Count <= 250_000)
                    {
                        foreach (var entry in community)
                        {
                            var code = entry.Key.Trim();
                            var brand = entry.Value.Brand?.Trim() ?? "";
                            var name = entry.Value.Name?.Trim() ?? "";
                            if (code.Length is 0 or > 120 || brand.Length == 0 || name.Length == 0 ||
                                brand.Length > 160 || name.Length > 240 || !knownCodes.Add(code))
                                continue;

                            merged.Add(new DeviceProfile
                            {
                                Id = "google-play-" + NormalizeCode(code),
                                Manufacturer = brand,
                                MarketingName = name,
                                ModelNumbers = [code],
                                ModelAliases = [name],
                                IdentitySource = CommunitySource,
                                IdentitySourceUrl = CommunitySourceUrl,
                                IdentityVerified = false
                            });
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }

        return new DeviceProfileCatalogDocument
        {
            SchemaVersion = curated.SchemaVersion,
            CatalogVersion = curated.CatalogVersion,
            Source = curated.Source + " | " + CommunitySource,
            UpdatedAtUtc = curated.UpdatedAtUtc,
            Profiles = merged
        };
    }

    public async Task<int> UpdateAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(CacheDirectory);
        var updatedAtLeastOneSource = false;

        try
        {
            using var response = await Http.GetAsync(SoftBoxCatalogUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
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
            using var response = await Http.GetAsync(CommunityCatalogUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxCommunityCatalogCharacters)
                throw new InvalidDataException("The community device catalog exceeds the permitted size.");
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (json.Length > MaxCommunityCatalogCharacters)
                throw new InvalidDataException("The community device catalog exceeds the permitted size.");

            var community = JsonSerializer.Deserialize<Dictionary<string, CommunityDeviceModel>>(json, _json)
                ?? throw new InvalidDataException("The community device catalog is empty.");
            if (community.Count is 0 or > 250_000)
                throw new InvalidDataException("The community device catalog entry count is not supported.");

            // Validate a representative set and reject files that are structurally unrelated.
            if (!community.Take(100).Any(x =>
                    !string.IsNullOrWhiteSpace(x.Key) &&
                    !string.IsNullOrWhiteSpace(x.Value.Brand) &&
                    !string.IsNullOrWhiteSpace(x.Value.Name)))
                throw new InvalidDataException("The community catalog has no valid device records.");

            await File.WriteAllTextAsync(CommunityCachePath, json, cancellationToken);
            updatedAtLeastOneSource = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidDataException) { }

        if (!updatedAtLeastOneSource)
            throw new HttpRequestException("تعذر تحديث قاعدتي الموديلات. سيستمر PhoneLab باستخدام النسخ المحلية المحفوظة.");

        return (await LoadAsync()).Profiles.Count;
    }

    public async Task<DeviceProfileMatch> MatchAsync(DeviceSnapshot device)
    {
        var catalog = await LoadAsync();
        var best = DeviceProfileMatch.Unknown;
        foreach (var profile in catalog.Profiles)
        {
            var score = 0;
            var reasons = new List<string>();
            if (profile.ModelNumbers.Contains(device.ModelNumber, StringComparer.OrdinalIgnoreCase) &&
                device.ModelNumber != "غير متاح")
            {
                score += 120;
                reasons.Add("مطابقة رقم الموديل");
            }
            if (profile.DeviceCodes.Contains(device.DeviceCode, StringComparer.OrdinalIgnoreCase) &&
                device.DeviceCode != "غير متاح")
            {
                score += 110;
                reasons.Add("مطابقة Device Codename");
            }
            if (profile.ProductNames.Contains(device.Product, StringComparer.OrdinalIgnoreCase) &&
                device.Product != "غير متاح")
            {
                score += 70;
                reasons.Add("مطابقة Product ID");
            }
            if (profile.Manufacturer.Equals(device.Manufacturer, StringComparison.OrdinalIgnoreCase) &&
                device.Manufacturer != "غير متاح")
            {
                score += 10;
                reasons.Add("مطابقة الشركة");
            }
            if (profile.ModelAliases.Any(alias => ContainsNormalized(device.Model, alias)))
            {
                score += 35;
                reasons.Add("مطابقة اسم تجاري");
            }
            if (profile.BoardIds.Contains(device.Board, StringComparer.OrdinalIgnoreCase) &&
                device.Board != "غير متاح")
            {
                score += 20;
                reasons.Add("مطابقة Board ID");
            }

            if (score > best.Score)
            {
                var confidence = score >= 110 && profile.IdentityVerified ? "مطابقة دقيقة" :
                    score >= 70 ? "مرجح" : "ضعيف";
                best = new DeviceProfileMatch(profile, score, confidence, reasons);
            }
        }
        return best;
    }

    private static string NormalizeCode(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool ContainsNormalized(string source, string target)
    {
        var a = NormalizeCode(source);
        var b = NormalizeCode(target);
        return b.Length > 3 && a.Contains(b, StringComparison.Ordinal);
    }

    private sealed class CommunityDeviceModel
    {
        public CommunityDeviceModel() { }
        public string? Brand { get; init; }
        public string? Name { get; init; }
    }
}
