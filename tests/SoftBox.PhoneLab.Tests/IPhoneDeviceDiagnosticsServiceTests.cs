using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class IPhoneDeviceDiagnosticsServiceTests
{
    [Fact]
    public void ResolvesOnlyACompleteToolPairFromTheConfiguredFolder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "softbox-iphone-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "idevice_id.exe"), []);
            Assert.Null(IPhoneDeviceDiagnosticsService.ResolveToolsDirectory(directory));

            File.WriteAllBytes(Path.Combine(directory, "ideviceinfo.exe"), []);
            Assert.Equal(Path.GetFullPath(directory), IPhoneDeviceDiagnosticsService.ResolveToolsDirectory(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("00008030-0012345678901234", true)]
    [InlineData("A1B2C3D4", true)]
    [InlineData("udid with spaces", false)]
    [InlineData("abc;del", false)]
    [InlineData("$(whoami)", false)]
    [InlineData("", false)]
    public void DeviceIdentifierIsStrictlyAllowListed(string value, bool expected)
        => Assert.Equal(expected, IPhoneDeviceDiagnosticsService.IsSafeDeviceIdentifier(value));
}
