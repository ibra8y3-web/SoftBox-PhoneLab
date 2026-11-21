using SoftBox.PhoneLab.Core.Firmware;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class FirmwareCompatibilityPolicyTests
{
    [Fact]
    public void ExactIdentityAndTrustEvidencePassButDoNotCreateFlashOperation()
    {
        var report = FirmwareCompatibilityPolicy.Evaluate(
            "Samsung", "SM-A546E", "a54x", "s5e8835", "KSA", 3,
            "Samsung", "SM-A546E", "a54x", "s5e8835", "KSA", 3,
            fileIntegrityVerified: true,
            publisherSignatureVerified: true);

        Assert.True(report.CanProceed);
        Assert.All(report.Checks, check => Assert.Equal(FirmwareCompatibilityState.Passed, check.State));
        Assert.Contains(report.Limitations, text => text.Contains("ليست أمر Flash", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Xiaomi", "Samsung", "الشركة المصنعة")]
    [InlineData("SM-A546B", "SM-A546E", "الموديل")]
    [InlineData("INS", "KSA", "المنطقة / CSC")]
    public void ExactIdentityMismatchFailsClosed(string deviceValue, string packageValue, string expectedCheck)
    {
        var manufacturer = expectedCheck == "الشركة المصنعة" ? deviceValue : "Samsung";
        var firmwareManufacturer = expectedCheck == "الشركة المصنعة" ? packageValue : "Samsung";
        var model = expectedCheck == "الموديل" ? deviceValue : "SM-A546E";
        var firmwareModel = expectedCheck == "الموديل" ? packageValue : "SM-A546E";
        var region = expectedCheck == "المنطقة / CSC" ? deviceValue : "KSA";
        var firmwareRegion = expectedCheck == "المنطقة / CSC" ? packageValue : "KSA";
        var report = FirmwareCompatibilityPolicy.Evaluate(
            manufacturer, model, "a54x", "s5e8835", region, 4,
            firmwareManufacturer, firmwareModel, "a54x", "s5e8835", firmwareRegion, 4,
            fileIntegrityVerified: true, publisherSignatureVerified: true);

        Assert.False(report.CanProceed);
        Assert.Contains(report.Checks, check => check.Name == expectedCheck && check.State == FirmwareCompatibilityState.Failed);
    }

    [Fact]
    public void MissingBoardRegionSignatureOrRollbackEvidenceRemainsUnknown()
    {
        var report = FirmwareCompatibilityPolicy.Evaluate(
            "Samsung", "SM-A546E", "a54x", null, null, null,
            "Samsung", "SM-A546E", "a54x", null, null, null,
            fileIntegrityVerified: true,
            publisherSignatureVerified: false);

        Assert.False(report.CanProceed);
        Assert.Contains(report.Checks, check => check.Name == "Board / Hardware SKU" && check.State == FirmwareCompatibilityState.Unknown);
        Assert.Contains(report.Checks, check => check.Name == "Anti-Rollback" && check.State == FirmwareCompatibilityState.Unknown);
        Assert.Contains(report.Checks, check => check.Name == "توقيع الناشر" && check.State == FirmwareCompatibilityState.Unknown);
    }

    [Fact]
    public void OlderRollbackIndexIsRejected()
    {
        var report = FirmwareCompatibilityPolicy.Evaluate(
            "Samsung", "SM-A546E", "a54x", "board", "KSA", 8,
            "Samsung", "SM-A546E", "a54x", "board", "KSA", 7,
            fileIntegrityVerified: true, publisherSignatureVerified: true);
        Assert.False(report.CanProceed);
        Assert.Contains(report.Checks, check => check.Name == "Anti-Rollback" && check.State == FirmwareCompatibilityState.Failed);
    }
}
