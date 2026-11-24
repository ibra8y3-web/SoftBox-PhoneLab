using SoftBox.PhoneLab.Core.Diagnostics;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class IPhoneBatteryDiagnosticsParserTests
{
    [Fact]
    public void ParsesGasGaugeCycleCountAndOlderCapacityRatio()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
              <key>GasGauge</key><dict>
                <key>CycleCount</key><integer>275</integer>
                <key>DesignCapacity</key><integer>2000</integer>
                <key>FullChargeCapacity</key><integer>1600</integer>
                <key>Status</key><string>Success</string>
              </dict>
            </dict></plist>
            """;

        var result = IPhoneBatteryDiagnosticsParser.Parse(xml, "16.7.10");

        Assert.Equal(275, result.CycleCount);
        Assert.Equal(2000, result.DesignCapacity);
        Assert.Equal(1600, result.FullChargeCapacity);
        Assert.Equal(80, result.EstimatedMaximumCapacityPercent);
        Assert.Equal("FullChargeCapacityToDesignCapacityRatio", result.EstimateBasis);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void ParsesPercentageScaleReportedByNewerIosWithoutTreatingItAsMah()
    {
        const string xml = """
            <plist version="1.0"><dict>
              <key>GasGauge</key><dict>
                <key>CycleCount</key><integer>418</integer>
                <key>DesignCapacity</key><integer>100</integer>
                <key>FullChargeCapacity</key><integer>87</integer>
                <key>Status</key><string>Success</string>
              </dict>
            </dict></plist>
            """;

        var result = IPhoneBatteryDiagnosticsParser.Parse(xml, "18.2");

        Assert.Equal(87, result.EstimatedMaximumCapacityPercent);
        Assert.Equal("iOS17PlusReportedPercent", result.EstimateBasis);
        Assert.Equal(418, result.CycleCount);
    }

    [Fact]
    public void DoesNotCalculateHealthWhenDiagnosticsStatusIsNotSuccess()
    {
        const string xml = """
            <plist version="1.0"><dict>
              <key>GasGauge</key><dict>
                <key>CycleCount</key><integer>300</integer>
                <key>DesignCapacity</key><integer>2000</integer>
                <key>FullChargeCapacity</key><integer>1200</integer>
                <key>Status</key><string>GasGaugeUnavailable</string>
              </dict>
            </dict></plist>
            """;

        var result = IPhoneBatteryDiagnosticsParser.Parse(xml, "16.4");

        Assert.Null(result.EstimatedMaximumCapacityPercent);
        Assert.Equal(300, result.CycleCount);
        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public void RejectsMalformedOversizedAndMissingGasGaugePlists()
    {
        Assert.Throws<InvalidDataException>(() => IPhoneBatteryDiagnosticsParser.Parse("<plist>", "17.0"));
        Assert.Throws<InvalidDataException>(() => IPhoneBatteryDiagnosticsParser.Parse(
            new string('x', 256 * 1024 + 1), "17.0"));
        Assert.Throws<InvalidDataException>(() => IPhoneBatteryDiagnosticsParser.Parse(
            "<plist version='1.0'><dict><key>WiFi</key><dict/></dict></plist>", "17.0"));
    }

    [Fact]
    public void NeverCalculatesPercentFromCurrentChargeWhenDesignCapacityIsUnavailable()
    {
        const string xml = """
            <plist version="1.0"><dict>
              <key>GasGauge</key><dict>
                <key>CycleCount</key><integer>100</integer>
                <key>FullChargeCapacity</key><integer>1700</integer>
                <key>Status</key><string>Success</string>
              </dict>
            </dict></plist>
            """;

        var result = IPhoneBatteryDiagnosticsParser.Parse(xml, "16.0");

        Assert.Null(result.EstimatedMaximumCapacityPercent);
        Assert.Equal("Unavailable", result.EstimateBasis);
    }
}
