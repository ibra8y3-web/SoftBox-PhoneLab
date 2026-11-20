using System.Security.Cryptography;
using SoftBox.PhoneLab.Core.Offline;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class OfflineReadinessServiceTests
{
    [Fact]
    public async Task MissingBundledToolsAreReportedAsMissingNotReady()
    {
        var root = CreateTempDirectory();
        try
        {
            var service = new OfflineReadinessService(root);
            var snapshot = await service.GetSnapshotAsync();

            Assert.Equal(root, snapshot.ApplicationRoot);
            Assert.Equal(7, snapshot.Packages.Count);
            Assert.All(snapshot.Packages, package =>
            {
                Assert.Equal(OfflinePackageAvailability.Missing, package.Availability);
                Assert.Null(package.Sha256);
                Assert.Contains("غير موجود", package.AvailabilityLabelAr);
            });
        }
        finally { DeleteTempDirectory(root); }
    }

    [Fact]
    public async Task ExistingFilesReportTheirActualLengthAndSha256()
    {
        var root = CreateTempDirectory();
        try
        {
            var adbPath = Path.Combine(root, "Tools", "platform-tools", "adb.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(adbPath)!);
            var bytes = new byte[] { 1, 7, 9, 2, 5, 4, 8 };
            await File.WriteAllBytesAsync(adbPath, bytes);

            var snapshot = await new OfflineReadinessService(root).GetSnapshotAsync();
            var adb = Assert.Single(snapshot.Packages, x => x.Id == "adb");

            Assert.Equal(OfflinePackageAvailability.Available, adb.Availability);
            Assert.Equal(bytes.Length, adb.SizeBytes);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), adb.Sha256);
            Assert.NotNull(adb.LastWriteTimeUtc);
        }
        finally { DeleteTempDirectory(root); }
    }

    [Fact]
    public async Task ResourceDirectoriesReportOnlyFilesActuallyPresent()
    {
        var root = CreateTempDirectory();
        try
        {
            var directory = Path.Combine(root, "Data", "device-profiles");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "profile.json"), "{\"maker\":\"Example\"}");
            Directory.CreateDirectory(Path.Combine(root, "Data", "boardmaps"));

            var snapshot = await new OfflineReadinessService(root).GetSnapshotAsync();
            var profiles = Assert.Single(snapshot.DataDirectories, x => x.Id == "device-profiles");
            var boardmaps = Assert.Single(snapshot.DataDirectories, x => x.Id == "boardmaps");

            Assert.True(profiles.Exists);
            Assert.Equal(1, profiles.FileCount);
            Assert.True(profiles.TotalBytes > 0);
            Assert.True(boardmaps.Exists);
            Assert.Equal(0, boardmaps.FileCount);
        }
        finally { DeleteTempDirectory(root); }
    }

    [Fact]
    public async Task PackagePathsAreResolvedRelativeToInstallationRootOnly()
    {
        var root = CreateTempDirectory();
        try
        {
            var nested = Path.Combine(root, "Tools", "scrcpy");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(Path.Combine(nested, "scrcpy.exe"), "test executable bytes");

            var snapshot = await new OfflineReadinessService(root).GetSnapshotAsync();
            var scrcpy = Assert.Single(snapshot.Packages, x => x.Id == "scrcpy");

            Assert.Equal(Path.Combine("Tools", "scrcpy", "scrcpy.exe"), scrcpy.RelativePath);
            Assert.Equal(OfflinePackageAvailability.Available, scrcpy.Availability);
            Assert.Equal(64, scrcpy.Sha256?.Length);
            Assert.DoesNotContain("..", Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(root, scrcpy.RelativePath))));
        }
        finally { DeleteTempDirectory(root); }
    }

    [Fact]
    public async Task SnapshotIncludesRealRuntimeAndNetworkInformationWithoutClaimingInternetAccess()
    {
        var root = CreateTempDirectory();
        try
        {
            var snapshot = await new OfflineReadinessService(root).GetSnapshotAsync();

            Assert.NotEqual("", snapshot.OperatingSystem);
            Assert.NotEqual("", snapshot.Runtime);
            Assert.True(snapshot.CapturedAtUtc <= DateTimeOffset.UtcNow.AddSeconds(1));
            Assert.NotNull(snapshot.NetworkStatusLabel);
            Assert.NotNull(snapshot.StorageAvailableLabel);
            Assert.DoesNotContain("Internet connected", snapshot.NetworkStatusLabel, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteTempDirectory(root); }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SoftBox-Offline-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
