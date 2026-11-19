using System.IO;
using System.Net.Http;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Local model identity catalogue with an optional transport-secured GitHub update channel.
/// Profiles identify devices; they do not supply or certify PCB geometry.
/// </summary>
public sealed class DeviceProfileCatalog
{
    private const string RemoteCatalog = "https://raw.githubusercontent.com/ibra8y3-web/SoftBox-PhoneLab/main/data/device-profiles/catalog.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoftBox", "PhoneLab", "device-profiles.json");

    public async Task<DeviceProfileCatalogDocument> LoadAsync()
    {
        foreach (var path in new[] { CachePath, Path.Combine(AppContext.BaseDirectory, "Data", "device-profiles", "catalog.json") })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var document = JsonSerializer.Deserialize<DeviceProfileCatalogDocument>(await File.ReadAllTextAsync(path), _json);
                if (document is { SchemaVersion: 1 }) return document;
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }
        return new DeviceProfileCatalogDocument();
    }

    public async Task<int> UpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(RemoteCatalog, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = JsonSerializer.Deserialize<DeviceProfileCatalogDocument>(json, _json)
            ?? throw new InvalidDataException("The device catalog is empty.");
        if (document.SchemaVersion != 1 || document.Profiles.Count > 100000)
            throw new InvalidDataException("The device catalog schema or size is not supported.");
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        await File.WriteAllTextAsync(CachePath, json, cancellationToken);
        return document.Profiles.Count;
    }

    public async Task<DeviceProfileMatch> MatchAsync(DeviceSnapshot device)
    {
        var catalog = await LoadAsync();
        var best = DeviceProfileMatch.Unknown;
        foreach (var profile in catalog.Profiles)
        {
            var score = 0;
            var reasons = new List<string>();
            if (profile.ModelNumbers.Contains(device.ModelNumber, StringComparer.OrdinalIgnoreCase) && device.ModelNumber != "غير متاح")
            {
                score += 120; reasons.Add("مطابقة رقم الموديل");
            }
            if (profile.DeviceCodes.Contains(device.DeviceCode, StringComparer.OrdinalIgnoreCase) && device.DeviceCode != "غير متاح")
            {
                score += 110; reasons.Add("مطابقة Device Codename");
            }
            if (profile.ProductNames.Contains(device.Product, StringComparer.OrdinalIgnoreCase) && device.Product != "غير متاح")
            {
                score += 70; reasons.Add("مطابقة Product ID");
            }
            if (profile.Manufacturer.Equals(device.Manufacturer, StringComparison.OrdinalIgnoreCase) && device.Manufacturer != "غير متاح")
            {
                score += 10; reasons.Add("مطابقة الشركة");
            }
            if (profile.ModelAliases.Any(alias => ContainsNormalized(device.Model, alias)))
            {
                score += 35; reasons.Add("مطابقة اسم تجاري");
            }
            if (profile.BoardIds.Contains(device.Board, StringComparer.OrdinalIgnoreCase) && device.Board != "غير متاح")
            {
                score += 20; reasons.Add("مطابقة Board ID");
            }
            if (score > best.Score)
            {
                var confidence = score >= 110 && profile.IdentityVerified ? "مطابقة دقيقة" : score >= 70 ? "مرجح" : "ضعيف";
                best = new DeviceProfileMatch(profile, score, confidence, reasons);
            }
        }
        return best;
    }

    private static bool ContainsNormalized(string source, string target)
    {
        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var a = Normalize(source); var b = Normalize(target);
        return b.Length > 3 && a.Contains(b, StringComparison.Ordinal);
    }
}
