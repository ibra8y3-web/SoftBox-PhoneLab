using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceCapabilityMatrixServiceTests
{
    [Fact]
    public void VerifiedIdentityAndExactReviewedBoardMapAreMarkedVerified()
    {
        var snapshot = CreateSnapshot();
        var profile = new DeviceProfile
        {
            Id = "samsung-a52",
            Manufacturer = "Samsung",
            MarketingName = "Galaxy A52",
            ModelNumbers = ["SM-A525F"],
            DeviceCodes = ["a52q"],
            IdentitySource = "SoftBox curated profile",
            IdentitySourceUrl = "https://example.com/device-profile",
            IdentityVerified = true
        };
        var match = new DeviceProfileMatch(profile, 120, "مطابقة دقيقة", ["مطابقة رقم الموديل"]);
        var map = new BoardMap
        {
            Id = "samsung-a52-board",
            DisplayName = "Galaxy A52 PCB",
            Manufacturer = "Samsung",
            ModelNumbers = ["SM-A525F"],
            DeviceCodes = ["a52q"],
            SourceName = "Authorized board documentation",
            SourceUri = "https://example.com/board-map",
            VerifiedBy = "Reviewer",
            VerifiedAtUtc = DateTimeOffset.UtcNow,
            AccuracyLevel = "pcb",
            IsVerifiedForModel = true,
            Parts = [new BoardPart { Id = "cpu", Name = "CPU", X = 0, Y = 0, Z = 0 }]
        };

        var report = DeviceCapabilityMatrixService.Evaluate(
            "Samsung Galaxy A52",
            deviceSelected: true,
            adbAuthorized: true,
            snapshot,
            match,
            map);

        Assert.Equal(DeviceCapabilityStatus.Verified, Find(report, "adb-transport").Status);
        Assert.Equal(DeviceCapabilityStatus.Verified, Find(report, "model-identity").Status);
        Assert.Equal(DeviceCapabilityStatus.Verified, Find(report, "android-properties").Status);
        Assert.Equal(DeviceCapabilityStatus.Verified, Find(report, "processor-abi").Status);
        Assert.Equal(DeviceCapabilityStatus.Verified, Find(report, "battery-telemetry").Status);
        Assert.Equal(DeviceCapabilityStatus.Verified, Find(report, "exact-pcb-map").Status);
        Assert.Equal(DeviceCapabilityStatus.Unknown, Find(report, "fastboot-transport").Status);
        Assert.Equal(DeviceCapabilityStatus.Unknown, Find(report, "qualcomm-edl").Status);
        Assert.Equal(DeviceCapabilityStatus.Unknown, Find(report, "mediatek-brom").Status);
        Assert.Equal(DeviceCapabilityStatus.Unknown, Find(report, "firmware-compatibility").Status);
    }

    [Fact]
    public void CommunityIdentityAndGenericBoardMapAreNeverPresentedAsVerified()
    {
        var snapshot = CreateSnapshot();
        var communityProfile = new DeviceProfile
        {
            Id = "community-model",
            Manufacturer = "Samsung",
            MarketingName = "Possible Galaxy Model",
            ModelNumbers = ["SM-A525F"],
            IdentitySource = "Community model list",
            IdentitySourceUrl = "https://example.com/community",
            IdentityVerified = false
        };
        var report = DeviceCapabilityMatrixService.Evaluate(
            "Samsung phone",
            true,
            true,
            snapshot,
            new DeviceProfileMatch(communityProfile, 120, "مرجح", ["Community model code match"]),
            new BoardMap
            {
                Id = "generic-android",
                AccuracyLevel = "generic",
                IsVerifiedForModel = false,
                AccuracyNotice = "Generic educational map only."
            });

        Assert.Equal(DeviceCapabilityStatus.Likely, Find(report, "model-identity").Status);
        Assert.Equal(DeviceCapabilityStatus.Unknown, Find(report, "exact-pcb-map").Status);
        Assert.Contains("Generic", Find(report, "exact-pcb-map").Evidence);
    }

    [Fact]
    public void UnauthorizedOrUnselectedDeviceDoesNotReuseStaleInspectionEvidence()
    {
        var staleSnapshot = CreateSnapshot();
        var staleProfile = new DeviceProfileMatch(
            new DeviceProfile { IdentityVerified = true, ModelNumbers = ["SM-A525F"] },
            120,
            "مطابقة دقيقة",
            ["stale"]);
        var report = DeviceCapabilityMatrixService.Evaluate(
            "Device awaiting permission",
            deviceSelected: true,
            adbAuthorized: false,
            staleSnapshot,
            staleProfile,
            new BoardMap { IsVerifiedForModel = true, AccuracyLevel = "pcb" });

        Assert.Equal(DeviceCapabilityStatus.NeedsAuthorization, Find(report, "adb-transport").Status);
        Assert.Equal(DeviceCapabilityStatus.NeedsAuthorization, Find(report, "model-identity").Status);
        Assert.Equal(DeviceCapabilityStatus.NeedsAuthorization, Find(report, "android-properties").Status);
        Assert.Equal("غير معروف", report.ModelNumber);
        Assert.Equal(DeviceCapabilityStatus.NeedsAuthorization, Find(report, "exact-pcb-map").Status);

        var empty = DeviceCapabilityMatrixService.Evaluate(
            "No selection", false, false, staleSnapshot, staleProfile, null);
        Assert.All(empty.Capabilities, item => Assert.NotEqual(DeviceCapabilityStatus.Verified, item.Status));
    }

    private static DeviceCapabilityAssessment Find(DeviceCapabilityMatrixSnapshot report, string id)
        => Assert.Single(report.Capabilities.Where(item => item.CapabilityId == id));

    private static DeviceSnapshot CreateSnapshot() => new()
    {
        Serial = "ADB-SERIAL",
        Manufacturer = "Samsung",
        Model = "Galaxy A52",
        ModelNumber = "SM-A525F",
        DeviceCode = "a52q",
        Soc = "Qualcomm SM7125",
        CpuAbi = "arm64-v8a",
        AndroidVersion = "13",
        ApiLevel = "33",
        BuildId = "TP1A",
        SecurityPatch = "2024-01-01",
        BatteryPercent = 80,
        BatteryVoltageMillivolts = 4100,
        BatteryTemperatureCelsius = 30.5,
        BatteryHealth = "Good",
        BatteryStatus = "Discharging"
    };
}
