using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Core.Compatibility;

public sealed record DeviceProfileCoverageRow(
    string ProfileId,
    string Manufacturer,
    string MarketingName,
    int ModelNumberCount,
    int DeviceCodeCount,
    int ProductIdCount,
    int BoardIdCount,
    int SocAliasCount,
    string HardwareFamily,
    int? DesignBatteryCapacityMah,
    bool CatalogMarksIdentityVerified,
    bool IdentitySourceIsHttps,
    string IdentitySource,
    string IdentitySourceUrl,
    string BoardMapId,
    string CoverageLevel,
    IReadOnlyList<string> MissingEvidence);

public sealed record DeviceCapabilityMatrixReport(
    DateTimeOffset GeneratedAtUtc,
    string CatalogVersion,
    int TotalProfiles,
    int ProfilesWithModelNumbers,
    int ProfilesWithDeviceCodes,
    int ProfilesWithBoardIds,
    int ProfilesWithBoardMapReferences,
    int ProfilesWithSocAliases,
    int ProfilesWithHttpsIdentitySources,
    int ProfilesMarkedIdentityVerified,
    int ProfilesMissingBoardEvidence,
    IReadOnlyList<DeviceProfileCoverageRow> Profiles);

/// <summary>
/// Builds an auditable coverage matrix from the actual curated/community device catalogue.
/// A BoardMapId is only a catalogue reference; it is not proof that a matching PCB drawing exists.
/// Empty board identifiers stay unknown and are never inferred from a model, SoC or marketing name.
/// </summary>
public static class DeviceCapabilityMatrixBuilder
{
    public static DeviceCapabilityMatrixReport Build(
        DeviceProfileCatalogDocument catalog,
        DateTimeOffset? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var sourceProfiles = catalog.Profiles ?? [];
        if (sourceProfiles.Count > 250_000)
            throw new InvalidDataException("Device profile catalogue exceeds the supported matrix size.");

        var rows = sourceProfiles
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Id))
            .Select(BuildRow)
            .OrderBy(row => row.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.MarketingName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProfileId, StringComparer.Ordinal)
            .ToArray();

        return new DeviceCapabilityMatrixReport(
            generatedAtUtc ?? DateTimeOffset.UtcNow,
            catalog.CatalogVersion ?? "",
            rows.Length,
            rows.Count(row => row.ModelNumberCount > 0),
            rows.Count(row => row.DeviceCodeCount > 0),
            rows.Count(row => row.BoardIdCount > 0),
            rows.Count(row => !string.IsNullOrWhiteSpace(row.BoardMapId)),
            rows.Count(row => row.SocAliasCount > 0),
            rows.Count(row => row.IdentitySourceIsHttps),
            rows.Count(row => row.CatalogMarksIdentityVerified && row.IdentitySourceIsHttps),
            rows.Count(row => row.BoardIdCount == 0 || string.IsNullOrWhiteSpace(row.BoardMapId)),
            rows);
    }

    public static string ToCsv(DeviceCapabilityMatrixReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new StringBuilder();
        output.AppendLine("ProfileId,Manufacturer,MarketingName,ModelNumberCount,DeviceCodeCount,ProductIdCount,BoardIdCount,SocAliasCount,HardwareFamily,DesignBatteryCapacityMah,CatalogMarksIdentityVerified,IdentitySourceIsHttps,IdentitySource,IdentitySourceUrl,BoardMapId,CoverageLevel,MissingEvidence");
        foreach (var row in report.Profiles)
        {
            output.AppendLine(string.Join(",", new[]
            {
                Csv(row.ProfileId),
                Csv(row.Manufacturer),
                Csv(row.MarketingName),
                Csv(row.ModelNumberCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Csv(row.DeviceCodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Csv(row.ProductIdCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Csv(row.BoardIdCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Csv(row.SocAliasCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Csv(row.HardwareFamily),
                Csv(row.DesignBatteryCapacityMah?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                Csv(row.CatalogMarksIdentityVerified.ToString()),
                Csv(row.IdentitySourceIsHttps.ToString()),
                Csv(row.IdentitySource),
                Csv(row.IdentitySourceUrl),
                Csv(row.BoardMapId),
                Csv(row.CoverageLevel),
                Csv(string.Join("; ", row.MissingEvidence))
            }));
        }
        return output.ToString();
    }

    private static DeviceProfileCoverageRow BuildRow(DeviceProfile profile)
    {
        var modelNumbers = Count(profile.ModelNumbers);
        var deviceCodes = Count(profile.DeviceCodes);
        var productIds = Count(profile.ProductNames);
        var boardIds = Count(profile.BoardIds);
        var socAliases = Count(profile.SocAliases);
        var hasBoardMapReference = !string.IsNullOrWhiteSpace(profile.BoardMapId);
        var sourceIsHttps = Uri.TryCreate(profile.IdentitySourceUrl, UriKind.Absolute, out var sourceUri) &&
                            sourceUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var missing = new List<string>();

        if (modelNumbers == 0) missing.Add("Exact model number");
        if (deviceCodes == 0) missing.Add("Device codename");
        if (productIds == 0) missing.Add("Android product ID");
        if (boardIds == 0) missing.Add("Board ID / board revision evidence");
        if (!hasBoardMapReference) missing.Add("Board-map catalogue reference");
        if (socAliases == 0) missing.Add("SoC alias");
        if (string.IsNullOrWhiteSpace(profile.HardwareFamily)) missing.Add("Hardware family");
        if (!sourceIsHttps) missing.Add("HTTPS identity source");

        var coverage = boardIds > 0 && hasBoardMapReference
            ? "Board identifiers and map reference declared"
            : boardIds > 0
                ? "Board identifiers declared; map reference missing"
                : "Identity profile only; board evidence incomplete";

        return new DeviceProfileCoverageRow(
            profile.Id,
            profile.Manufacturer ?? "",
            profile.MarketingName ?? "",
            modelNumbers,
            deviceCodes,
            productIds,
            boardIds,
            socAliases,
            profile.HardwareFamily ?? "",
            profile.DesignBatteryCapacityMah,
            profile.IdentityVerified,
            sourceIsHttps,
            profile.IdentitySource ?? "",
            profile.IdentitySourceUrl ?? "",
            profile.BoardMapId ?? "",
            coverage,
            missing);
    }

    private static int Count(IEnumerable<string>? values)
        => values?.Count(value => !string.IsNullOrWhiteSpace(value)) ?? 0;

    private static string Csv(string? value)
    {
        var normalized = value ?? "";
        var trimmed = normalized.TrimStart(' ', '\t', '\r', '\n');
        if (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@')
            normalized = "'" + normalized;
        return "\"" + normalized.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
