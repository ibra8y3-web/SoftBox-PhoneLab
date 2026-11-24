using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class WindowsUsbInstanceIdentifierParserTests
{
    [Theory]
    [InlineData(@"USB\VID_04E8&PID_685D\R58M123456A", "R58M123456A")]
    [InlineData(@"USB\VID_05AC&PID_12A8\C39X1234ABCD", "C39X1234ABCD")]
    [InlineData(@"USB\VID_0E8D&PID_0003\MTK-ABC1234", "MTK-ABC1234")]
    public void ReadsSyntacticallyPlausiblePnpInstanceSuffix(string instanceId, string expected)
    {
        Assert.True(WindowsUsbInstanceIdentifierParser.TryExtractInstanceSuffix(instanceId, out var suffix));
        Assert.Equal(expected, suffix);
    }

    [Theory]
    [InlineData(@"USB\VID_04E8&PID_685D\5&2A6C1234&0&0000")]
    [InlineData(@"USB\VID_04E8&PID_685D")]
    [InlineData(@"PCI\VEN_1234&DEV_5678\ABCDEF")]
    [InlineData("")]
    [InlineData(@"USB\VID_04E8&PID_685D\00000000")]
    public void RejectsPortTokensAndNonUsbOrIncompleteInstances(string instanceId)
    {
        Assert.False(WindowsUsbInstanceIdentifierParser.TryExtractInstanceSuffix(instanceId, out var suffix));
        Assert.Equal(string.Empty, suffix);
    }
}
