using SoftBox.PhoneLab.Core.Parsing;
using Xunit;
namespace SoftBox.PhoneLab.Tests;
public sealed class SmartDeviceIdentityParsingTests
{
    [Fact]
    public void UsesSystemFallbacksFromSingleGetpropSnapshot()
    {
        const string properties = """
            [ro.product.system.manufacturer]: [Samsung]
            [ro.product.system.model]: [SM-A315G]
            [ro.product.system.name]: [a31]
            [ro.product.system.device]: [a31]
            """;
        var identity = DevicePropertyParser.ParseIdentityHint(properties);
        Assert.Equal("Samsung", identity.Manufacturer); Assert.Equal("SM-A315G", identity.Model);
        Assert.Equal("a31", identity.Product); Assert.Equal("a31", identity.DeviceCode);
    }
    [Fact]
    public void PrefersMarketNameWhenPresent()
    {
        const string properties = """
            [ro.product.manufacturer]: [Xiaomi]
            [ro.product.marketname]: [Redmi Note 13 Pro]
            [ro.product.model]: [2312DRA50G]
            """;
        var identity = DevicePropertyParser.ParseIdentityHint(properties);
        Assert.Equal("Xiaomi", identity.Manufacturer); Assert.Equal("Redmi Note 13 Pro", identity.Model);
    }
}
