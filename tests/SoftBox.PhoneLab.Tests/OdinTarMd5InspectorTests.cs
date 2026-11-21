using System.Formats.Tar;
using System.Security.Cryptography;
using SoftBox.PhoneLab.Core.Firmware;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class OdinTarMd5InspectorTests
{
    [Fact]
    public async Task InspectsTarMd5AndReportsPartitionPayloadsWithoutExtraction()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "AP.tar.md5");
        try
        {
            var tar = CreateTar();
            var md5 = Convert.ToHexString(MD5.HashData(tar)).ToLowerInvariant();
            await File.WriteAllBytesAsync(path, [.. tar, .. System.Text.Encoding.ASCII.GetBytes(md5)]);

            var result = await OdinTarMd5Inspector.InspectAsync(path);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.True(result.IsTarReadable);
            Assert.True(result.HasAppendedMd5);
            Assert.True(result.AppendedMd5Valid);
            Assert.Contains(result.EntryNames, item => item.EndsWith("boot.img.lz4", StringComparison.Ordinal));
            Assert.Contains(result.PartitionPayloads, item => item.Equals("boot.img.lz4", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsTamperedTarMd5()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "BL.tar.md5");
        try
        {
            var tar = CreateTar();
            await File.WriteAllBytesAsync(path, [.. tar, .. System.Text.Encoding.ASCII.GetBytes(new string('0', 32))]);
            var result = await OdinTarMd5Inspector.InspectAsync(path);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, message => message.Contains("MD5", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsTarEntryTraversal()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "unsafe.tar");
        try
        {
            var tar = CreateTar("../outside.img");
            await File.WriteAllBytesAsync(path, tar);
            var result = await OdinTarMd5Inspector.InspectAsync(path);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, message => message.Contains("آمن", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] CreateTar(string fileName = "boot.img.lz4")
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, leaveOpen: true))
        {
            var entry = new UstarTarEntry(TarEntryType.RegularFile, fileName)
            {
                DataStream = new MemoryStream([1, 2, 3, 4, 5])
            };
            writer.WriteEntry(entry);
        }
        return output.ToArray();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-odin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
