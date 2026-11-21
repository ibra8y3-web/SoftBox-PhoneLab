using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using SoftBox.PhoneLab.Core.Firmware;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class XiaomiFastbootRomInspectorTests
{
    [Fact]
    public async Task ReadsProductImagesAndScriptCommandsWithoutExtractingOrExecutingThem()
    {
        var root = CreateTempDirectory();
        var archivePath = Path.Combine(root, "xiaomi-fastboot.tgz");
        try
        {
            CreateRomArchive(archivePath,
                ("android-info.txt", "require product=ruby\n"),
                ("images/boot.img", "boot-image"),
                ("images/vendor_boot.img", "vendor-boot-image"),
                ("flash_all.sh", "#!/bin/sh\nfastboot flash boot images/boot.img\nfastboot flash vendor_boot images/vendor_boot.img\nfastboot erase userdata\nfastboot reboot\n"));

            var report = await XiaomiFastbootRomInspector.InspectAsync(archivePath, "ruby");

            Assert.Equal(XiaomiRomProductMatchState.Match, report.ProductMatch);
            Assert.Contains("ruby", report.RequiredProducts);
            Assert.Contains("boot", report.FlashPartitions);
            Assert.Contains("vendor_boot", report.FlashPartitions);
            Assert.True(report.ContainsEraseOrWipeCommands);
            Assert.False(report.ContainsUnlockCommands);
            Assert.False(report.FlashAllowed);
            Assert.False(report.UnlockAllowed);
            Assert.Contains(report.Findings, item => item.Contains("لم يتم استخراج", StringComparison.Ordinal));
            Assert.True(File.Exists(archivePath));
            Assert.Equal(archivePath, Assert.Single(Directory.EnumerateFiles(root)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ProductMismatchIsReportedAndNeverPermitsFlash()
    {
        var root = CreateTempDirectory();
        var archivePath = Path.Combine(root, "rom.tgz");
        try
        {
            CreateRomArchive(archivePath, ("android-info.txt", "require product=ruby\n"));
            var report = await XiaomiFastbootRomInspector.InspectAsync(archivePath, "veux");
            Assert.Equal(XiaomiRomProductMatchState.Mismatch, report.ProductMatch);
            Assert.False(report.FlashAllowed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task MissingDeviceProductOrPackageRequirementRemainsUnknown()
    {
        var root = CreateTempDirectory();
        var archivePath = Path.Combine(root, "rom.tgz");
        try
        {
            CreateRomArchive(archivePath, ("images/boot.img", "image"));
            var report = await XiaomiFastbootRomInspector.InspectAsync(archivePath);
            Assert.Equal(XiaomiRomProductMatchState.Unknown, report.ProductMatch);
            Assert.Null(report.SelectedDeviceProduct);
            Assert.False(report.FlashAllowed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsUnsafeTarPath()
    {
        var root = CreateTempDirectory();
        var archivePath = Path.Combine(root, "unsafe.tgz");
        try
        {
            CreateRomArchive(archivePath, ("../outside.txt", "not allowed"));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                XiaomiFastbootRomInspector.InspectAsync(archivePath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsUnsupportedArchiveFormat()
    {
        var root = CreateTempDirectory();
        var archivePath = Path.Combine(root, "rom.zip");
        try
        {
            await File.WriteAllTextAsync(archivePath, "not tar");
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                XiaomiFastbootRomInspector.InspectAsync(archivePath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void CreateRomArchive(string path, params (string Name, string Text)[] entries)
    {
        using var output = File.Create(path);
        using var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
        using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var item in entries)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, item.Name)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(item.Text))
            };
            writer.WriteEntry(entry);
        }
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "softbox-xiaomi-rom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
