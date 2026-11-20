using SoftBox.PhoneLab.Core.Firmware;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class FirmwareLibraryServiceTests
{
    [Fact]
    public async Task ImportCopiesFileAndIndexesMetadataWithExactSha256()
    {
        var root = CreateTempDirectory();
        var source = Path.Combine(root, "source.zip");
        var library = Path.Combine(root, "library");
        await File.WriteAllBytesAsync(source, [0x01, 0x02, 0x03, 0x04, 0x05]);

        try
        {
            var service = new FirmwareLibraryService(library);
            var item = await service.ImportAsync(source, "Samsung", "SM-A525F", "Egypt", "A525FXXU",
                "User-selected local file", "Test import");
            var all = await service.GetItemsAsync();
            var verify = await service.VerifyAsync(item.Id);

            Assert.Single(all);
            Assert.Equal("Samsung", all[0].Manufacturer);
            Assert.Equal("SM-A525F", all[0].Model);
            Assert.Equal(5L, item.SizeBytes);
            Assert.Matches("^[0-9a-f]{64}$", item.Sha256);
            Assert.True(verify.FileExists);
            Assert.True(verify.SizeMatches);
            Assert.True(verify.Sha256Matches);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyDetectsFirmwareFileTampering()
    {
        var root = CreateTempDirectory();
        var source = Path.Combine(root, "image.img");
        var library = Path.Combine(root, "library");
        await File.WriteAllBytesAsync(source, [0x10, 0x20, 0x30]);

        try
        {
            var service = new FirmwareLibraryService(library);
            var item = await service.ImportAsync(source, "Google", "Pixel 7", "", "factory",
                "Local file", "");
            var stored = service.GetAbsolutePath(item);
            await File.WriteAllBytesAsync(stored, [0xAA, 0xBB, 0xCC, 0xDD]);

            var verify = await service.VerifyAsync(item.Id);
            Assert.True(verify.FileExists);
            Assert.False(verify.SizeMatches);
            Assert.False(verify.Sha256Matches);
            Assert.Contains("اختلف الحجم أو SHA-256", verify.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ImportRejectsUnsupportedFileExtensions()
    {
        var root = CreateTempDirectory();
        var source = Path.Combine(root, "driver.exe");
        await File.WriteAllBytesAsync(source, [0x00]);
        try
        {
            var service = new FirmwareLibraryService(Path.Combine(root, "library"));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.ImportAsync(source, "Vendor", "Model", "", "", "", ""));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-firmware-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
