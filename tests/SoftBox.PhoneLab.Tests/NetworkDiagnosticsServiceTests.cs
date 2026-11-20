using SoftBox.PhoneLab.Core.Network;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class NetworkDiagnosticsServiceTests
{
    [Theory]
    [InlineData("example.com")]
    [InlineData("one.two.example")]
    [InlineData("printer")]
    [InlineData("192.0.2.1")]
    [InlineData("2001:db8::1")]
    public void IsValidHostAcceptsDnsNamesAndIpAddresses(string host)
    {
        Assert.True(NetworkDiagnosticsService.IsValidHost(host));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("example.com?query=1")]
    [InlineData("host with spaces.example")]
    public void IsValidHostRejectsUrlsPathsAndMalformedValues(string host)
    {
        Assert.False(NetworkDiagnosticsService.IsValidHost(host));
    }
}
