using SoftBox.PhoneLab.Core.Fastboot;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class XiaomiFastbootFlashPolicyTests
{
    [Fact]
    public void PermitsOnlyExactXiaomiSerialProductUnlockedPartitionAndAcknowledgements()
    {
        var root = CreateRoot();
        var image = Path.Combine(root, "boot.img");
        File.WriteAllBytes(image, [1, 2, 3, 4]);
        try
        {
            var result = XiaomiFastbootFlashPolicy.Evaluate(
                "Xiaomi", "FASTBOOT-123", "FASTBOOT-123", "ruby", "ruby",
                BootloaderLockState.Unlocked, "boot", image, true, true);

            Assert.True(result.CanFlash);
            Assert.Empty(result.Blockers);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LockedBootloaderBlocksFlash()
    {
        var root = CreateRoot();
        var image = Path.Combine(root, "boot.img");
        File.WriteAllBytes(image, [1]);
        try
        {
            var result = XiaomiFastbootFlashPolicy.Evaluate(
                "Xiaomi", "FASTBOOT-123", "FASTBOOT-123", "ruby", "ruby",
                BootloaderLockState.Locked, "boot", image, true, true);

            Assert.False(result.CanFlash);
            Assert.Contains(result.Blockers, item => item.Contains("Bootloader"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ProductMismatchAndUnsupportedPartitionBlockFlash()
    {
        var root = CreateRoot();
        var image = Path.Combine(root, "boot.img");
        File.WriteAllBytes(image, [1]);
        try
        {
            var result = XiaomiFastbootFlashPolicy.Evaluate(
                "Xiaomi", "FASTBOOT-123", "FASTBOOT-123", "ruby", "veux",
                BootloaderLockState.Unlocked, "system", image, true, true);

            Assert.False(result.CanFlash);
            Assert.Contains(result.Blockers, item => item.Contains("Product"));
            Assert.Contains(result.Blockers, item => item.Contains("القسم"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void DifferentSerialBlocksFlashEvenWhenProductMatches()
    {
        var root = CreateRoot();
        var image = Path.Combine(root, "boot.img");
        File.WriteAllBytes(image, [1]);
        try
        {
            var result = XiaomiFastbootFlashPolicy.Evaluate(
                "Xiaomi", "FASTBOOT-123", "FASTBOOT-999", "ruby", "ruby",
                BootloaderLockState.Unlocked, "boot", image, true, true);

            Assert.False(result.CanFlash);
            Assert.Contains(result.Blockers, item => item.Contains("Serial"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ProductWithDifferentPunctuationDoesNotCountAsExactMatch()
    {
        var root = CreateRoot();
        var image = Path.Combine(root, "boot.img");
        File.WriteAllBytes(image, [1]);
        try
        {
            var result = XiaomiFastbootFlashPolicy.Evaluate(
                "Xiaomi", "FASTBOOT-123", "FASTBOOT-123", "ruby-pro", "ruby_pro",
                BootloaderLockState.Unlocked, "boot", image, true, true);

            Assert.False(result.CanFlash);
            Assert.Contains(result.Blockers, item => item.Contains("Product"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void VbmetaIsNotInTheRestrictedFlashPartitionList()
    {
        Assert.DoesNotContain("vbmeta", XiaomiFastbootFlashPolicy.SupportedPartitions);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "softbox-xiaomi-flash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
