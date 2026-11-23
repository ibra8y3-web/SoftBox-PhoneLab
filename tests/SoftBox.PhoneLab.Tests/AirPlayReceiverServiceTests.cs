using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AirPlayReceiverServiceTests
{
    [Fact]
    public void ReceiverExecutableMustUseTheExpectedOfficialFileName()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "softbox-airplay-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "another-player.exe");
        try
        {
            File.WriteAllBytes(path, [0x4D, 0x5A]);
            Assert.Throws<ArgumentException>(() => AirPlayReceiverService.ValidateReceiverPath(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CorrectReceiverNameMustActuallyExist()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "softbox-airplay-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "airplay-windows.exe");
        try
        {
            Assert.Throws<FileNotFoundException>(() => AirPlayReceiverService.ValidateReceiverPath(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExistingExpectedReceiverPathIsCanonicalized()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "softbox-airplay-valid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "airplay-windows.exe");
        try
        {
            File.WriteAllBytes(path, [0x4D, 0x5A]);
            var actual = AirPlayReceiverService.ValidateReceiverPath(path);
            Assert.Equal(Path.GetFullPath(path), actual);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AirPlayDoesNotClaimSupportOnNonWindowsPlatforms()
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.Throws<PlatformNotSupportedException>(() =>
            AirPlayReceiverService.ValidateReceiverPath(Path.Combine(Path.GetTempPath(), "airplay-windows.exe")));
    }
}
