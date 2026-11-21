using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class WindowsDriverSignatureVerificationTests
{
    [Theory]
    [InlineData("driver.cat")]
    [InlineData("USB.SYS")]
    [InlineData("vendor.dll")]
    [InlineData("setup.exe")]
    public void SupportsDriverCatalogAndAuthenticodeBinaryExtensions(string path)
        => Assert.True(WindowsDriverSignatureVerificationService.IsSupportedFileExtension(path));

    [Theory]
    [InlineData("firmware.zip")]
    [InlineData("notes.txt")]
    [InlineData("unknown")]
    [InlineData("")]
    public void RejectsNonSignatureFileExtensions(string path)
        => Assert.False(WindowsDriverSignatureVerificationService.IsSupportedFileExtension(path));

    [Fact]
    public void VerificationRejectsUnsupportedFileBeforeCallingWindowsTrustProvider()
    {
        var service = new WindowsDriverSignatureVerificationService();
        Assert.Throws<ArgumentException>(() => service.Verify("driver-package.zip"));
    }
}
