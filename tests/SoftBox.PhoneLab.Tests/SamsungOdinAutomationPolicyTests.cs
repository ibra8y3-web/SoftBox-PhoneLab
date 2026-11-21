using SoftBox.PhoneLab.Core.Firmware;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SamsungOdinAutomationPolicyTests
{
    [Fact]
    public void AllowsStartOnlyWhenExactProcessComPortPackagesAndConfirmationsArePresent()
    {
        var evidence = ValidEvidence();

        var decision = SamsungOdinAutomationPolicy.Evaluate(evidence);

        Assert.True(decision.CanInvokeStart);
        Assert.Empty(decision.Blockers);
    }

    [Fact]
    public void RejectsDifferentOdinExecutableEvenWhenWindowLooksCorrect()
    {
        var evidence = ValidEvidence() with { ActualExecutablePath = @"C:\temp\other-odin.exe" };

        var decision = SamsungOdinAutomationPolicy.Evaluate(evidence);

        Assert.False(decision.CanInvokeStart);
        Assert.Contains(decision.Blockers, item => item.Contains("مسار عملية Odin"));
    }

    [Fact]
    public void RejectsAmbiguousOrDisabledStartButton()
    {
        var ambiguous = SamsungOdinAutomationPolicy.Evaluate(ValidEvidence() with { StartButtonCount = 2 });
        var disabled = SamsungOdinAutomationPolicy.Evaluate(ValidEvidence() with { StartButtonEnabled = false });

        Assert.False(ambiguous.CanInvokeStart);
        Assert.False(disabled.CanInvokeStart);
    }

    [Fact]
    public void RejectsMissingComPortOrPackageEvidence()
    {
        var noCom = SamsungOdinAutomationPolicy.Evaluate(ValidEvidence() with { HasConnectedComPort = false });
        var noPackage = SamsungOdinAutomationPolicy.Evaluate(ValidEvidence() with
        {
            VisibleUiText = ["Odin", "Start", "ID:COM7"]
        });

        Assert.False(noCom.CanInvokeStart);
        Assert.False(noPackage.CanInvokeStart);
        Assert.Contains(noPackage.Blockers, item => item.Contains("AP_G990"));
    }

    [Fact]
    public void RejectsStartWhenInvokePatternIsNotAvailable()
    {
        var decision = SamsungOdinAutomationPolicy.Evaluate(
            ValidEvidence() with { StartButtonSupportsInvokePattern = false });

        Assert.False(decision.CanInvokeStart);
        Assert.Contains(decision.Blockers, item => item.Contains("InvokePattern"));
    }

    [Fact]
    public void RequiresCompatibilityAndBackupAcknowledgements()
    {
        var decision = SamsungOdinAutomationPolicy.Evaluate(
            ValidEvidence() with
            {
                OperatorConfirmedCompatibility = false,
                OperatorConfirmedBackup = false
            });

        Assert.False(decision.CanInvokeStart);
        Assert.Contains(decision.Blockers, item => item.Contains("توافق"));
        Assert.Contains(decision.Blockers, item => item.Contains("نسخة احتياطية"));
    }

    private static SamsungOdinAutomationEvidence ValidEvidence()
        => new(
            @"C:\Tools\Odin\Odin.exe",
            @"C:\Tools\Odin\Odin.exe",
            "Odin3 v3.14",
            1,
            true,
            true,
            [@"C:\Firmware\BL_G990.tar.md5", @"C:\Firmware\AP_G990.tar.md5"],
            ["Odin", "ID:COM7", "BL_G990.tar.md5", "AP_G990.tar.md5"],
            true,
            true)
        {
            StartButtonSupportsInvokePattern = true,
            OperatorConfirmedComPortMatchesSelectedDevice = true
        };
}
