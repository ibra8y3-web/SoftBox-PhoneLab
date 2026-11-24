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
    private static readonly IReadOnlyDictionary<string, string> KnownVendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["05AC"] = "Apple", ["18D1"] = "Google", ["8087"] = "Intel", ["0BB4"] = "HTC",
        ["04E8"] = "Samsung", ["22B8"] = "Motorola", ["1004"] = "LG", ["12D1"] = "Huawei",
        ["0502"] = "Acer", ["0FCE"] = "Sony Ericsson", ["054C"] = "Sony", ["413C"] = "Dell",
        ["0955"] = "NVIDIA", ["04DD"] = "Sharp", ["19D2"] = "ZTE", ["0482"] = "Kyocera",
        ["10A9"] = "Pantech", ["05C6"] = "Qualcomm", ["0930"] = "Toshiba", ["0B05"] = "ASUS",
        ["2006"] = "Lenovo Mobile", ["17EF"] = "Lenovo", ["04C5"] = "Fujitsu", ["22D9"] = "OPPO",
        ["2717"] = "Xiaomi", ["0E8D"] = "MediaTek", ["19D1"] = "BYD"
    };

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

        if (vendor is not null && KnownVendors.TryGetValue(vendor, out var vendorName))
            return new UsbHardwareIdentity(vendor, product ?? "----", vendorName,
                "Vendor identified; USB mode not determined",
                "The VID is a vendor hint only. It does not identify the exact handset model, chipset, board revision, driver match, protocol mode or firmware compatibility.");

        return new UsbHardwareIdentity(
            vendor ?? "----", product ?? "----", "Unknown", "Unknown USB mode",
            "No exact VID/PID or vendor-only profile is registered. Do not infer chipset, model or supported operations from an unknown ID.");
    }

    private static string? NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length == 4 && normalized.All(Uri.IsHexDigit) ? normalized : null;
    }
}
