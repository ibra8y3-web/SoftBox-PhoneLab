namespace SoftBox.PhoneLab.Core.Models;

public sealed record DeviceSnapshot
{
    public string Serial { get; init; } = "";
    public string Manufacturer { get; init; } = "غير متاح";
    public string Model { get; init; } = "غير متاح";
    public string ModelNumber { get; init; } = "غير متاح";
    public string Product { get; init; } = "غير متاح";
    public string DeviceCode { get; init; } = "غير متاح";
    public string HardwareSku { get; init; } = "غير متاح";
    public string BuildFingerprint { get; init; } = "غير متاح";
    public string Bootloader { get; init; } = "غير متاح";
    public string Board { get; init; } = "غير متاح";
    public string HardwareRevision { get; init; } = "غير متاح";
    public string BoardRevision { get; init; } = "غير متاح";
    public string Soc { get; init; } = "غير متاح";
    public string CpuDescription { get; init; } = "غير متاح";
    public int CpuCores { get; init; }
    public string CpuAbi { get; init; } = "غير متاح";
    public double? RamGiB { get; init; }
    public long? DataTotalKiB { get; init; }
    public long? DataUsedKiB { get; init; }
    public string DisplayResolution { get; init; } = "غير متاح";
    public string DisplayDensity { get; init; } = "غير متاح";
    public string AndroidVersion { get; init; } = "غير متاح";
    public string ApiLevel { get; init; } = "غير متاح";
    public string BuildId { get; init; } = "غير متاح";
    public string SecurityPatch { get; init; } = "غير متاح";
    public string Baseband { get; init; } = "غير متاح";
    public int? BatteryPercent { get; init; }
    public int? BatteryVoltageMillivolts { get; init; }
    public double? BatteryTemperatureCelsius { get; init; }
    public string BatteryHealth { get; init; } = "غير متاح";
    public string BatteryStatus { get; init; } = "غير متاح";
    public DateTimeOffset ReadAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
