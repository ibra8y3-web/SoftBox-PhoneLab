namespace SoftBox.PhoneLab.Core.Models;

/// <summary>Identity profile only. It is not a PCB map and must never imply hardware-map accuracy.</summary>
public sealed class DeviceProfileCatalogDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string CatalogVersion { get; init; } = "1";
    public string Source { get; init; } = "";
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public List<DeviceProfile> Profiles { get; init; } = [];
}

public sealed class DeviceProfile
{
    public string Id { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string MarketingName { get; init; } = "";
    public string[] ModelNumbers { get; init; } = [];
    public string[] DeviceCodes { get; init; } = [];
    public string[] ProductNames { get; init; } = [];
    public string[] ModelAliases { get; init; } = [];
    public string[] BoardIds { get; init; } = [];
    public string[] SocAliases { get; init; } = [];
    public string HardwareFamily { get; init; } = "";
    public int? DesignBatteryCapacityMah { get; init; }
    public string BoardMapId { get; init; } = "";
    public string IdentitySource { get; init; } = "";
    public string IdentitySourceUrl { get; init; } = "";
    public bool IdentityVerified { get; init; }
}

public sealed record DeviceProfileMatch(
    DeviceProfile? Profile,
    int Score,
    string Confidence,
    IReadOnlyList<string> Reasons)
{
    public bool IsExact => Profile is not null && Score >= 100;
    public string DisplayName => Profile?.MarketingName ?? "موديل غير موجود في قاعدة البيانات";
    public static DeviceProfileMatch Unknown { get; } = new(null, 0, "غير معروف", []);
}
