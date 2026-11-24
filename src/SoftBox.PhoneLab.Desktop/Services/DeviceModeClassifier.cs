namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record DeviceModeEvidence(
    string Mode,
    string Confidence,
    string Source,
    string Limitation);

/// <summary>
/// Conservative, read-only classification of the USB/ADB mode Windows and Android actually expose.
/// A vendor/product hint is never promoted into a model identity or an authorization claim.
/// </summary>
public static class DeviceModeClassifier
{
    public static DeviceModeEvidence ClassifyUsb(WindowsUsbDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var vendor = Normalize(device.VendorId);
        var product = Normalize(device.ProductId);
        var text = string.Join(" ", device.Manufacturer, device.FriendlyName, device.DeviceClass, device.Service,
            string.Join(" ", device.HardwareIds));

        if (vendor == "05C6" && product == "9008")
            return Hint("Qualcomm EDL / 9008", "VID/PID hint", "Windows USB PnP",
                "This identifies a Qualcomm 9008 USB interface only. It does not prove the handset model, authorized programmer, or that a recovery image is safe.");

        if (vendor == "0E8D" &&
            (Contains(text, "download agent") || Contains(text, "da usb vcom")))
            return Hint("MediaTek Download Agent USB interface (stage hint)", "USB descriptor hint", "Windows USB PnP",
                "Windows exposes a MediaTek Download Agent/VCOM interface. This does not identify the exact SoC, board, signed DA package, authorization state or compatible firmware.");

        if (vendor == "0E8D" &&
            (Contains(text, "preloader") || Contains(text, "brom") || product == "0003"))
        {
            var mode = Contains(text, "brom") || (product == "0003" && !Contains(text, "preloader"))
                ? "MediaTek BROM (USB hint)"
                : "MediaTek Preloader (USB hint)";
            return Hint(mode, "USB descriptor / VID-PID hint", "Windows USB PnP",
                "The interface is a MediaTek service-mode clue. Exact SoC, board, Download Agent compatibility and OEM authorization are not established.");
        }

        if (Contains(text, "download mode") && Contains(text, "samsung"))
            return Hint("Samsung Download Mode", "Explicit Windows descriptor", "Windows USB PnP",
                "Download Mode is inferred only because the Windows descriptor names it. Odin COM-port ownership and exact phone identity must still be matched independently.");

        if (vendor == "04E8" && product == "685D")
            return Hint("Samsung Download Mode (04E8:685D)", "Known Samsung Download-mode VID/PID", "Windows USB PnP",
                "Windows reports Samsung USB VID/PID 04E8:685D, a Samsung Download-mode CDC interface. This does not identify the exact model, CSC, bootloader binary, or prove that Odin has matched the same handset on its COM port.");

        if (vendor == "04E8" &&
            (Contains(text, "modem") || Contains(text, "serial") || Contains(text, "cdc") ||
             string.Equals(device.Service, "usbser", StringComparison.OrdinalIgnoreCase)))
            return Hint("Samsung USB service interface (Download Mode unconfirmed)", "Samsung service-interface hint",
                "Windows USB PnP",
                "A Samsung modem/serial USB interface is present. It may be a service/Download-mode interface, but Odin mode and COM-port ownership are not proved by this descriptor alone.");

        if (Contains(text, "fastboot") || Contains(text, "android bootloader interface"))
            return Hint("Android Bootloader / Fastboot", "Explicit USB descriptor", "Windows USB PnP",
                "The descriptor indicates a bootloader interface. Read product and serial through Fastboot before using any device-specific workflow.");

        if (vendor == "05AC" || Contains(text, "apple") || Contains(text, "iphone") || Contains(text, "ipad"))
        {
            var recovery = Contains(text, "recovery") || Contains(text, "dfu");
            return Hint(recovery ? "Apple Recovery / DFU USB hint" : "Apple USB interface",
                recovery ? "USB descriptor hint" : "Apple vendor/manufacturer hint",
                "Windows USB PnP",
                recovery
                    ? "USB presence does not guarantee that a third-party tool can read a serial or leave Recovery/DFU. Do not infer a normal Lockdown session."
                    : "USB presence does not prove that Apple Lockdown pairing is trusted. iOS data reads may require unlocking and approving Trust This Computer.");
        }

        if (Contains(text, "adb interface") || Contains(text, "android composite"))
            return Hint("Android USB / ADB interface", "USB descriptor hint", "Windows USB PnP",
                "The descriptor indicates an Android USB interface, but ADB authorization must be read separately. Screen lock and USB debugging state are not inferred from the descriptor.");

        if (Contains(text, "mtp") || Contains(text, "portable device"))
            return Hint("MTP / file-transfer interface", "Windows class/name hint", "Windows USB PnP",
                "MTP visibility does not authorize ADB, unlock the device, or expose bootloader/service-mode identifiers.");

        if (!string.Equals(device.Identity.Category, "Unknown", StringComparison.OrdinalIgnoreCase))
            return Hint(device.Identity.ModeHint, "Known VID/PID family hint", "Windows USB PnP",
                "This is a hardware-interface family hint only. Exact model, boot state and supported operations remain unverified.");

        return Hint("Unknown USB mode", "Unknown", "Windows USB PnP",
            "SoftBox cannot infer a phone model, serial, boot mode, driver or service protocol from this USB record.");
    }

    public static DeviceModeEvidence ClassifyAdbState(string? state)
    {
        var normalized = (state ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "device" => Hint("Android OS • ADB authorized", "ADB transport state", "Android Debug Bridge",
                "ADB reports an authorized transport. This does not prove that the screen is unlocked or that every privileged operation is allowed."),
            "unauthorized" => Hint("Android OS • ADB authorization pending", "ADB transport state", "Android Debug Bridge",
                "The phone is visible to ADB but has not authorized this computer. The owner must unlock the device if required and approve the RSA prompt."),
            "offline" => Hint("Android OS • ADB offline", "ADB transport state", "Android Debug Bridge",
                "ADB sees a transport that is not ready. This is not proof of a bootloader or Download Mode session."),
            "bootloader" => Hint("Android Bootloader / Fastboot", "ADB transport state", "Android Debug Bridge",
                "ADB reports bootloader state. Use the Fastboot transport for read-only bootloader variables."),
            _ => Hint("ADB state unknown", "Unknown", "Android Debug Bridge",
                "No recognized ADB transport state was supplied.")
        };
    }

    private static DeviceModeEvidence Hint(string mode, string confidence, string source, string limitation)
        => new(mode, confidence, source, limitation);

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static bool Contains(string value, string needle)
        => value.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
