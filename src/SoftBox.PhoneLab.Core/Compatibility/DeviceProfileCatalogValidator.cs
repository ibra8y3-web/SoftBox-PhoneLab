using System.Text.RegularExpressions;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Core.Compatibility;

public sealed record DeviceProfileCatalogValidation(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Validates externally curated community catalog snapshots before they enter the local device database.
/// Community data can improve discovery, but can never self-assert OEM verification or PCB-map accuracy.
/// </summary>
public static class DeviceProfileCatalogValidator
{
    private static readonly Regex IdPattern = new(@"^[a-z0-9][a-z0-9._-]{0,159}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new(@"^[a-f0-9]{64}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static DeviceProfileCatalogValidation ValidateCommunityCatalog(DeviceProfileCatalogDocument? document)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (document is null)
            return new(false, ["Community device catalog is missing."], warnings);
        if (document.SchemaVersion != 1)
            errors.Add("Unsupported community device catalog schema.");
        if (document.Profiles is null || document.Profiles.Count is < 1 or > 100_000)
            errors.Add("Community catalog must contain between 1 and 100,000 device profiles.");
        if (document.Sources is null || document.Sources.Count is < 1 or > 20)
            errors.Add("Community catalog must list between 1 and 20 source records.");

        var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.Sources is not null)
        {
            foreach (var source in document.Sources)
            {
                if (source is null ||
                    !IdPattern.IsMatch(source.Id) ||
                    !sourceIds.Add(source.Id) ||
                    string.IsNullOrWhiteSpace(source.Name) ||
                    source.Name.Length > 180 ||
                    !IsHttps(source.Url) ||
                    string.IsNullOrWhiteSpace(source.License) ||
                    source.License.Length > 80 ||
                    !Sha256Pattern.IsMatch(source.Sha256) ||
                    (string.IsNullOrWhiteSpace(source.Revision) || source.Revision.Length > 160) ||
                    source.RecordCount < 1 ||
                    source.RecordCount > 250_000 ||
                    source.RetrievedAtUtc == default)
                {
                    errors.Add("A source record has invalid identity, HTTPS URL, license, digest, record count or retrieval date.");
                    break;
                }
            }
        }

        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modelOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (document.Profiles is not null)
        {
            foreach (var profile in document.Profiles)
            {
                if (profile is null ||
                    !IdPattern.IsMatch(profile.Id) ||
                    !profileIds.Add(profile.Id) ||
                    string.IsNullOrWhiteSpace(profile.Manufacturer) ||
                    profile.Manufacturer.Length > 160 ||
                    string.IsNullOrWhiteSpace(profile.MarketingName) ||
                    profile.MarketingName.Length > 240 ||
                    string.IsNullOrWhiteSpace(profile.IdentitySource) ||
                    profile.IdentitySource.Length > 240 ||
                    !IsHttps(profile.IdentitySourceUrl) ||
                    string.IsNullOrWhiteSpace(profile.IdentitySourceLicense) ||
                    profile.IdentitySourceLicense.Length > 120)
                {
                    errors.Add("A device profile has missing/invalid identity or provenance fields.");
                    continue;
                }

                if (profile.IdentityVerified)
                    errors.Add("Community-sourced device profiles must never set identityVerified=true.");
                if (!string.IsNullOrWhiteSpace(profile.BoardMapId))
                    errors.Add("Community identity records must not claim a model-specific BoardMapId.");
                if (profile.ModelNumbers is null || profile.ModelNumbers.Length > 64 ||
                    profile.DeviceCodes is null || profile.DeviceCodes.Length > 32 ||
                    profile.SocAliases is null || profile.SocAliases.Length > 32 ||
                    profile.Peripherals is null || profile.Peripherals.Length > 128 ||
                    profile.SupportedOsVersions is null || profile.SupportedOsVersions.Length > 64)
                {
                    errors.Add("A device profile exceeds identifier or specification list limits.");
                    continue;
                }
                if (profile.ModelNumbers.Length == 0 && profile.DeviceCodes.Length == 0)
                    errors.Add("A device profile must have a model number or device codename.");
                if (profile.DesignBatteryCapacityMah is < 100 or > 20_000)
                    errors.Add("A device profile battery capacity is outside plausible data bounds.");
                if (profile.CpuCores is < 1 or > 64)
                    errors.Add("A device profile CPU core count is outside supported bounds.");

                foreach (var model in profile.ModelNumbers)
                {
                    if (string.IsNullOrWhiteSpace(model) || model.Length > 120 || model.Any(char.IsControl))
                        errors.Add("A device profile contains an invalid model number.");
                    else if (modelOwners.TryGetValue(model, out var owner) && owner != profile.Id)
                        warnings.Add($"Model identifier '{model}' appears in multiple profiles; matching must remain ambiguous until reviewed.");
                    else
                        modelOwners[model] = profile.Id;
                }
                if (profile.DeviceCodes.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 120) ||
                    profile.SocAliases.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 160))
                    errors.Add("A device profile contains an invalid device code or SoC alias.");
            }
        }

        return new(errors.Count == 0, errors.Distinct(StringComparer.Ordinal).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static bool IsHttps(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           uri.Scheme == Uri.UriSchemeHttps &&
           !string.IsNullOrWhiteSpace(uri.Host);
}
