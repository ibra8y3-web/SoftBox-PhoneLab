using System.IO.Compression;
using SoftBox.PhoneLab.Core.Backup;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class BackupArchiveServiceTests
{
    private readonly BackupArchiveService _service = new();

    [Fact]
    public async Task CreateArchiveAndVerifyChecksEveryFileHashAndLength()
    {
        var root = CreateTempDirectory();
        var archive = Path.Combine(Path.GetTempPath(), $"softbox-backup-{Guid.NewGuid():N}.zip");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "DCIM", "Camera"));
            await File.WriteAllTextAsync(Path.Combine(root, "DCIM", "Camera", "photo.txt"), "actual user file bytes");
            await File.WriteAllBytesAsync(Path.Combine(root, "notes.bin"), [0x10, 0x20, 0x30, 0x40]);

            var manifest = await _service.CreateArchiveAsync(root, archive, "••••1234", "/sdcard/DCIM");
            Assert.Equal(2, manifest.FileCount);
            Assert.Equal(24 + 4, manifest.TotalBytes);
            Assert.All(manifest.Files, file => Assert.Matches("^[0-9a-f]{64}$", file.Sha256));

            var verified = await _service.VerifyArchiveAsync(archive);
            Assert.True(verified.IsValid);
            Assert.Equal(2, verified.VerifiedFiles);
            Assert.Equal(manifest.TotalBytes, verified.VerifiedBytes);
            Assert.Empty(verified.Errors);
            Assert.Contains(verified.Warnings, warning => warning.Contains("غير مشفرة", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            if (File.Exists(archive)) File.Delete(archive);
        }
    }

    [Fact]
    public async Task VerifyArchiveRejectsTamperedEntryContent()
    {
        var root = CreateTempDirectory();
        var archive = Path.Combine(Path.GetTempPath(), $"softbox-tamper-{Guid.NewGuid():N}.zip");
        var modified = Path.Combine(Path.GetTempPath(), $"softbox-modified-{Guid.NewGuid():N}.zip");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "payload.txt"), "original content");
            await _service.CreateArchiveAsync(root, archive, "••••9999", "/sdcard/Documents");

            using (var input = ZipFile.OpenRead(archive))
            using (var output = ZipFile.Open(modified, ZipArchiveMode.Create))
            {
                foreach (var sourceEntry in input.Entries)
                {
                    var newEntry = output.CreateEntry(sourceEntry.FullName);
                    using var destination = newEntry.Open();
                    if (sourceEntry.FullName == "payload.txt")
                    {
                        await destination.WriteAsync("tampered content"u8.ToArray());
                    }
                    else
                    {
                        using var original = sourceEntry.Open();
                        await original.CopyToAsync(destination);
                    }
                }
            }

            var result = await _service.VerifyArchiveAsync(modified);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("SHA-256", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            if (File.Exists(archive)) File.Delete(archive);
            if (File.Exists(modified)) File.Delete(modified);
        }
    }

    [Fact]
    public async Task VerifyArchiveRejectsMissingManifest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"softbox-no-manifest-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("file.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("content");
            }

            await Assert.ThrowsAsync<InvalidDataException>(() => _service.VerifyArchiveAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task CreateArchiveRejectsArchiveInsideSourceDirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "file.txt"), "content");
            var target = Path.Combine(root, "nested", "backup.zip");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.CreateArchiveAsync(root, target, "••••1234", "/sdcard"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-backup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
