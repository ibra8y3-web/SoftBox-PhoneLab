using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceModeClassifierTests
{
    [Theory]
    [InlineData("05C6", "9008", "", "Qualcomm HS-USB QDLoader 9008", "Qualcomm EDL / 9008")]
    [InlineData("0E8D", "0003", "MediaTek", "MediaTek PreLoader USB VCOM", "MediaTek Preloader")]
    [InlineData("04E8", "1234", "Samsung", "Samsung Download Mode", "Samsung Download Mode")]
    [InlineData("18D1", "1234", "Google", "Android Bootloader Interface", "Android Bootloader / Fastboot")]
    [InlineData("05AC", "0001", "Apple Inc.", "Apple Mobile Device USB Driver", "Apple USB interface")]
    public void ClassifiesKnownUsbModeHintsWithoutPromotingThemToExactIdentity(
        string vendor, string product, string manufacturer, string friendlyName, string expectedMode)
    {
        var device = CreateUsb(vendor, product, manufacturer, friendlyName);

        var result = DeviceModeClassifier.ClassifyUsb(device);

        Assert.Contains(expectedMode, result.Mode, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(result.Confidence));
        Assert.False(string.IsNullOrWhiteSpace(result.Limitation));
    }

    [Theory]
    [InlineData("device", "ADB authorized")]
    [InlineData("unauthorized", "authorization pending")]
    [InlineData("offline", "offline")]
    [InlineData("bootloader", "Fastboot")]
    public void SeparatesAdbTransportStates(string state, string expected)
    {
        var result = DeviceModeClassifier.ClassifyAdbState(state);

        Assert.Contains(expected, result.Mode, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClassifiesSamsungModemDescriptorAsServiceHintNotConfirmedDownloadMode()
    {
        var result = DeviceModeClassifier.ClassifyUsb(
            CreateUsb("04E8", "685D", "Samsung", "Samsung Mobile USB Modem"));

        Assert.Contains("service interface", result.Mode, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unconfirmed", result.Mode, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hint", result.Confidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoesNotGuessDownloadModeFromSamsungVendorIdAlone()
    {
        var result = DeviceModeClassifier.ClassifyUsb(
            CreateUsb("04E8", "6860", "Samsung", "Samsung Android USB Composite Device"));

        Assert.DoesNotContain("Download Mode", result.Mode, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hint", result.Confidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownUsbDeviceRemainsUnknown()
    {
        var result = DeviceModeClassifier.ClassifyUsb(
            CreateUsb("FFFF", "0001", "Unknown", "USB Composite Device"));

        Assert.Equal("Unknown USB mode", result.Mode);
        Assert.Equal("Unknown", result.Confidence);
    }

    private static WindowsUsbDevice CreateUsb(
        string vendor, string product, string manufacturer, string friendlyName)
        => new(
            $"USB\\VID_{vendor}&PID_{product}\\TEST",
            friendlyName,
            manufacturer,
            "USB",
            "",
            vendor,
            product,
            0,
            "Present",
            UsbHardwareIdentityCatalog.Classify(vendor, product));
}
