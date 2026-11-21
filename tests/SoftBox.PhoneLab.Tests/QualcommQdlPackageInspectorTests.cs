using SoftBox.PhoneLab.Core.Diagnostics;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class QualcommQdlPackageInspectorTests
{
    [Fact]
    public async Task InspectsProgrammerXmlImagesAndBuildsExactDryRunArguments()
    {
        var root = CreateRoot();
        var programmer = Path.Combine(root, "prog_firehose_ddr.elf");
        var image = Path.Combine(root, "boot.img");
        var raw = Path.Combine(root, "rawprogram0.xml");
        var patch = Path.Combine(root, "patch0.xml");
        await File.WriteAllBytesAsync(programmer, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(image, [5, 6, 7, 8, 9]);
        await File.WriteAllTextAsync(raw,
            "<data><program filename=\"boot.img\" label=\"boot\" physical_partition_number=\"0\" start_sector=\"0\" num_partition_sectors=\"8\" SECTOR_SIZE_IN_BYTES=\"512\" /></data>");
        await File.WriteAllTextAsync(patch,
            "<data><patch filename=\"boot.img\" physical_partition_number=\"0\" start_sector=\"0\" byte_offset=\"0\" size_in_bytes=\"4\" value=\"0x00\" /></data>");

        try
        {
            var report = await QualcommQdlPackageInspector.InspectAsync(root, programmer, [raw], [patch]);

            Assert.True(report.IsStructurallyValid);
            Assert.Single(report.ProgramEntries);
            Assert.Single(report.Images);
            Assert.Equal("boot", report.ProgramEntries[0].PartitionLabel);
            Assert.Equal(64, report.ProgrammerSha256.Length);
            Assert.Equal(64, report.Images[0].Sha256.Length);
            Assert.Equal(2, report.XmlFiles.Count);
            Assert.All(report.XmlFiles, xml => Assert.Equal(64, xml.Sha256.Length));

            var args = QualcommQdlPackageInspector.BuildFlashArguments(report, "EDL-123", QualcommQdlStorageType.Ufs, dryRun: true);
            Assert.Equal("--storage=ufs", args[0]);
            Assert.Equal("--dry-run", args[1]);
            Assert.Equal("--serial=EDL-123", args[2]);
            Assert.Equal(programmer, args[3]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsRawProgramPathTraversal()
    {
        var root = CreateRoot();
        var programmer = Path.Combine(root, "prog_firehose.elf");
        var raw = Path.Combine(root, "rawprogram0.xml");
        var patch = Path.Combine(root, "patch0.xml");
        await File.WriteAllBytesAsync(programmer, [1]);
        await File.WriteAllTextAsync(raw,
            "<data><program filename=\"../outside.img\" label=\"boot\" physical_partition_number=\"0\" start_sector=\"0\" num_partition_sectors=\"8\" SECTOR_SIZE_IN_BYTES=\"512\" /></data>");
        await File.WriteAllTextAsync(patch,
            "<data><patch filename=\"boot.img\" physical_partition_number=\"0\" start_sector=\"0\" byte_offset=\"0\" size_in_bytes=\"4\" value=\"0x00\" /></data>");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                QualcommQdlPackageInspector.InspectAsync(root, programmer, [raw], [patch]));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsXmlExternalEntityDeclarations()
    {
        var root = CreateRoot();
        var programmer = Path.Combine(root, "prog_firehose.elf");
        var raw = Path.Combine(root, "rawprogram0.xml");
        var patch = Path.Combine(root, "patch0.xml");
        await File.WriteAllBytesAsync(programmer, [1]);
        await File.WriteAllTextAsync(raw,
            "<!DOCTYPE data [<!ENTITY x SYSTEM \"file:///outside\">]><data>&x;</data>");
        await File.WriteAllTextAsync(patch,
            "<data><patch filename=\"boot.img\" physical_partition_number=\"0\" start_sector=\"0\" byte_offset=\"0\" size_in_bytes=\"4\" value=\"0x00\" /></data>");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                QualcommQdlPackageInspector.InspectAsync(root, programmer, [raw], [patch]));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "softbox-qdl-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
