using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class WirelessAdbPairingServiceTests
{
    [Theory]
    [InlineData("192.168.1.20", 37123, "192.168.1.20:37123")]
    [InlineData(" 192.168.1.20 ", 1, "192.168.1.20:1")]
    [InlineData("2001:db8::1", 65535, "[2001:db8::1]:65535")]
    public void FormatEndpointAcceptsIpAddressesAndValidPorts(string host, int port, string expected)
        => Assert.Equal(expected, WirelessAdbPairingService.FormatEndpoint(host, port));

    [Theory]
    [InlineData("phone.local", 37123)]
    [InlineData("", 37123)]
    [InlineData("999.1.1.1", 37123)]
    public void FormatEndpointRejectsHostnamesAndInvalidAddresses(string host, int port)
        => Assert.Throws<ArgumentException>(() => WirelessAdbPairingService.FormatEndpoint(host, port));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void FormatEndpointRejectsOutOfRangePorts(int port)
        => Assert.Throws<ArgumentOutOfRangeException>(() => WirelessAdbPairingService.FormatEndpoint("192.168.1.20", port));

    [Theory]
    [InlineData("123456", true)]
    [InlineData("000001", true)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("12a456", false)]
    [InlineData("１２３４５６", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PairingCodeValidationRequiresExactlySixAsciiDigits(string? code, bool expected)
        => Assert.Equal(expected, WirelessAdbPairingService.IsValidPairingCode(code));
}
