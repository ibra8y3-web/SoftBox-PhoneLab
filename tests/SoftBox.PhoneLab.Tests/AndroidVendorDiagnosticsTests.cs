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
    [InlineData("Google", AndroidVendorKind.GooglePixel)]
    [InlineData("Google Pixel", AndroidVendorKind.GooglePixel)]
    [InlineData("Motorola", AndroidVendorKind.Motorola)]
    [InlineData("Lenovo", AndroidVendorKind.Motorola)]
    [InlineData("OnePlus", AndroidVendorKind.OnePlus)]
    [InlineData("OPPO", AndroidVendorKind.Oppo)]
    [InlineData("vivo", AndroidVendorKind.Vivo)]
    [InlineData("realme", AndroidVendorKind.Realme)]
    [InlineData("Huawei", AndroidVendorKind.Huawei)]
    [InlineData("HONOR", AndroidVendorKind.Huawei)]
    [InlineData("TECNO", AndroidVendorKind.Tecno)]
    [InlineData("Infinix", AndroidVendorKind.Infinix)]
    [InlineData("Sony", AndroidVendorKind.Generic)]
    [InlineData("", AndroidVendorKind.Generic)]
    public void ManufacturerNameMapsToTheCorrectReadOnlyPropertyProfile(string manufacturer, AndroidVendorKind expected)
    {
        Assert.Equal(expected, AndroidDeviceToolsService.NormalizeVendor(manufacturer));
    }
}
