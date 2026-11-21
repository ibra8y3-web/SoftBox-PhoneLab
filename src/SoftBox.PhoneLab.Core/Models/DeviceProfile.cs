namespace SoftBox.PhoneLab.Core.Models;

/// <summary>Identity and published specification profile only. It is never PCB geometry.</summary>
public sealed class DeviceProfileCatalogDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string CatalogVersion { get; init; } = "1";
    public string Source { get; init; } = "";
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public List<DeviceProfileSource> Sources { get; init; } = [];
    public List<DeviceProfile> Profiles { get; init; } = [];
}

public sealed class DeviceProfileSource
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    public string License { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string Revision { get; init; } = "";
    public int RecordCount { get; init; }
    public DateTimeOffset RetrievedAtUtc { get; init; }
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
    public string IdentitySourceLicense { get; init; } = "";
    public string IdentitySourceRevision { get; init; } = "";
    public bool IdentityVerified { get; init; }

    // Published specifications are evidence for identification and planning, not live health measurements.
    public string DeviceType { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public string Cpu { get; init; } = "";
    public int? CpuCores { get; init; }
    public string CpuFrequency { get; init; } = "";
    public string Gpu { get; init; } = "";
    public string Ram { get; init; } = "";
    public string Storage { get; init; } = "";
    public string Display { get; init; } = "";
    public string ScreenResolution { get; init; } = "";
    public string ScreenTechnology { get; init; } = "";
    public string Wifi { get; init; } = "";
    public string Bluetooth { get; init; } = "";
    public string[] Peripherals { get; init; } = [];
    public string[] SupportedOsVersions { get; init; } = [];
    public string CurrentOsBranch { get; init; } = "";
    public List<DeviceProfileSource> Sources { get; init; } = [];
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
