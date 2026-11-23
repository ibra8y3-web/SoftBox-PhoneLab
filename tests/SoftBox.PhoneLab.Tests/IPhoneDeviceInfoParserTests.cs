using SoftBox.PhoneLab.Core.Diagnostics;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class IPhoneDeviceInfoParserTests
{
    [Fact]
    public void ParsesLockdownXmlPlistAndMasksDeviceIdentity()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>DeviceName</key><string>iPhone Test</string>
              <key>ProductType</key><string>iPhone15,4</string>
              <key>ProductVersion</key><string>18.1</string>
              <key>BuildVersion</key><string>22B83</string>
              <key>ModelNumber</key><string>MTXX3</string>
              <key>HardwareModel</key><string>D84AP</string>
              <key>SerialNumber</key><string>F2L123456789</string>
              <key>TotalDiskCapacity</key><integer>128000000000</integer>
              <key>TotalDataCapacity</key><integer>110000000000</integer>
              <key>AmountDataAvailable</key><integer>42000000000</integer>
              <key>BatteryCurrentCapacity</key><integer>64</integer>
              <key>BatteryIsCharging</key><true/>
            </dict></plist>
            """;

        var result = IPhoneDeviceInfoParser.Parse(xml, "00008030-0012345678901234");

        Assert.Equal("iPhone Test", result.DeviceName);
        Assert.Equal("iPhone15,4", result.ProductType);
        Assert.Equal("18.1", result.ProductVersion);
        Assert.Equal("22B83", result.BuildVersion);
        Assert.Equal("••••6789", result.MaskedSerialNumber);
        Assert.Equal(128000000000, result.TotalDiskCapacityBytes);
        Assert.Equal(42000000000, result.AmountDataAvailableBytes);
        Assert.Equal(64, result.CurrentChargePercent);
        Assert.True(result.IsCharging);
        Assert.DoesNotContain("F2L123456789", result.MaskedSerialNumber);
    }

    [Fact]
    public void DoesNotInterpretCurrentChargeAsBatteryHealth()
    {
        const string xml = """
            <plist version="1.0"><dict>
              <key>BatteryCurrentCapacity</key><integer>17</integer>
            </dict></plist>
            """;

        var result = IPhoneDeviceInfoParser.Parse(xml, "device-identifier-1234");

        Assert.Equal(17, result.CurrentChargePercent);
        Assert.Equal("غير متاح", result.ProductType);
    }

    [Fact]
    public void RejectsMalformedPlistAndExternalEntityDeclarations()
    {
        Assert.Throws<InvalidDataException>(() =>
            IPhoneDeviceInfoParser.Parse("<plist><dict>", "device-1234"));
        Assert.Throws<InvalidDataException>(() =>
            IPhoneDeviceInfoParser.Parse(
                "<!DOCTYPE plist [<!ENTITY x SYSTEM 'file:///C:/secret'>]><plist><dict><key>DeviceName</key><string>&x;</string></dict></plist>",
                "device-1234"));
    }

    [Fact]
    public void RejectsOversizedPlist()
    {
        Assert.Throws<InvalidDataException>(() =>
            IPhoneDeviceInfoParser.Parse(new string('x', 1024 * 1024 + 1), "device-1234"));
    }

    [Theory]
    [InlineData("00008030-0012345678901234", true)]
    [InlineData("AABBCCDD", true)]
    [InlineData("serial with spaces", false)]
    [InlineData("$(whoami)", false)]
    [InlineData("", false)]
    public void ValidatesAppleDeviceIdentifiersWithoutAcceptingShellSyntax(string id, bool expected)
        => Assert.Equal(expected, SoftBox.PhoneLab.Desktop.Services.IPhoneDeviceDiagnosticsService.IsSafeDeviceIdentifier(id));
}
