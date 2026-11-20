using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidNetworkDiagnosticsParserTests
{
    [Fact]
    public void ParsesDefaultRouteIpAddressesAndSystemToggles()
    {
        var result = AndroidNetworkDiagnosticsParser.Parse(
            "device-serial",
            "default via 192.168.1.1 dev wlan0 proto dhcp src 192.168.1.44 metric 600",
            "18: wlan0 inet 192.168.1.44/24 brd 192.168.1.255 scope global wlan0\n18: wlan0 inet6 2001:db8::44/64 scope global temporary",
            "1",
            "0");

        Assert.Equal("192.168.1.1", result.DefaultGateway);
        Assert.Equal("wlan0", result.DefaultInterface);
        Assert.Contains("wlan0: 192.168.1.44/24", result.Ipv4Addresses);
        Assert.Contains("wlan0: 2001:db8::44/64", result.Ipv6Addresses);
        Assert.True(result.WifiEnabled);
        Assert.False(result.AirplaneModeEnabled);
        Assert.Empty(result.UnavailableCommands);
    }

    [Fact]
    public void DoesNotInventValuesWhenAndroidDoesNotExposeThem()
    {
        var result = AndroidNetworkDiagnosticsParser.Parse(
            "device-serial", "", "", "unknown", "", ["wifi-state: permission denied"]);

        Assert.Null(result.DefaultGateway);
        Assert.Null(result.DefaultInterface);
        Assert.Empty(result.Ipv4Addresses);
        Assert.Empty(result.Ipv6Addresses);
        Assert.Null(result.WifiEnabled);
        Assert.Null(result.AirplaneModeEnabled);
        Assert.Contains("wifi-state: permission denied", result.UnavailableCommands);
    }

    [Fact]
    public void IgnoresLoopbackAndInvalidAddressRecords()
    {
        var result = AndroidNetworkDiagnosticsParser.Parse(
            "device-serial",
            "default dev rmnet_data0",
            "1: lo inet 127.0.0.1/8 scope host lo\n2: wlan0 inet 10.0.0.8/24 scope global wlan0\n3: wlan0 inet6 fe80::123/64 scope link",
            "0",
            "0");

        Assert.Equal("rmnet_data0", result.DefaultInterface);
        Assert.Null(result.DefaultGateway);
        Assert.Single(result.Ipv4Addresses);
        Assert.Equal("wlan0: 10.0.0.8/24", result.Ipv4Addresses[0]);
        Assert.Empty(result.Ipv6Addresses);
    }

    [Fact]
    public void RejectsUnsafeDeviceSerials()
    {
        Assert.Throws<ArgumentException>(() => AndroidNetworkDiagnosticsParser.Parse(
            "serial;rm -rf", "", "", "1", "0"));
    }
}
