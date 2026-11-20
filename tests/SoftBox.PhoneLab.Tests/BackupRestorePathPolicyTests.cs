using SoftBox.PhoneLab.Core.Backup;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class BackupRestorePathPolicyTests
{
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute/path.txt")]
    [InlineData("folder/../../escape.txt")]
    [InlineData("folder//empty-segment.txt")]
    [InlineData("folder/./dot.txt")]
    [InlineData("folder\\windows-path.txt")]
    [InlineData("folder/name:stream.txt")]
    [InlineData("CON")]
    [InlineData("folder/COM1.txt")]
    [InlineData("folder/trailing.")]
    [InlineData("folder/trailing ")]
    public void RejectsUnsafeOrWindowsIncompatibleArchivePaths(string path)
    {
        Assert.Throws<InvalidDataException>(() => BackupRestorePathPolicy.ValidateRelativePath(path));
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("DCIM/Camera/photo-01.jpg")]
    [InlineData("Documents/Repair Report.txt")]
    public void AcceptsOrdinaryRelativeFilePaths(string path)
    {
        BackupRestorePathPolicy.ValidateRelativePath(path);
    }

    [Fact]
    public void ResolveInsideDirectoryKeepsFilesUnderStagingRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "softbox-restore-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var resolved = BackupRestorePathPolicy.ResolveInsideDirectory(root, "DCIM/Camera/photo.jpg");
            Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, resolved, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("photo.jpg", resolved, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
