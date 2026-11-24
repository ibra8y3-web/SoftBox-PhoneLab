namespace SoftBox.PhoneLab.Core.Models;

public sealed record UsbHardwareIdentity(
    string VendorId,
    string ProductId,
    string Category,
    string ModeHint,
    string Limitation);

/// <summary>
/// Conservative lookup of a few well-known USB VID/PID combinations. These are mode hints,
/// not proof of a model, board revision, signed programmer availability or recovery compatibility.
/// </summary>
public static class UsbHardwareIdentityCatalog
{
    private static readonly IReadOnlyDictionary<string, UsbHardwareIdentity> Known =
        new Dictionary<string, UsbHardwareIdentity>(StringComparer.OrdinalIgnoreCase)
        {
            ["05C6:9008"] = new("05C6", "9008", "Qualcomm", "Qualcomm EDL / 9008 indicator",
                "The USB ID alone does not identify the exact model or authorize a programmer/flash operation."),
            ["0E8D:0003"] = new("0E8D", "0003", "MediaTek", "MediaTek Preloader/BROM family indicator",
                "The USB ID alone does not identify the exact chipset, secure-boot state or compatible DA."),
            ["04E8:685D"] = new("04E8", "685D", "Samsung", "Samsung Download Mode USB CDC interface",
                "This is a Samsung Download-mode USB interface identifier; it does not reveal the exact handset model, CSC, bootloader binary or firmware compatibility."),
            ["04E8:6860"] = new("04E8", "6860", "Samsung", "Samsung Android USB interface indicator",
                "This is a normal Samsung Android USB interface identifier, not evidence of Download Mode or exact handset identity."),
            ["18D1:4EE0"] = new("18D1", "4EE0", "Google", "Google Fastboot interface indicator",
                "This does not prove bootloader unlock state or firmware compatibility."),
            ["18D1:4EE7"] = new("18D1", "4EE7", "Google", "Google ADB interface indicator",
                "ADB authorization must still be granted on the device.")
        };

    public static UsbHardwareIdentity Classify(string? vendorId, string? productId)
    {
        var vendor = NormalizeId(vendorId);
        var product = NormalizeId(productId);
        if (vendor is not null && product is not null &&
            Known.TryGetValue(vendor + ":" + product, out var identity))
            return identity;

        return new UsbHardwareIdentity(
            vendor ?? "----",
            product ?? "----",
            "Unknown",
            "Unknown USB mode",
            "No exact VID/PID profile is registered. Do not infer chipset, model or supported operations from an unknown ID.");
    }

    private static string? NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length == 4 && normalized.All(Uri.IsHexDigit) ? normalized : null;
    }
}
