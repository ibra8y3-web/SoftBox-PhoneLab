using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class MaintenanceModeDiscoveryServiceTests
{
    [Fact]
    public void DiscoversSamsungDownloadModeWithoutAnyAdbRecord()
    {
        var devices = new[]
        {
            Create("04E8", "685D", "Samsung", "SAMSUNG Mobile USB CDC Composite Device", "usbser")
        };

        var found = MaintenanceModeDiscoveryService.Discover(devices);

        var device = Assert.Single(found);
        Assert.True(device.IsSamsungDownloadMode);
        Assert.Equal("Samsung", device.ManufacturerLabel);
        Assert.Equal("04E8:685D", device.HardwareId);
    }

    [Fact]
    public void DoesNotListSamsungNormalMtpInterfaceAsAMaintenanceMode()
    {
        var devices = new[]
        {
            Create("04E8", "6860", "Samsung", "SAMSUNG Mobile USB Composite Device", "")
        };

        var found = MaintenanceModeDiscoveryService.Discover(devices);

        Assert.Empty(found);
    }

    [Theory]
    [InlineData("05C6", "9008", "", "Qualcomm HS-USB QDLoader 9008", true, false, false)]
    [InlineData("0E8D", "0003", "MediaTek", "MediaTek PreLoader USB VCOM", false, false, true)]
    [InlineData("18D1", "4EE0", "Google", "Android Bootloader Interface", false, true, false)]
    public void DetectsOtherServiceModeFamilies(
        string vendor, string product, string manufacturer, string name,
        bool qcom, bool fastboot, bool mtk)
    {
        var found = MaintenanceModeDiscoveryService.Discover(
            [Create(vendor, product, manufacturer, name, "")]);

        var device = Assert.Single(found);
        Assert.Equal(qcom, device.IsQualcommEdl);
        Assert.Equal(fastboot, device.IsFastboot);
        Assert.Equal(mtk, device.IsMediaTekService);
    }

    [Fact]
    public void DiscoversHuaweiServiceInterfaceAsReadOnlyWhenAdbIsAbsent()
    {
        var found = MaintenanceModeDiscoveryService.Discover(
            [Create("12D1", "FFFF", "Huawei", "HUAWEI Emergency Download Interface", "")]);

        var device = Assert.Single(found);
        Assert.True(device.IsUnclassifiedVendorServiceMode);
        Assert.False(device.IsSamsungDownloadMode);
        Assert.False(device.IsQualcommEdl);
        Assert.False(device.IsMediaTekService);
    }

    [Fact]
    public void DiscoversAppleDfuEvidenceWithoutOfferingAnAndroidTool()
    {
        var found = MaintenanceModeDiscoveryService.Discover(
            [Create("05AC", "1227", "Apple Inc.", "Apple Mobile Device USB DFU", "")]);

        var device = Assert.Single(found);
        Assert.True(device.IsAppleRecoveryOrDfu);
        Assert.False(device.IsFastboot);
        Assert.False(device.IsSamsungDownloadMode);
    }

    [Fact]
    public void PreservesWindowsComPortHintWithoutTreatingItAsPhoneSerial()
    {
        var found = MaintenanceModeDiscoveryService.Discover(
            [Create("04E8", "685D", "Samsung", "SAMSUNG Mobile USB Modem (COM7)", "usbser")]);

        var device = Assert.Single(found);
        Assert.Equal("COM7", device.ComPort);
        Assert.DoesNotContain("COM7", device.UsbDevice.InstanceId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IgnoresUnrelatedUnknownUsbDevices()
    {
        var found = MaintenanceModeDiscoveryService.Discover(
            [Create("FFFF", "0001", "Unknown", "USB Composite Device", "")]);

        Assert.Empty(found);
    }

    private static WindowsUsbDevice Create(
        string vendor, string product, string manufacturer, string name, string service)
        => new(
            $@"USB\VID_{vendor}&PID_{product}\DEVICE01",
            name,
            manufacturer,
            "USB",
            service,
            vendor,
            product,
            0,
            "Present",
            UsbHardwareIdentityCatalog.Classify(vendor, product));
}
