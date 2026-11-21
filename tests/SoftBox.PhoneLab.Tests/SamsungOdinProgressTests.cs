using SoftBox.PhoneLab.Core.Firmware;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SamsungOdinProgressTests
{
    [Theory]
    [InlineData("ID:COM7\nPASS!", SamsungOdinExecutionState.Succeeded)]
    [InlineData("ID:COM7\nFAIL! Auth", SamsungOdinExecutionState.Failed)]
    [InlineData("Downloading...\n<ID:0/007>", SamsungOdinExecutionState.Running)]
    [InlineData("Odin window ready\nID:COM7", SamsungOdinExecutionState.Unknown)]
    public void ClassifiesOnlyExplicitOdinResultOrProgressEvidence(
        string visibleText,
        SamsungOdinExecutionState expected)
    {
        Assert.Equal(expected, SamsungOdinUiAutomationService.ClassifyExecutionState(visibleText, null));
    }

    [Fact]
    public void ProgressPercentageCanIndicateRunningWithoutGuessingAResult()
    {
        Assert.Equal(
            SamsungOdinExecutionState.Running,
            SamsungOdinUiAutomationService.ClassifyExecutionState("Odin", 37.5));
    }

    [Fact]
    public void HundredPercentWithoutPassMarkerRemainsUnknown()
    {
        Assert.Equal(
            SamsungOdinExecutionState.Unknown,
            SamsungOdinUiAutomationService.ClassifyExecutionState("Odin", 100));
    }
}
