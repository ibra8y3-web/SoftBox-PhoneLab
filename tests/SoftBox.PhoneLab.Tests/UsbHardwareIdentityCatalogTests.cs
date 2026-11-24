using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class UsbHardwareIdentityCatalogTests
{
    [Theory]
    [InlineData("05c6", "9008", "Qualcomm", "Qualcomm EDL / 9008 indicator")]
    [InlineData("0E8D", "0003", "MediaTek", "MediaTek Preloader/BROM family indicator")]
    [InlineData("04E8", "685D", "Samsung", "Samsung Download Mode USB CDC interface")]
    [InlineData("04E8", "6860", "Samsung", "Samsung Android USB interface indicator")]
    [InlineData("18D1", "4EE0", "Google", "Google Fastboot interface indicator")]
    [InlineData("18D1", "4EE7", "Google", "Google ADB interface indicator")]
    public void ClassifiesKnownUsbModeHints(string vendor, string product, string category, string hint)
    {
        var result = UsbHardwareIdentityCatalog.Classify(vendor, product);

        Assert.Equal(category, result.Category);
        Assert.Equal(hint, result.ModeHint);
        Assert.Equal(vendor.ToUpperInvariant(), result.VendorId);
        Assert.Equal(product.ToUpperInvariant(), result.ProductId);
        Assert.NotEmpty(result.Limitation);
    }

    [Fact]
    public void UnknownOrMalformedIdsDoNotGuessAChipset()
    {
        var unknown = UsbHardwareIdentityCatalog.Classify("FFFF", "0001");
        var malformed = UsbHardwareIdentityCatalog.Classify("05C6;9008", "not-an-id");

        Assert.Equal("Unknown", unknown.Category);
        Assert.Equal("Unknown", malformed.Category);
        Assert.Contains("Do not infer", unknown.Limitation);
        Assert.Equal("----", malformed.VendorId);
        Assert.Equal("----", malformed.ProductId);
    }
}
