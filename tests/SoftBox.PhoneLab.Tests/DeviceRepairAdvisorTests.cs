using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceRepairAdvisorTests
{
    [Fact]
    public void UnauthorizedAdbProducesConnectionAdviceNotHardwareFailure()
    {
        var report = DeviceRepairAdvisor.Analyze(null, null, null, adbAuthorized: false);

        var item = Assert.Single(report.Advice);
        Assert.Equal("adb-not-authorized", item.Id);
        Assert.Equal(AdviceSeverity.Review, item.Severity);
        Assert.Contains("ليست دليلًا على عطل مادي", item.Limitation);
    }

    [Fact]
    public void HighBatteryTemperatureAndLowStorageProduceEvidenceLinkedAdvice()
    {
        var device = new DeviceSnapshot
        {
            Manufacturer = "TestVendor",
            Model = "Model-A",
            BatteryPercent = 80,
            BatteryTemperatureCelsius = 52.0,
            DataTotalKiB = 1_000_000,
            DataUsedKiB = 970_000
        };

        var report = DeviceRepairAdvisor.Analyze(device, null, null, adbAuthorized: true);

        Assert.Contains(report.Advice, item => item.Id == "battery-temperature-high" && item.Severity == AdviceSeverity.Urgent);
        Assert.Contains(report.Advice, item => item.Id == "storage-critical");
        Assert.All(report.Advice.Where(item => item.Id != "no-observed-issue"), item => Assert.NotEmpty(item.Evidence));
    }

    [Fact]
    public void AgentFailedStatusIsEvidenceNotAnAutomaticRepairVerdict()
    {
        var agent = new AgentSnapshot
        {
            Tests = new Dictionary<string, AgentTestResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["touch"] = new() { Status = "failed", Evidence = "Two areas did not respond." },
                ["camera"] = new() { Status = "review", Evidence = "Permission was not granted." },
                ["microphone"] = new() { Status = "not_tested", Evidence = "" }
            }
        };

        var report = DeviceRepairAdvisor.Analyze(new DeviceSnapshot(), agent, null, adbAuthorized: true);

        Assert.Contains(report.Advice, item => item.Id == "agent-failed-touch" && item.Confidence == AdviceConfidence.TestEvidence);
        Assert.Contains(report.Advice, item => item.Id == "agent-review-camera");
        Assert.DoesNotContain(report.Advice, item => item.Id.Contains("microphone", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingEvidenceDoesNotDeclareDeviceHealthy()
    {
        var report = DeviceRepairAdvisor.Analyze(new DeviceSnapshot(), null, null, adbAuthorized: true);
        var item = Assert.Single(report.Advice);

        Assert.Equal("no-observed-issue", item.Id);
        Assert.Contains("لا يعني", item.Assessment);
        Assert.Contains(report.Limitations, value => value.Contains("Agent", StringComparison.Ordinal));
    }

    [Fact]
    public void FailedComponentDiagnosticIsIncludedWithItsEvidence()
    {
        var diagnostic = new ComponentDiagnostic
        {
            Id = "speaker",
            Name = "Speaker",
            Status = DiagnosticStatus.Failed,
            Evidence = "No sound heard during manual test.",
            Description = "User-confirmed no output."
        };

        var report = DeviceRepairAdvisor.Analyze(new DeviceSnapshot(), null, [diagnostic], adbAuthorized: true);

        Assert.Contains(report.Advice, item => item.Id == "component-speaker");
        Assert.Contains(report.Advice.Single(item => item.Id == "component-speaker").Evidence,
            evidence => evidence.Contains("No sound", StringComparison.Ordinal));
    }
}
