using System.IO.Compression;
using SoftBox.PhoneLab.Core.Support;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SupportBundleServiceTests
{
    [Fact]
    public void RedactSensitiveDataRemovesCommonCredentialsAndDeviceSerials()
    {
        const string input = """
            password=hunter2
            Authorization: Bearer abcdefghijklmnop
            access_token=secret-token-value
            adb_serial=R58N123456A
            normal-status=connected
            """;

        var redacted = SupportBundleService.RedactSensitiveData(input);

        Assert.DoesNotContain("hunter2", redacted);
        Assert.DoesNotContain("abcdefghijklmnop", redacted);
        Assert.DoesNotContain("secret-token-value", redacted);
        Assert.DoesNotContain("R58N123456A", redacted);
        Assert.Contains("[REDACTED]", redacted);
        Assert.Contains("normal-status=connected", redacted);
    }

    [Fact]
    public async Task CreateBundleContainsSystemDiagnosticsAndOnlyRedactedSelectedLogs()
    {
        var root = CreateTempDirectory();
        var log = Path.Combine(root, "app.log");
        var archivePath = Path.Combine(root, "support.zip");
        await File.WriteAllTextAsync(log, "token=private-token\nstatus=ready\nserial=DEVICE-123456");
        try
        {
            var service = new SupportBundleService();
            var result = await service.CreateAsync(archivePath, [log], "User notes password=my-secret");
            Assert.True(File.Exists(archivePath));
            Assert.Equal(1, result.IncludedFileCount);
            Assert.Single(result.Files);
            Assert.Matches("^[0-9a-f]{64}$", result.Files[0].Sha256);

            using var archive = ZipFile.OpenRead(archivePath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "system-diagnostics.txt");
            var logEntry = Assert.Single(archive.Entries, entry => entry.FullName.StartsWith("user-logs/", StringComparison.Ordinal));
            using var reader = new StreamReader(logEntry.Open());
            var content = await reader.ReadToEndAsync();

            Assert.DoesNotContain("private-token", content);
            Assert.DoesNotContain("DEVICE-123456", content);
            Assert.Contains("status=ready", content);
            var diagnostics = ReadEntry(archive, "system-diagnostics.txt");
            Assert.DoesNotContain("my-secret", diagnostics);
            Assert.Contains("[REDACTED]", diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateBundleRejectsUnsupportedFileExtension()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "dump.bin");
        await File.WriteAllBytesAsync(file, [0x01]);
        try
        {
            var service = new SupportBundleService();
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.CreateAsync(Path.Combine(root, "support.zip"), [file]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-support-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
