using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class OemUsbDriverCatalogTests
{
    [Fact]
    public void CatalogContainsMoreThanFifteenOfficialHttpsResources()
    {
        Assert.Equal(33, OemUsbDriverCatalog.All.Count);
        Assert.All(OemUsbDriverCatalog.All, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Id));
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.True(Uri.TryCreate(item.OfficialUrl, UriKind.Absolute, out var uri));
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            Assert.NotEmpty(item.Aliases);
            Assert.False(string.IsNullOrWhiteSpace(item.ResourceKind));
        });
    }

    [Theory]
    [InlineData("samsung", "samsung")]
    [InlineData("Samsung Electronics", "samsung")]
    [InlineData("Xiaomi Communications", "xiaomi")]
    [InlineData("Google", "google")]
    [InlineData("Sony Xperia", "sony")]
    [InlineData("HUAWEI", "huawei")]
    [InlineData("Dell Inc.", "dell")]
    [InlineData("Sharp Corporation", "sharp")]
    [InlineData("ZTE Corporation", "zte")]
    [InlineData("Apple Inc.", "apple")]
    [InlineData("OnePlus Technology", "oneplus")]
    [InlineData("OPPO", "oppo")]
    [InlineData("realme", "realme")]
    [InlineData("vivo Mobile", "vivo")]
    [InlineData("HONOR Device Co.", "honor")]
    [InlineData("TECNO Mobile", "tecno")]
    [InlineData("Infinix Mobility", "infinix")]
    [InlineData("Nothing Technology", "nothing")]
    [InlineData("HMD Global", "hmd")]
    [InlineData("TCL Communication", "tcl")]
    [InlineData("Fairphone", "fairphone")]
    public void FindsTheMatchingManufacturerResource(string manufacturer, string expectedId)
    {
        var result = OemUsbDriverCatalog.FindForManufacturer(manufacturer);
        Assert.NotNull(result);
        Assert.Equal(expectedId, result!.Id);
    }

    [Theory]
    [InlineData("", "Android Composite ADB Interface", "04E8", "samsung")]
    [InlineData("Generic MTP", "USB Composite Device", "2717", "xiaomi")]
    [InlineData("", "Unknown USB Device", "12D1", "huawei")]
    [InlineData("Unknown", "USB Device", "0E8D", "mediatek")]
    [InlineData("Unknown", "Sony Mobile", "FFFF", "sony")]
    [InlineData("", "", "0FCE", "sony")]
    [InlineData("", "Apple Mobile Device USB Driver", "05AC", "apple")]
    public void MatchesManufacturerFromWindowsUsbPnPOrVendorId(
        string manufacturer, string friendlyName, string vendorId, string expectedId)
    {
        var result = OemUsbDriverCatalog.FindForUsbIdentity(manufacturer, friendlyName, vendorId);
        Assert.NotNull(result);
        Assert.Equal(expectedId, result!.Id);
    }

    [Fact]
    public void OppoUsbVendorIdResolvesToOfficialOppoSupportPortal()
    {
        var resource = OemUsbDriverCatalog.FindForUsbIdentity(null, null, "22D9");
        Assert.NotNull(resource);
        Assert.Equal("oppo", resource.Id);
        Assert.Equal(Uri.UriSchemeHttps, new Uri(resource.OfficialUrl).Scheme);
    }

    [Fact]
    public void UnknownManufacturerDoesNotGuessADriver()
    {
        Assert.Null(OemUsbDriverCatalog.FindForManufacturer("Unknown Android OEM"));
        Assert.Null(OemUsbDriverCatalog.FindForManufacturer(null));
        Assert.Null(OemUsbDriverCatalog.FindForUsbIdentity("Unknown", "Generic USB", "FFFF"));
    }
}
