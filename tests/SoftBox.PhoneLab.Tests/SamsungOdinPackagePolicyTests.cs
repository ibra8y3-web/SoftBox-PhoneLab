using SoftBox.PhoneLab.Core.Firmware;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SamsungOdinPackagePolicyTests
{
    [Fact]
    public void BuildsStableOdinSlotOrderAndAcceptsTarMd5Packages()
    {
        var plan = SamsungOdinPackagePolicy.BuildPlan(
        [
            new(SamsungOdinSlot.CSC, @"C:\firmware\CSC.tar.md5"),
            new(SamsungOdinSlot.AP, @"C:\firmware\AP.tar.md5"),
            new(SamsungOdinSlot.BL, @"C:\firmware\BL.tar.md5"),
            new(SamsungOdinSlot.CP, @"C:\firmware\CP.tar.md5")
        ]);

        Assert.Equal(
            new[] { SamsungOdinSlot.BL, SamsungOdinSlot.AP, SamsungOdinSlot.CP, SamsungOdinSlot.CSC },
            plan.Select(item => item.Slot));
    }

    [Fact]
    public void RequiresApPackage()
    {
        Assert.Throws<InvalidDataException>(() =>
            SamsungOdinPackagePolicy.BuildPlan(
            [
                new(SamsungOdinSlot.BL, @"C:\firmware\BL.tar.md5")
            ]));
    }

    [Fact]
    public void RejectsSelectingCscAndHomeCscTogether()
    {
        Assert.Throws<InvalidDataException>(() =>
            SamsungOdinPackagePolicy.BuildPlan(
            [
                new(SamsungOdinSlot.AP, @"C:\firmware\AP.tar.md5"),
                new(SamsungOdinSlot.CSC, @"C:\firmware\CSC.tar.md5"),
                new(SamsungOdinSlot.HomeCsc, @"C:\firmware\HOME_CSC.tar.md5")
            ]));
    }

    [Fact]
    public void RejectsDuplicateFilesAndUnsupportedExtensions()
    {
        Assert.Throws<InvalidDataException>(() =>
            SamsungOdinPackagePolicy.BuildPlan(
            [
                new(SamsungOdinSlot.AP, @"C:\firmware\AP.tar.md5"),
                new(SamsungOdinSlot.CP, @"C:\firmware\AP.tar.md5")
            ]));

        Assert.Throws<InvalidDataException>(() =>
            SamsungOdinPackagePolicy.BuildPlan(
            [
                new(SamsungOdinSlot.AP, @"C:\firmware\AP.zip")
            ]));
    }

    [Theory]
    [InlineData(@"C:\firmware\AP.tar", true)]
    [InlineData(@"C:\firmware\AP.tar.md5", true)]
    [InlineData(@"C:\firmware\AP.zip", false)]
    [InlineData(@"C:\firmware\AP.md5", false)]
    public void RecognizesOnlyOdinTarPackageExtensions(string path, bool expected)
        => Assert.Equal(expected, SamsungOdinPackagePolicy.IsSupportedPackagePath(path));
}
