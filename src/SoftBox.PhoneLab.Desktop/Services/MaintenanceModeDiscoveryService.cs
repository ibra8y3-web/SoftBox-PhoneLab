using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record MaintenanceModeDevice(
    WindowsUsbDevice UsbDevice,
    DeviceModeEvidence ModeEvidence,
    string ManufacturerLabel,
    string ComPort)
{
    public string HardwareId => $"{UsbDevice.VendorId}:{UsbDevice.ProductId}";
    public string DisplayName => string.IsNullOrWhiteSpace(UsbDevice.FriendlyName)
        ? UsbDevice.InstanceId
        : UsbDevice.FriendlyName;
    public string DriverStatus => UsbDevice.ProblemCode > 0
        ? $"مشكلة تعريف Windows (Code {UsbDevice.ProblemCode})"
        : $"PnP: {UsbDevice.PnpStatus} • Service: {(string.IsNullOrWhiteSpace(UsbDevice.Service) ? "غير معلن" : UsbDevice.Service)}";
    public bool IsSamsungDownloadMode =>
        ModeEvidence.Mode.Contains("Samsung Download Mode", StringComparison.OrdinalIgnoreCase);
    public bool IsFastboot =>
        ModeEvidence.Mode.Contains("Fastboot", StringComparison.OrdinalIgnoreCase) ||
        ModeEvidence.Mode.Contains("Bootloader", StringComparison.OrdinalIgnoreCase);
    public bool IsQualcommEdl =>
        ModeEvidence.Mode.Contains("Qualcomm EDL", StringComparison.OrdinalIgnoreCase);
    public bool IsMediaTekService =>
        ModeEvidence.Mode.Contains("MediaTek", StringComparison.OrdinalIgnoreCase);

    public string Details => string.Join(Environment.NewLine,
    [
        "DEVICE: " + DisplayName,
        "MANUFACTURER: " + ManufacturerLabel,
        "MODE: " + ModeEvidence.Mode,
        "CONFIDENCE: " + ModeEvidence.Confidence,
        "SOURCE: " + ModeEvidence.Source,
        "USB VID/PID: " + HardwareId,
        "COM PORT: " + (ComPort.Length == 0 ? "غير ظاهر في وصف PnP" : ComPort),
        "PNP INSTANCE: " + UsbDevice.InstanceId,
        "HARDWARE IDS: " + (UsbDevice.HardwareIds.Count == 0 ? "غير متاحة" : string.Join(" | ", UsbDevice.HardwareIds.Take(8))),
        "DRIVER: " + DriverStatus,
        "LIMITATION: " + ModeEvidence.Limitation
    ]);
}

/// <summary>
/// Builds a read-only service-mode inventory from Windows PnP evidence.
/// It intentionally never queries ADB to decide whether a device in boot/service mode exists.
/// </summary>
public static class MaintenanceModeDiscoveryService
{
    private static readonly Regex ComPortPattern = new(@"\bCOM[1-9][0-9]*\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<MaintenanceModeDevice> Discover(IEnumerable<WindowsUsbDevice> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source
            .Select(device =>
            {
                var evidence = DeviceModeClassifier.ClassifyUsb(device);
                var manufacturer = !string.IsNullOrWhiteSpace(device.Manufacturer)
                    ? device.Manufacturer
                    : !string.Equals(device.Identity.Category, "Unknown", StringComparison.OrdinalIgnoreCase)
                        ? device.Identity.Category
                        : "غير معروف";
                var match = ComPortPattern.Match($"{device.FriendlyName} {device.Service}");
                return new MaintenanceModeDevice(
                    device,
                    evidence,
                    manufacturer,
                    match.Success ? match.Value.ToUpperInvariant() : string.Empty);
            })
            .Where(IsMaintenanceCandidate)
            .GroupBy(item => item.UsbDevice.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.IsSamsungDownloadMode ? 0 :
                item.IsFastboot ? 1 : item.IsQualcommEdl ? 2 : item.IsMediaTekService ? 3 : 4)
            .ThenBy(item => item.ManufacturerLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsMaintenanceCandidate(MaintenanceModeDevice item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var device = item.UsbDevice;
        var evidence = item.ModeEvidence;
        var vendor = device.VendorId.Trim();
        var text = string.Join(" ", device.Manufacturer, device.FriendlyName, device.DeviceClass,
            device.Service, string.Join(" ", device.HardwareIds));

        return item.IsSamsungDownloadMode ||
               item.IsFastboot ||
               item.IsQualcommEdl ||
               item.IsMediaTekService ||
               (vendor.Equals("04E8", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("download mode", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("cdc", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("modem", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("serial", StringComparison.OrdinalIgnoreCase) ||
                 device.Service.Equals("usbser", StringComparison.OrdinalIgnoreCase))) ||
               (vendor.Equals("05C6", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("qdl", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("qualcomm", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("9008", StringComparison.OrdinalIgnoreCase))) ||
               (vendor.Equals("0E8D", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("mediatek", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("preloader", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("brom", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("vcom", StringComparison.OrdinalIgnoreCase))) ||
               evidence.Mode.Contains("Download Mode", StringComparison.OrdinalIgnoreCase) ||
               evidence.Mode.Contains("EDL", StringComparison.OrdinalIgnoreCase);
    }
}
