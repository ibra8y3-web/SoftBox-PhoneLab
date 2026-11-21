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

    [Theory]
    [InlineData("driver.sys", true)]
    [InlineData("driver.inf", true)]
    [InlineData("vendor.dll", true)]
    [InlineData("catalog.cat", true)]
    [InlineData("notes.txt", false)]
    [InlineData("archive.zip", false)]
    public void CatalogMembershipAcceptsOnlyDriverPackageMemberExtensions(string path, bool expected)
        => Assert.Equal(expected, WindowsDriverSignatureVerificationService.IsSupportedCatalogMemberExtension(path));

    [Fact]
    public void CatalogMembershipArgumentsUsePolicyAwareSignToolVerification()
    {
        var arguments = WindowsDriverSignatureVerificationService.BuildCatalogMembershipArguments(
            @"C:\Drivers\oem.cat",
            @"C:\Drivers\driver.sys");

        Assert.Equal(new[]
        {
            "verify", "/v", "/kp", "/c",
            @"C:\Drivers\oem.cat",
            @"C:\Drivers\driver.sys"
        }, arguments);
    }

    [Fact]
    public void VerificationRejectsUnsupportedFileBeforeCallingWindowsTrustProvider()
    {
        var service = new WindowsDriverSignatureVerificationService();
        Assert.Throws<ArgumentException>(() => service.Verify("driver-package.zip"));
    }
}
