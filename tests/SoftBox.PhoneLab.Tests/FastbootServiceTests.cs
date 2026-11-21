using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class FastbootServiceTests
{
    [Fact]
    public void DeviceParserReturnsOnlyValidFastbootRowsAndPreservesDetails()
    {
        const string output = """
            < waiting for any device >
            0123456789ABCDEF fastboot usb:1-3
            device.serial-02 fastboot product:oriole model:Pixel_6
            unauthorized-device unauthorized
            bad/serial fastboot usb:2-1
            0123456789ABCDEF fastboot usb:1-3
            """;

        var devices = FastbootService.ParseDevices(output);

        Assert.Equal(2, devices.Count);
        Assert.Equal("0123456789ABCDEF", devices[0].Serial);
        Assert.Equal("usb:1-3", devices[0].Details);
        Assert.Equal("device.serial-02", devices[1].Serial);
        Assert.Equal("product:oriole model:Pixel_6", devices[1].Details);
    }

    [Fact]
    public void RecoveryRebootArgumentBuilderTargetsOnlyTheSelectedFastbootSerial()
    {
        var arguments = FastbootService.BuildRebootRecoveryArguments("FASTBOOT-123");

        Assert.Equal(new[] { "-s", "FASTBOOT-123", "reboot", "recovery" }, arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-s")]
    [InlineData("bad/serial")]
    public void RecoveryRebootArgumentBuilderRejectsInvalidSerials(string serial)
    {
        Assert.Throws<ArgumentException>(() => FastbootService.BuildRebootRecoveryArguments(serial));
    }

    [Fact]
    public void FastbootdRebootArgumentBuilderTargetsOnlyTheSelectedSerial()
    {
        var arguments = FastbootService.BuildRebootFastbootdArguments("FASTBOOT-123");

        Assert.Equal(new[] { "-s", "FASTBOOT-123", "reboot", "fastboot" }, arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-s")]
    [InlineData("bad/serial")]
    public void FastbootdRebootArgumentBuilderRejectsInvalidSerials(string serial)
    {
        Assert.Throws<ArgumentException>(() => FastbootService.BuildRebootFastbootdArguments(serial));
    }

    [Fact]
    public void DeviceParserReturnsEmptyListForNoConnectedFastbootDevice()
    {
        var devices = FastbootService.ParseDevices("< waiting for any device >\n");

        Assert.Empty(devices);
    }

    [Fact]
    public void DeviceParserRejectsUnsafeSerialsAndNonFastbootStates()
    {
        var devices = FastbootService.ParseDevices("""
            bad/serial fastboot
            safe-serial adb
            safe-serial
            """);

        Assert.Empty(devices);
    }

    [Fact]
    public void SingleImageFlashBuilderUsesExactSerialAndRestrictedPartition()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "softbox-fastboot-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        var image = System.IO.Path.Combine(root, "boot.img");
        System.IO.File.WriteAllBytes(image, [1, 2, 3, 4]);
        try
        {
            var arguments = FastbootService.BuildFlashPartitionArguments("FASTBOOT-123", "boot", image);
            Assert.Equal(new[] { "-s", "FASTBOOT-123", "flash", "boot", image }, arguments);
        }
        finally { System.IO.Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("system")]
    [InlineData("userdata")]
    [InlineData("super")]
    [InlineData("boot_a")]
    public void SingleImageFlashBuilderRejectsPartitionsOutsideTheAllowList(string partition)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "softbox-fastboot-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        var image = System.IO.Path.Combine(root, "boot.img");
        System.IO.File.WriteAllBytes(image, [1]);
        try
        {
            Assert.Throws<ArgumentException>(() =>
                FastbootService.BuildFlashPartitionArguments("FASTBOOT-123", partition, image));
        }
        finally { System.IO.Directory.Delete(root, recursive: true); }
    }
}

