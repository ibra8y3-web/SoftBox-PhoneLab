using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DiagnosticEngineTests
{
    [Fact]
    public void ReadSystemPropertiesAreBlueDataReadNotGreenPass()
    {
        var device = new DeviceSnapshot
        {
            Manufacturer = "Google",
            Model = "Pixel",
            Soc = "Tensor",
            CpuDescription = "Tensor",
            RamGiB = 8,
            BatteryPercent = 77
        };

        var results = DiagnosticEngine.Build(device, null);

        Assert.Equal(DiagnosticStatus.DataRead, results.Single(x => x.Id == "processor").Status);
        Assert.Equal(DiagnosticStatus.DataRead, results.Single(x => x.Id == "ram").Status);
        Assert.Equal(DiagnosticStatus.NotTested, results.Single(x => x.Id == "pmic").Status);
    }

    [Fact]
    public void AgentFailureIsMappedToRedFailure()
    {
        var agent = new AgentSnapshot
        {
            Tests = new Dictionary<string, AgentTestResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["speaker"] = new() { Status = "failed", Evidence = "User reported no audible output." }
            }
        };

        var results = DiagnosticEngine.Build(null, agent);

        Assert.Equal(DiagnosticStatus.Failed, results.Single(x => x.Id == "speaker").Status);
    }

    [Fact]
    public void TouchPassDoesNotFalselyMarkDisplayAsPassed()
    {
        var agent = new AgentSnapshot
        {
            Tests = new Dictionary<string, AgentTestResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["touch"] = new() { Status = "passed", Evidence = "Grid 40/40." }
            }
        };

        var results = DiagnosticEngine.Build(null, agent);

        Assert.Equal(DiagnosticStatus.Pass, results.Single(x => x.Id == "touch").Status);
        Assert.Equal(DiagnosticStatus.NotTested, results.Single(x => x.Id == "display").Status);
        Assert.Equal(DiagnosticStatus.NotTested, results.Single(x => x.Id == "front-camera").Status);
    }
}
