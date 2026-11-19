using SoftBox.PhoneLab.Core.Parsing;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DevicePropertyParserTests
{
    [Fact]
    public void ParsesAndroidProperties()
    {
        const string props = """
            [ro.product.manufacturer]: [Google]
            [ro.product.model]: [Pixel 9]
            [ro.soc.model]: [Tensor G4]
            [ro.build.version.release]: [15]
            """;

        var parsed = DevicePropertyParser.ParseProperties(props);

        Assert.Equal("Google", parsed["ro.product.manufacturer"]);
        Assert.Equal("Pixel 9", parsed["ro.product.model"]);
        Assert.Equal("Tensor G4", parsed["ro.soc.model"]);
    }

    [Fact]
    public void ParsesStorageFromDataMount()
    {
        const string df = """
            Filesystem      1K-blocks    Used Available Use% Mounted on
            /dev/block/dm-2  120000000 45000000 75000000  38% /data
            """;

        var value = DevicePropertyParser.ParseDataStorage(df);

        Assert.Equal<long?>(120000000L, value.Total);
        Assert.Equal<long?>(45000000L, value.Used);
    }

    [Fact]
    public void ParsesBatteryObservationsWithoutClaimingCapacity()
    {
        const string battery = """
            level: 83
            voltage: 4180
            temperature: 315
            health: 2
            status: 2
            """;

        var value = DevicePropertyParser.ParseBattery(battery);

        Assert.Equal<int?>(83, value.Level);
        Assert.Equal<int?>(4180, value.Voltage);
        Assert.Equal<double?>(31.5, value.Temperature);
        Assert.Equal("GOOD reported by Android", value.Health);
        Assert.Equal("Charging", value.Status);
    }

    [Fact]
    public void MissingValuesStayUnavailable()
    {
        var value = DevicePropertyParser.ParseBattery("");

        Assert.Null(value.Level);
        Assert.Null(value.Voltage);
        Assert.Null(value.Temperature);
        Assert.Equal("Unknown", value.Health);
    }
}
