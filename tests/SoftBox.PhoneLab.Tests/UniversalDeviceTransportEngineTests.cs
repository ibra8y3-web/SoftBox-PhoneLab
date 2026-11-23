using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class UniversalDeviceTransportEngineTests
{
    [Fact]
    public void AppleUsbIdentityRoutesToAirPlayViewOnlyAndAppleDevices()
    {
        var result = UniversalDeviceTransportEngine.Assess("Apple Inc.", "Apple Mobile Device USB Driver", "05AC", "12A8", false);

        Assert.Contains("Apple", result.DeviceFamily);
        Assert.Contains("AirPlay", result.ScreenViewingRoute);
        Assert.Contains("view only", result.ScreenViewingRoute, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No remote touch", result.RemoteControlRoute);
    }

    [Fact]
    public void AuthorizedSamsungAndroidUsesAdbAndScrcpy()
    {
        var result = UniversalDeviceTransportEngine.Assess("samsung", "SM-A315G", "04E8", "6860", true);

        Assert.Equal("ADB authorized", result.UsbTransport);
        Assert.Equal("scrcpy via ADB", result.ScreenViewingRoute);
        Assert.Contains("control", result.RemoteControlRoute, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecognizedOppoWithoutAdbIsNotShownAsScreenConnected()
    {
        var result = UniversalDeviceTransportEngine.Assess("OPPO", "Android Composite Device", "22D9", "0001", false);

        Assert.Contains("OPPO", result.DeviceFamily);
        Assert.Contains("Miracast", result.ScreenViewingRoute);
        Assert.Contains("No ADB control", result.RemoteControlRoute);
    }

    [Fact]
    public void QualcommEdlIdentityIsNotClassifiedAsOrdinaryScreenMirror()
    {
        var result = UniversalDeviceTransportEngine.Assess("", "Qualcomm HS-USB QDLoader", "05C6", "9008", false);

        Assert.Contains("Qualcomm", result.DeviceFamily);
        Assert.Contains("No ordinary screen mirror", result.ScreenViewingRoute);
    }

    [Fact]
    public void UnknownUsbIdentityDoesNotGuessPhoneOrProtocol()
    {
        var result = UniversalDeviceTransportEngine.Assess("Unknown", "USB Composite Device", "FFFF", "0001", false);

        Assert.Equal("Unknown USB device", result.DeviceFamily);
        Assert.Equal("Unknown", result.DetectionConfidence);
        Assert.Contains("will not guess", result.Limitation);
    }
}
