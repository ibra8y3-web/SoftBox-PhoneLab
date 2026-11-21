using SoftBox.PhoneLab.Core.Fastboot;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class XiaomiFastbootDiagnosticsPolicyTests
{
    [Theory]
    [InlineData("Xiaomi", true)]
    [InlineData("Redmi", true)]
    [InlineData("POCO", true)]
    [InlineData("Samsung", false)]
    [InlineData("Google", false)]
    public void RecognizesOnlyXiaomiDeviceFamilies(string manufacturer, bool expected)
        => Assert.Equal(expected, XiaomiFastbootDiagnosticsPolicy.IsXiaomiManufacturer(manufacturer));

    [Fact]
    public void AcceptsOnlyTheSelectedFastbootSerialAndNeverAllowsFlash()
    {
        var parsed = FastbootVariableParser.Parse(
            "(bootloader) product: ruby\n" +
            "(bootloader) unlocked: no\n" +
            "(bootloader) current-slot: a\n" +
            "(bootloader) anti: 4");

        var report = XiaomiFastbootDiagnosticsPolicy.Evaluate(
            "Xiaomi", "ABC123", "ABC123", "product: ruby", parsed);

        Assert.True(report.IsXiaomiDevice);
        Assert.True(report.SelectedSerialMatches);
        Assert.Equal("ruby", report.Product);
        Assert.Contains("anti", report.RollbackVariables.Keys);
        Assert.False(report.FlashAllowed);
    }

    [Fact]
    public void SerialMismatchFailsClosedEvenWhenVendorAndFastbootVariablesArePresent()
    {
        var parsed = FastbootVariableParser.Parse("(bootloader) product: ruby\n(bootloader) unlocked: no");
        var report = XiaomiFastbootDiagnosticsPolicy.Evaluate(
            "Redmi", "SELECTED", "OTHER", "product: ruby", parsed);

        Assert.True(report.IsXiaomiDevice);
        Assert.False(report.SelectedSerialMatches);
        Assert.False(report.FlashAllowed);
        Assert.Contains(report.Findings, item => item.Contains("لا يطابق", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesToApplyXiaomiProfileToAnotherBrand()
    {
        var parsed = FastbootVariableParser.Parse("(bootloader) product: sm-a546e");
        var report = XiaomiFastbootDiagnosticsPolicy.Evaluate(
            "Samsung", "ABC123", "ABC123", "", parsed);

        Assert.False(report.IsXiaomiDevice);
        Assert.False(report.FlashAllowed);
    }
}
