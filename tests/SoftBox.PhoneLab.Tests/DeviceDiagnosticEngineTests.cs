using SoftBox.PhoneLab.Core.Compatibility;
using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceDiagnosticEngineTests
{
    [Theory]
    [InlineData("Qualcomm Snapdragon 8 Gen 3", "qualcomm-qcom")]
    [InlineData("SM8650", "qualcomm-qcom")]
    [InlineData("MediaTek Dimensity 9300", "mediatek-mtk")]
    [InlineData("MT6895", "mediatek-mtk")]
    [InlineData("Exynos 2400", "samsung-exynos")]
    [InlineData("Google Tensor G4", "google-tensor")]
    [InlineData("Kirin 9010", "huawei-kirin")]
    [InlineData("Unisoc T820", "unisoc-mobile")]
    public void SelectsSpecificEngineFromReportedSoc(string soc, string engineId)
    {
        var plan = DeviceDiagnosticEngine.Build(new DeviceDiagnosticEngineContext(
            Device(soc), null, AdbAuthorized: true, AgentConnected: true, FastbootDetected: false));

        Assert.Equal(engineId, plan.EngineId);
        Assert.Equal(DiagnosticEngineIdentityConfidence.ExactReportedSoc, plan.IdentityConfidence);
    }

    [Fact]
    public void UnverifiedCommunityProfileCannotSelectChipsetEngineWithoutReportedSoc()
    {
        var profile = new DeviceProfile
        {
            Id = "community-phone",
            Manufacturer = "Example",
            MarketingName = "Example Phone",
            HardwareFamily = "Qualcomm Snapdragon 8 Gen 3",
            SocAliases = ["SM8650"],
            IdentityVerified = false
        };
        var plan = DeviceDiagnosticEngine.Build(new DeviceDiagnosticEngineContext(
            Device("غير متاح"), profile, AdbAuthorized: true, AgentConnected: false, FastbootDetected: false));

        Assert.Equal("android-generic", plan.EngineId);
        Assert.Equal(DiagnosticEngineIdentityConfidence.GenericFallback, plan.IdentityConfidence);
        Assert.Contains(plan.Warnings, warning => warning.Contains("identity hint"));
    }

    [Fact]
    public void VerifiedProfileCanSelectEngineWhenPhoneDoesNotExposeSocProperty()
    {
        var profile = new DeviceProfile
        {
            Id = "verified-profile",
            Manufacturer = "Google",
            MarketingName = "Pixel",
            HardwareFamily = "Google Tensor G3",
            IdentityVerified = true
        };
        var plan = DeviceDiagnosticEngine.Build(new DeviceDiagnosticEngineContext(
            Device("غير متاح"), profile, AdbAuthorized: true, AgentConnected: false, FastbootDetected: false));

        Assert.Equal("google-tensor", plan.EngineId);
        Assert.Equal(DiagnosticEngineIdentityConfidence.VerifiedProfileFamily, plan.IdentityConfidence);
    }

    [Fact]
    public void DoesNotElevateAdbToAgentOrFastbootCapabilities()
    {
        var plan = DeviceDiagnosticEngine.Build(new DeviceDiagnosticEngineContext(
            Device("SM8650"), null, AdbAuthorized: true, AgentConnected: false, FastbootDetected: false));

        Assert.True(Assert.Single(plan.Procedures, item => item.Id == "android-properties").CanExecute);
        Assert.False(Assert.Single(plan.Procedures, item => item.Id == "agent-hardware-diagnostics").CanExecute);
        Assert.Equal(DiagnosticProcedureState.NeedsSetup,
            Assert.Single(plan.Procedures, item => item.Id == "agent-hardware-diagnostics").State);
        Assert.False(Assert.Single(plan.Procedures, item => item.Id == "fastboot-read-only").CanExecute);
    }

    [Fact]
    public void ConflictingSoCStringsFallBackToGenericInsteadOfChoosingOneVendor()
    {
        var plan = DeviceDiagnosticEngine.Build(new DeviceDiagnosticEngineContext(
            Device("Qualcomm Snapdragon / MediaTek Dimensity"), null, true, false, false));

        Assert.Equal("android-generic", plan.EngineId);
        Assert.Equal(DiagnosticEngineIdentityConfidence.ConflictingSocEvidence, plan.IdentityConfidence);
        Assert.Contains(plan.Warnings, warning => warning.Contains("more than one SoC family", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsafeSerialCannotUnlockDiagnosticProcedures()
    {
        var plan = DeviceDiagnosticEngine.Build(new DeviceDiagnosticEngineContext(
            Device("SM8650") with { Serial = "--bad;serial" }, null, true, true, true));

        Assert.All(plan.Procedures.Where(item => item.Transport.StartsWith("ADB", StringComparison.Ordinal)),
            item => Assert.False(item.CanExecute));
        Assert.False(Assert.Single(plan.Procedures, item => item.Id == "fastboot-read-only").CanExecute);
        Assert.False(Assert.Single(plan.Procedures, item => item.Id == "agent-hardware-diagnostics").CanExecute);
    }

    [Fact]
    public void SoCEngineNeverGrantsPrivateServiceProtocolOrFirmwareWrite()
    {
        var plan = DeviceDiagnosticEngine.Build(new DeviceDiagnosticEngineContext(
            Device("MT6989"), null, AdbAuthorized: true, AgentConnected: true, FastbootDetected: true));

        Assert.False(Assert.Single(plan.Procedures, item => item.Id == "private-oem-service-transport").CanExecute);
        Assert.False(Assert.Single(plan.Procedures, item => item.Id == "firmware-write").CanExecute);
    }

    [Fact]
    public void AppliesConfiguredBatteryThermalReviewThresholdsToReportedTelemetry()
    {
        var device = Device("SM8650") with { BatteryTemperatureCelsius = 56.2 };
        var plan = DeviceDiagnosticEngine.Build(new DeviceDiagnosticEngineContext(
            device, null, true, false, false));

        Assert.Contains(plan.Warnings, warning => warning.Contains("CRITICAL BATTERY TEMPERATURE REVIEW", StringComparison.Ordinal));
        Assert.Contains("55.0", plan.Warnings.Single(warning => warning.Contains("CRITICAL", StringComparison.Ordinal)));
    }

    [Fact]
    public void RejectsUnsafeOrUnboundedEngineSettings()
    {
        var settings = new DiagnosticEngineSettings
        {
            AdbCommandTimeoutSeconds = 600,
            MaximumOutputBytes = 10_000_000,
            BatteryReviewTemperatureCelsius = double.NaN,
            BatteryCriticalTemperatureCelsius = 46
        };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceDiagnosticEngine.Build(new DeviceDiagnosticEngineContext(
                Device("SM8650"), null, true, false, false, Settings: settings)));
    }

    private static DeviceSnapshot Device(string soc)
        => new()
        {
            Serial = "TEST-DEVICE",
            Manufacturer = "Example",
            ModelNumber = "EX-1",
            Model = "Example",
            Soc = soc
        };
}
