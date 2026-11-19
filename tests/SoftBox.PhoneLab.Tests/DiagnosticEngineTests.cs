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
    public void Camera2CaptureResultsAreMappedToEachFacing()
    {
        var agent = new AgentSnapshot
        {
            Cameras =
            [
                new AgentCameraInfo { Id = "0", Facing = "back" },
                new AgentCameraInfo { Id = "1", Facing = "front" }
            ],
            Tests = new Dictionary<string, AgentTestResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["camera_0"] = new() { Status = "passed", Evidence = "JPEG captured from rear route." },
                ["camera_1"] = new() { Status = "failed", Evidence = "Camera2 failed to capture front route." }
            }
        };

        var results = DiagnosticEngine.Build(null, agent);

        Assert.Equal(DiagnosticStatus.Pass, results.Single(x => x.Id == "rear-camera").Status);
        Assert.Equal(DiagnosticStatus.Failed, results.Single(x => x.Id == "front-camera").Status);
    }

    [Fact]
    public void BatteryCapacityEstimateRemainsReviewAndNeverBecomesPass()
    {
        var agent = new AgentSnapshot
        {
            BatteryCapacityEstimateMah = 3200,
            Tests = new Dictionary<string, AgentTestResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["battery_capacity"] = new() { Status = "review", Evidence = "Fuel Gauge estimate only." }
            }
        };

        var results = DiagnosticEngine.Build(null, agent);

        Assert.Equal(DiagnosticStatus.Review, results.Single(x => x.Id == "battery").Status);
    }

    [Fact]
    public void PersistedBatteryCapacityEstimateStaysYellowAfterAgentRestart()
    {
        var agent = new AgentSnapshot { BatteryCapacityEstimateMah = 3150 };

        var result = DiagnosticEngine.Build(null, agent).Single(x => x.Id == "battery");

        Assert.Equal(DiagnosticStatus.Review, result.Status);
        Assert.Contains("3150", result.Evidence);
    }

    [Fact]
    public void CameraFocusIsReportedSeparatelyFromImageCapture()
    {
        var agent = new AgentSnapshot
        {
            Cameras = [new AgentCameraInfo { Id = "0", Facing = "back" }],
            Tests = new Dictionary<string, AgentTestResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["camera_focus_0"] = new() { Status = "review", Evidence = "AF state was not locked." }
            }
        };

        var results = DiagnosticEngine.Build(null, agent);

        Assert.Equal(DiagnosticStatus.Review, results.Single(x => x.Id == "camera-focus").Status);
        Assert.Equal(DiagnosticStatus.DataRead, results.Single(x => x.Id == "rear-camera").Status);
    }

    [Fact]
    public void ExternalBenchMeasurementsPopulateComponentsAsReadDataNotPass()
    {
        var measurement = new ExternalMeasurementSample(
            "BenchFixture", DateTimeOffset.UtcNow, 5.05, 3.86, 0.72, 31.5, 3.63,
            new Dictionary<string, double> { ["VPH_PWR"] = 3.84, ["VSYS"] = 3.79 },
            4180, 82.5);

        var results = DiagnosticEngine.Build(null, null, measurement);

        Assert.Equal(DiagnosticStatus.DataRead, results.Single(x => x.Id == "usb-port").Status);
        Assert.Equal(DiagnosticStatus.Review, results.Single(x => x.Id == "battery").Status);
        Assert.Equal(DiagnosticStatus.DataRead, results.Single(x => x.Id == "pmic").Status);
        Assert.Equal(DiagnosticStatus.DataRead, results.Single(x => x.Id == "charge-ic").Status);
    }

    [Fact]
    public void AndroidHardwareFeatureInventoryIsNotAFunctionalRadioTest()
    {
        var agent = new AgentSnapshot
        {
            HardwareFeatures = ["android.hardware.nfc", "android.hardware.wifi", "android.hardware.bluetooth"]
        };

        var results = DiagnosticEngine.Build(null, agent);

        Assert.Equal(DiagnosticStatus.DataRead, results.Single(x => x.Id == "nfc").Status);
        Assert.Equal(DiagnosticStatus.DataRead, results.Single(x => x.Id == "wifi").Status);
        Assert.Equal(DiagnosticStatus.DataRead, results.Single(x => x.Id == "bluetooth").Status);
        Assert.Equal(DiagnosticStatus.NotTested, results.Single(x => x.Id == "sim").Status);
    }

    [Fact]
    public void FlashlightAndEarpieceRemainSeparateTests()
    {
        var agent = new AgentSnapshot
        {
            Tests = new Dictionary<string, AgentTestResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["flashlight"] = new() { Status = "failed", Evidence = "Torch did not illuminate." },
                ["earpiece"] = new() { Status = "passed", Evidence = "Tone audible." }
            }
        };

        var results = DiagnosticEngine.Build(null, agent);

        Assert.Equal(DiagnosticStatus.Failed, results.Single(x => x.Id == "flashlight").Status);
        Assert.Equal(DiagnosticStatus.Pass, results.Single(x => x.Id == "earpiece").Status);
        Assert.Equal(DiagnosticStatus.NotTested, results.Single(x => x.Id == "speaker").Status);
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
