using System.IO;
using SoftBox.PhoneLab.Core.Firmware;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class XiaomiFastbootRomFlashPlanBuilderTests
{
    [Fact]
    public async Task ParsesAndHashesMultipleImagesWithoutExecutingTheBatchFile()
    {
        using var root = new RomFixture();
        root.Write("android-info.txt", "require product=ruby\n");
        root.Write("images/boot.img", "boot bytes");
        root.Write("images/vendor_boot.img", "vendor bytes");
        root.Write("images/super.img", "super bytes");
        root.Write("images/anti_version.txt", "3\n");
        var script = root.Write("flash_all_except_storage.bat",
            "@echo off\nfastboot %* getvar product 2>&1 | findstr /r /c:\"product: *ruby$\"\n" +
            "fastboot %* flash boot images\\boot.img\n" +
            "fastboot %* flash vendor_boot images\\vendor_boot.img\n" +
            "fastboot %* flash super images\\super.img\nfastboot %* reboot\n");

        var plan = await XiaomiFastbootRomFlashPlanBuilder.BuildAsync(script, "ruby");

        Assert.True(plan.CanExecute);
        Assert.Equal(XiaomiRomProductMatchState.Match, plan.ProductMatch);
        Assert.Equal(3, plan.FlashImageCount);
        Assert.False(plan.RequiresDataWipe);
        Assert.Equal(3, plan.FirmwareAntiRollbackIndex);
        Assert.All(plan.Steps.Where(x => x.Kind == XiaomiFastbootRomStepKind.FlashPartition),
            step => Assert.False(string.IsNullOrWhiteSpace(step.Sha256)));
        Assert.DoesNotContain(plan.Steps, x => x.Kind == XiaomiFastbootRomStepKind.WipeUserData);
        Assert.True(File.Exists(script));
    }

    [Fact]
    public async Task WipeRequiresExplicitFlashAllScriptAndIsRepresentedAsSeparateStep()
    {
        using var root = new RomFixture();
        root.Write("images/android-info.txt", "require product=veux\n");
        root.Write("images/boot.img", "image");
        var script = root.Write("flash_all.bat",
            "@echo off\nfastboot %* flash boot images\\boot.img\nfastboot %* -w\nfastboot %* reboot\n");

        var plan = await XiaomiFastbootRomFlashPlanBuilder.BuildAsync(script, "veux");

        Assert.True(plan.CanExecute);
        Assert.True(plan.RequiresDataWipe);
        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(XiaomiFastbootRomStepKind.WipeUserData, plan.Steps[1].Kind);
    }

    [Fact]
    public async Task ExceptStorageScriptRejectsWipeAndUnlockScriptIsAlwaysRejected()
    {
        using var root = new RomFixture();
        root.Write("android-info.txt", "require product=ruby\n");
        root.Write("images/boot.img", "image");
        var exceptStorage = root.Write("flash_all_except_storage.bat",
            "fastboot %* flash boot images\\boot.img\nfastboot %* -w\n");
        var lockScript = root.Write("flash_all_lock.bat", "fastboot %* flash boot images\\boot.img\n");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XiaomiFastbootRomFlashPlanBuilder.BuildAsync(exceptStorage, "ruby"));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XiaomiFastbootRomFlashPlanBuilder.BuildAsync(lockScript, "ruby"));
    }

    [Fact]
    public async Task RejectsProductMismatchUnknownImageAndArbitraryCommands()
    {
        using var root = new RomFixture();
        root.Write("android-info.txt", "require product=ruby\n");
        root.Write("images/boot.img", "image");
        var script = root.Write("flash_all_except_storage.bat",
            "fastboot %* flash boot images\\boot.img\n");

        var mismatched = await XiaomiFastbootRomFlashPlanBuilder.BuildAsync(script, "veux");
        Assert.False(mismatched.CanExecute);
        Assert.Equal(XiaomiRomProductMatchState.Mismatch, mismatched.ProductMatch);

        root.Write("flash_all_except_storage.bat",
            "fastboot %* erase userdata\n");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XiaomiFastbootRomFlashPlanBuilder.BuildAsync(script, "ruby"));
    }

    [Theory]
    [InlineData("../outside.img")]
    [InlineData("C:\\\\outside.img")]
    [InlineData("images\\\\..\\\\outside.img")]
    public async Task RejectsImagePathsOutsideTheRomImagesDirectory(string path)
    {
        using var root = new RomFixture();
        root.Write("android-info.txt", "require product=ruby\n");
        root.Write("images/boot.img", "image");
        var script = root.Write("flash_all_except_storage.bat", "fastboot %* flash boot " + path + "\n");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XiaomiFastbootRomFlashPlanBuilder.BuildAsync(script, "ruby"));
    }

    [Fact]
    public async Task RejectsUnrecognizedFastbootCommandRatherThanPassingItThrough()
    {
        using var root = new RomFixture();
        root.Write("android-info.txt", "require product=ruby\n");
        var script = root.Write("flash_all_except_storage.bat", "fastboot %* oem device-info\n");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XiaomiFastbootRomFlashPlanBuilder.BuildAsync(script, "ruby"));
    }

    [Fact]
    public async Task UserdataImageIsMarkedAsDestructiveAndAllowedOnlyInCleanFlashScript()
    {
        using var root = new RomFixture();
        root.Write("android-info.txt", "require product=ruby\n");
        root.Write("images/boot.img", "boot");
        root.Write("images/userdata.img", "userdata-image");
        var clean = root.Write("flash_all.bat",
            "fastboot %* flash boot images\\boot.img\n" +
            "fastboot %* flash userdata images\\userdata.img\n");
        var preserve = root.Write("flash_all_except_storage.bat",
            "fastboot %* flash userdata images\\userdata.img\n");

        var plan = await XiaomiFastbootRomFlashPlanBuilder.BuildAsync(clean, "ruby");

        Assert.True(plan.RequiresDataWipe);
        Assert.Contains(plan.Steps, step => step.Kind == XiaomiFastbootRomStepKind.FlashUserData);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XiaomiFastbootRomFlashPlanBuilder.BuildAsync(preserve, "ruby"));
    }

    [Theory]
    [InlineData("erase")]
    [InlineData("format")]
    public async Task UserdataEraseAndFormatAreExplicitDestructivePlanSteps(string verb)
    {
        using var root = new RomFixture();
        root.Write("android-info.txt", "require product=ruby\n");
        root.Write("images/boot.img", "boot");
        var script = root.Write("flash_all.bat",
            "fastboot %* flash boot images\\boot.img\nfastboot %* " + verb + " userdata\\n");

        var plan = await XiaomiFastbootRomFlashPlanBuilder.BuildAsync(script, "ruby");
        var wipe = Assert.Single(plan.Steps.Where(step => step.Kind == XiaomiFastbootRomStepKind.WipeUserData));

        Assert.True(plan.RequiresDataWipe);
        Assert.Equal(verb, wipe.CommandVerb);
        Assert.Equal("userdata", wipe.Partition);
    }

    private sealed class RomFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "softbox-xiaomi-flash-plan-" + Guid.NewGuid().ToString("N"));

        public RomFixture() => Directory.CreateDirectory(Root);

        public string Write(string relativePath, string content)
        {
            var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            return full;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
