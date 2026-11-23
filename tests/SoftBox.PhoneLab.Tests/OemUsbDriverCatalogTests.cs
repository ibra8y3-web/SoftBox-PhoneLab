using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class OemUsbDriverCatalogTests
{
    [Fact]
    public void CatalogContainsMoreThanFifteenOfficialHttpsResources()
    {
        Assert.Equal(21, OemUsbDriverCatalog.All.Count);
        Assert.All(OemUsbDriverCatalog.All, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Id));
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.True(Uri.TryCreate(item.OfficialUrl, UriKind.Absolute, out var uri));
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            Assert.NotEmpty(item.Aliases);
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
    public void MatchesManufacturerFromWindowsUsbPnPOrVendorId(
        string manufacturer, string friendlyName, string vendorId, string expectedId)
    {
        var result = OemUsbDriverCatalog.FindForUsbIdentity(manufacturer, friendlyName, vendorId);
        Assert.NotNull(result);
        Assert.Equal(expectedId, result!.Id);
    }

    [Fact]
    public void UnknownManufacturerDoesNotGuessADriver()
    {
        Assert.Null(OemUsbDriverCatalog.FindForManufacturer("Unknown Android OEM"));
        Assert.Null(OemUsbDriverCatalog.FindForManufacturer(null));
        Assert.Null(OemUsbDriverCatalog.FindForUsbIdentity("Unknown", "Generic USB", "FFFF"));
    }
}
