namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record DeviceTransportAssessment(
    string DeviceFamily,
    string DetectionConfidence,
    string UsbTransport,
    string ScreenViewingRoute,
    string RemoteControlRoute,
    string Limitation);

/// <summary>
/// Conservative transport selection from actual Windows PnP identity and an independently
/// confirmed ADB authorization state. It never infers a phone model from a VID alone.
/// </summary>
public static class UniversalDeviceTransportEngine
{
    public static DeviceTransportAssessment Assess(
        string? manufacturer,
        string? friendlyName,
        string? vendorId,
        string? productId,
        bool adbAuthorized)
    {
        var text = (manufacturer + " " + friendlyName).Trim();
        var vendor = (vendorId ?? string.Empty).Trim().ToUpperInvariant();
        var product = (productId ?? string.Empty).Trim().ToUpperInvariant();
        var id = vendor + ":" + product;

        if (vendor == "05AC" || text.Contains("Apple", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("iPad", StringComparison.OrdinalIgnoreCase))
        {
            return new DeviceTransportAssessment(
                "Apple iPhone / iPad (likely)",
                "USB vendor/manufacturer hint; exact model is not proven",
                "Apple Devices app / Apple Mobile Device USB driver; trust the PC on the device",
                "AirPlay receiver over the same LAN (view only)",
                "No remote touch/control channel is provided by the AirPlay receiver",
                "USB detection does not unlock or mirror iOS. Install Apple Devices from Apple/Microsoft Store and approve Trust. AirPlay requires a separate receiver session.");
        }

        if (id == "05C6:9008")
            return new DeviceTransportAssessment(
                "Qualcomm emergency-mode indicator",
                "Known VID/PID family hint only",
                "Qualcomm 9008 / EDL indicator",
                "No ordinary screen mirror in this mode",
                "No general Android UI control",
                "This USB mode does not prove the exact phone, programmer authorization or a safe recovery package.");

        if (id == "0E8D:0003")
            return new DeviceTransportAssessment(
                "MediaTek Preloader/BROM indicator",
                "Known VID/PID family hint only",
                "MediaTek Preloader/BROM indicator",
                "No ordinary screen mirror in this mode",
                "No general Android UI control",
                "This USB mode does not prove the exact SoC, signed Download Agent or OEM authorization.");

        var resource = OemUsbDriverCatalog.FindForUsbIdentity(manufacturer, friendlyName, vendor);
        if (resource is not null)
        {
            if (adbAuthorized)
            {
                return new DeviceTransportAssessment(
                    resource.Name,
                    "Manufacturer read from an authorized ADB device or matching USB identity",
                    "ADB authorized",
                    "scrcpy via ADB",
                    "ADB/scrcpy control subject to Android authorization and OEM restrictions",
                    "Supported Android devices may use ADB regardless of brand when the operating system exposes the interface and the user authorizes this PC. This does not guarantee every model or boot mode.");
            }

            return new DeviceTransportAssessment(
                resource.Name,
                "Manufacturer/USB hint; exact model and active Android state are not proven",
                "USB / MTP or OEM USB interface; inspect the Windows PnP status",
                "No screen stream until an actual supported mirroring transport is available",
                "No control until ADB is separately detected and authorized",
                "Install only the model-matched official package if Windows reports a driver problem. MTP/file transfer does not grant ADB access.");
        }

        return new DeviceTransportAssessment(
            "Unknown USB device",
            "Unknown",
            "USB device detected; protocol not identified",
            "No supported screen-view route identified",
            "No control route identified",
            "SoftBox will not guess a vendor, phone model, driver or service protocol from an unknown USB identifier.");
    }
}
