using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class OemUsbDriverCatalogTests
{
    [Fact]
    public void CatalogContainsMoreThanFifteenOfficialHttpsResources()
    {
        Assert.True(OemUsbDriverCatalog.All.Count >= 19);
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

    [Fact]
    public void UnknownManufacturerDoesNotGuessADriver()
    {
        Assert.Null(OemUsbDriverCatalog.FindForManufacturer("Unknown Android OEM"));
        Assert.Null(OemUsbDriverCatalog.FindForManufacturer(null));
    }
}
