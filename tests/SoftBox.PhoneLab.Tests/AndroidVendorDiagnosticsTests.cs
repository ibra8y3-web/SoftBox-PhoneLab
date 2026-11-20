using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidVendorDiagnosticsTests
{
    [Theory]
    [InlineData("samsung", AndroidVendorKind.Samsung)]
    [InlineData("SAMSUNG ELECTRONICS", AndroidVendorKind.Samsung)]
    [InlineData("Xiaomi", AndroidVendorKind.Xiaomi)]
    [InlineData("Redmi", AndroidVendorKind.Xiaomi)]
    [InlineData("POCO", AndroidVendorKind.Xiaomi)]
    [InlineData("Google", AndroidVendorKind.Generic)]
    [InlineData("", AndroidVendorKind.Generic)]
    public void ManufacturerNameMapsToTheCorrectReadOnlyPropertyProfile(string manufacturer, AndroidVendorKind expected)
    {
        Assert.Equal(expected, AndroidDeviceToolsService.NormalizeVendor(manufacturer));
    }
}
