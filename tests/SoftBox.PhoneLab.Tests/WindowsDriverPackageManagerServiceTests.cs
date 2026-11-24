using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class WindowsDriverPackageManagerServiceTests
{
    [Theory]
    [InlineData("oem0.inf", true)]
    [InlineData("OEM42.INF", true)]
    [InlineData("oem123456.inf", true)]
    [InlineData("oem.inf", false)]
    [InlineData("oem-2.inf", false)]
    [InlineData("C:\\temp\\oem1.inf", false)]
    [InlineData("oem2.inf /force", false)]
    [InlineData("oem2.inf;shutdown", false)]
    public void PublishedInfNameValidationIsStrict(string value, bool expected)
        => Assert.Equal(expected, WindowsDriverPackageManagerService.IsValidPublishedInfName(value));

    [Fact]
    public void DeleteArgumentsNeverContainForce()
    {
        var args = WindowsDriverPackageManagerService.BuildDeleteDriverArguments("oem42.inf");
        Assert.Equal(new[] { "/delete-driver", "oem42.inf", "/uninstall" }, args);
        Assert.DoesNotContain("/force", args);
    }

    [Fact]
    public void AddDriverArgumentsUseOneExactInfPath()
    {
        var args = WindowsDriverPackageManagerService.BuildAddDriverArguments(@"C:\OEM package\phone.inf");
        Assert.Equal("/add-driver", args[0]);
        Assert.Equal(@"C:\OEM package\phone.inf", args[1]);
        Assert.Equal("/install", args[2]);
        Assert.Equal(3, args.Count);
    }

    [Fact]
    public void ExportArgumentsRequireAValidPublishedName()
    {
        var args = WindowsDriverPackageManagerService.BuildExportDriverArguments(
            "oem9.inf", @"C:\Driver backups");
        Assert.Equal(new[] { "/export-driver", "oem9.inf", @"C:\Driver backups" }, args);
        Assert.Throws<ArgumentException>(() =>
            WindowsDriverPackageManagerService.BuildExportDriverArguments("anything.inf", @"C:\Backup"));
    }
}
