using SoftBox.PhoneLab.Core.Firmware;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class MediaTekScatterPackageInspectorTests
{
    [Fact]
    public async Task ParsesScatterAndHashesEveryDownloadImage()
    {
        var root = CreateRoot();
        var scatter = Path.Combine(root, "MT6765_Android_scatter.txt");
        await File.WriteAllBytesAsync(Path.Combine(root, "boot.img"), [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(Path.Combine(root, "recovery.img"), [5, 6, 7, 8]);
        await File.WriteAllTextAsync(scatter, SampleScatter());

        try
        {
            var report = await MediaTekScatterPackageInspector.InspectAsync(scatter);
            Assert.True(report.IsStructurallyValid);
            Assert.Equal("MT6765", report.Platform);
            Assert.Equal(64, report.ScatterSha256.Length);
            Assert.Equal("TestProject", report.Project);
            Assert.Equal(2, report.Partitions.Count);
            Assert.Empty(report.MissingImages);
            Assert.All(report.Partitions, partition => Assert.Equal(64, partition.ImageSha256!.Length));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task MissingImageIsReportedAndPackageIsNotStructurallyValid()
    {
        var root = CreateRoot();
        var scatter = Path.Combine(root, "MT6765_Android_scatter.txt");
        await File.WriteAllBytesAsync(Path.Combine(root, "boot.img"), [1, 2, 3, 4]);
        await File.WriteAllTextAsync(scatter, SampleScatter(missingRecovery: true));
        try
        {
            var report = await MediaTekScatterPackageInspector.InspectAsync(scatter);
            Assert.False(report.IsStructurallyValid);
            Assert.Contains("recovery.img", report.MissingImages);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsScatterImagePathTraversal()
    {
        var root = CreateRoot();
        var scatter = Path.Combine(root, "MT6765_Android_scatter.txt");
        await File.WriteAllTextAsync(scatter, SampleScatter(pathTraversal: true));
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                MediaTekScatterPackageInspector.InspectAsync(scatter));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string SampleScatter(bool missingRecovery = false, bool pathTraversal = false)
    {
        var first = pathTraversal ? "../boot.img" : "boot.img";
        var second = missingRecovery ? "recovery.img" : "recovery.img";
        return """
            - general:
              config_version: V1.1.2
              platform: MT6765
              project: TestProject
            - partition_index: SYS0
              partition_name: boot
              file_name: FIRST_IMAGE
              is_download: true
              type: NORMAL_ROM
              linear_start_addr: 0x0
              physical_start_addr: 0x0
              partition_size: 0x20000
              region: EMMC_USER
            - partition_index: SYS1
              partition_name: recovery
              file_name: SECOND_IMAGE
              is_download: true
              type: NORMAL_ROM
              linear_start_addr: 0x20000
              physical_start_addr: 0x20000
              partition_size: 0x20000
              region: EMMC_USER
            """.Replace("FIRST_IMAGE", first, StringComparison.Ordinal)
               .Replace("SECOND_IMAGE", second, StringComparison.Ordinal);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "softbox-mtk-scatter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
