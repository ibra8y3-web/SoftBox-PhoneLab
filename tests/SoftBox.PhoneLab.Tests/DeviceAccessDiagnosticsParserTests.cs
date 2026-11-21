using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceAccessDiagnosticsParserTests
{
    [Fact]
    public void DetectsDeviceOwnerProfileOwnerAndActiveAdminIndicators()
    {
        const string policy =
            "Device Policy Manager state:\n" +
            "  Device Owner: admin=ComponentInfo{com.example.dpc/.AdminReceiver}\n" +
            "  Profile Owner (User 10): admin=ComponentInfo{com.example.work/.AdminReceiver}\n" +
            "  Active admin: ComponentInfo{com.example.dpc/.AdminReceiver}\n";

        var report = DeviceAccessDiagnosticsParser.Parse(
            policy, "mShowingLockscreen=true", "READY", "Example Carrier");

        Assert.True(report.PolicyOutputReadable);
        Assert.True(report.HasDeviceOwner);
        Assert.True(report.HasProfileOwner);
        Assert.True(report.HasActiveAdmin);
        Assert.True(report.HasManagementOwner);
        Assert.Equal(KeyguardVisibility.Visible, report.KeyguardVisibility);
        Assert.Equal("READY", report.SimStateProperty);
        Assert.Equal("Example Carrier", report.NetworkOperatorProperty);
    }

    [Fact]
    public void DoesNotTreatMissingOwnerIndicatorsAsProofThatDeviceIsUnmanaged()
    {
        var report = DeviceAccessDiagnosticsParser.Parse(
            "Device Policy Manager state:\n  Some other platform state\n",
            "mShowingLockscreen=false",
            null,
            null);

        Assert.True(report.PolicyOutputReadable);
        Assert.False(report.HasDeviceOwner);
        Assert.False(report.HasProfileOwner);
        Assert.False(report.HasManagementOwner);
        Assert.Equal(KeyguardVisibility.NotVisible, report.KeyguardVisibility);
        Assert.Empty(report.SimStateProperty);
        Assert.Empty(report.NetworkOperatorProperty);
    }

    [Fact]
    public void MissingOrConflictingKeyguardSignalsRemainUnknown()
    {
        var missing = DeviceAccessDiagnosticsParser.Parse("device policy", "", "", "");
        var conflicting = DeviceAccessDiagnosticsParser.Parse(
            "device policy",
            "mShowingLockscreen=true\nmKeyguardShowing=false",
            "READY",
            "Carrier");

        Assert.Equal(KeyguardVisibility.Unknown, missing.KeyguardVisibility);
        Assert.Equal(KeyguardVisibility.Unknown, conflicting.KeyguardVisibility);
    }

    [Theory]
    [InlineData("Can't find service: device_policy")]
    [InlineData("Permission Denial: can't dump device_policy")]
    [InlineData("")]
    public void UnavailablePolicyOutputIsNotInterpretedAsNoDeviceManagement(string output)
    {
        var report = DeviceAccessDiagnosticsParser.Parse(output, "", "", "");

        Assert.False(report.PolicyOutputReadable);
        Assert.False(report.HasDeviceOwner);
        Assert.False(report.HasProfileOwner);
        Assert.False(report.HasActiveAdmin);
    }

    [Fact]
    public void SimReadyAndCarrierNameDoNotProduceCarrierUnlockVerdict()
    {
        var report = DeviceAccessDiagnosticsParser.Parse(
            "Device Policy Manager state",
            "mShowingLockscreen=false",
            "READY",
            "Carrier");

        Assert.Equal("READY", report.SimStateProperty);
        Assert.Equal("Carrier", report.NetworkOperatorProperty);
        // The report deliberately contains no inferred SIM/network-unlock Boolean.
    }

    [Fact]
    public void PropertyOutputIsBoundedAndControlCharactersAreRemoved()
    {
        var report = DeviceAccessDiagnosticsParser.Parse(
            "Device Policy Manager state",
            "",
            "READY\r\n" + new string('X', 100),
            "Carrier\0Name");

        Assert.DoesNotContain('\r', report.SimStateProperty);
        Assert.DoesNotContain('\n', report.SimStateProperty);
        Assert.True(report.SimStateProperty.Length <= 80);
        Assert.Equal("CarrierName", report.NetworkOperatorProperty);
    }
}
