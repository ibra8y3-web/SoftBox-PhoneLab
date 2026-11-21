using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Core.Compatibility;

/// <summary>
/// Efficiently joins curated and community device identities using a manufacturer-scoped identifier index.
/// Ambiguous identifier collisions are retained as separate rows for the identity resolver to report.
/// </summary>
public static class DeviceProfileCatalogMerger
{
    public static IReadOnlyList<DeviceProfile> Merge(
        IEnumerable<DeviceProfile> curatedProfiles,
        IEnumerable<DeviceProfile> communityProfiles)
    {
        ArgumentNullException.ThrowIfNull(curatedProfiles);
        ArgumentNullException.ThrowIfNull(communityProfiles);

        var profiles = curatedProfiles.ToList();
        var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var identifierIndex = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            if (!string.IsNullOrWhiteSpace(profile.Id) && !byId.ContainsKey(profile.Id))
                byId.Add(profile.Id, index);
            IndexProfile(identifierIndex, profile, index);
        }

        foreach (var incoming in communityProfiles)
        {
            if (incoming is null)
                continue;

            if (!string.IsNullOrWhiteSpace(incoming.Id) && byId.TryGetValue(incoming.Id, out var sameIdIndex))
            {
                if (!SharesIdentifier(profiles[sameIdIndex], incoming))
                    continue;

                profiles[sameIdIndex] = MergePair(profiles[sameIdIndex], incoming);
                IndexProfile(identifierIndex, profiles[sameIdIndex], sameIdIndex);
                continue;
            }

            var candidates = FindCandidates(identifierIndex, incoming);
            if (candidates.Count == 1)
            {
                var index = candidates.Single();
                profiles[index] = MergePair(profiles[index], incoming);
                IndexProfile(identifierIndex, profiles[index], index);
                if (!string.IsNullOrWhiteSpace(incoming.Id) && !byId.ContainsKey(incoming.Id))
                    byId.Add(incoming.Id, index);
                continue;
            }

            // Zero candidates means a distinct profile; multiple candidates mean the identifier is ambiguous.
            // Preserve the new row instead of arbitrarily merging it into one of the existing models.
            var newIndex = profiles.Count;
            profiles.Add(incoming);
            if (!string.IsNullOrWhiteSpace(incoming.Id) && !byId.ContainsKey(incoming.Id))
                byId.Add(incoming.Id, newIndex);
            IndexProfile(identifierIndex, incoming, newIndex);
        }

        return profiles;
    }

    private static HashSet<int> FindCandidates(
        Dictionary<string, HashSet<int>> index,
        DeviceProfile profile)
    {
        var candidates = new HashSet<int>();
        foreach (var key in Keys(profile))
        {
            if (index.TryGetValue(key, out var owners))
                candidates.UnionWith(owners);
        }
        return candidates;
    }

    private static void IndexProfile(
        Dictionary<string, HashSet<int>> index,
        DeviceProfile profile,
        int profileIndex)
    {
        foreach (var key in Keys(profile))
        {
            if (!index.TryGetValue(key, out var owners))
            {
                owners = [];
                index.Add(key, owners);
            }
            owners.Add(profileIndex);
        }
    }

    private static IEnumerable<string> Keys(DeviceProfile profile)
    {
        var manufacturer = NormalizeCode(profile.Manufacturer);
        if (manufacturer.Length == 0)
            yield break;

        foreach (var identifier in (profile.ModelNumbers ?? []).Concat(profile.DeviceCodes ?? []))
        {
            var normalized = NormalizeCode(identifier);
            if (normalized.Length > 0)
                yield return manufacturer + ":" + normalized;
        }
    }

    private static bool SharesIdentifier(DeviceProfile left, DeviceProfile right)
    {
        var rightKeys = Keys(right).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Keys(left).Any(rightKeys.Contains);
    }

    private static string NormalizeCode(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static DeviceProfile MergePair(DeviceProfile preferred, DeviceProfile extra)
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
            Sources = (preferred.Sources ?? []).Concat(extra.Sources ?? [])
                .GroupBy(source => source.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToList()
        };

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
}
