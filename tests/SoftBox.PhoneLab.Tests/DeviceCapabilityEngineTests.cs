using SoftBox.PhoneLab.Core.Compatibility;
using SoftBox.PhoneLab.Core.Firmware;
using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceCapabilityEngineTests
{
    [Fact]
    public void ExactModelNumberAndManufacturerResolveVerifiedIdentity()
    {
        var device = Device();
        var result = DeviceCapabilityEngine.ResolveIdentity(device, [Profile()]);

        Assert.Equal(DeviceIdentityState.Exact, result.State);
        Assert.True(result.IsExact);
        Assert.Equal("samsung-a52", result.Profile!.Id);
        Assert.True(result.ExactModelNumberMatched);
        Assert.True(result.ManufacturerMatched);
        Assert.True(result.VerifiedIdentitySource);
    }

    [Fact]
    public void ManufacturerConflictPreventsExactIdentityEvenWhenModelNumberMatches()
    {
        var result = DeviceCapabilityEngine.ResolveIdentity(
            Device(manufacturer: "Xiaomi"),
            [Profile()]);

        Assert.Equal(DeviceIdentityState.Conflicting, result.State);
        Assert.Null(result.Profile);
        Assert.Contains(result.Conflicts, value => value.Contains("الشركة المعلنة", StringComparison.Ordinal));
    }

    [Fact]
    public void MatchingDeviceCodeWithoutExactModelNumberIsOnlyProbable()
    {
        var result = DeviceCapabilityEngine.ResolveIdentity(
            Device(modelNumber: "غير متاح", deviceCode: "a52q"),
            [Profile()]);

        Assert.Equal(DeviceIdentityState.Probable, result.State);
        Assert.False(result.IsExact);
    }

    [Fact]
    public void EqualTopMatchesAreAmbiguousAndDoNotChooseFirstProfile()
    {
        var first = Profile(id: "first");
        var second = Profile(id: "second");
        var result = DeviceCapabilityEngine.ResolveIdentity(Device(), [first, second]);

        Assert.Equal(DeviceIdentityState.Ambiguous, result.State);
        Assert.Null(result.Profile);
        Assert.Contains("first", result.Conflicts[0]);
        Assert.Contains("second", result.Conflicts[0]);
    }

    [Fact]
    public void MissingOrUnverifiedIdentitySourceCannotProduceAnExactMatch()
    {
        var source = Profile();
        var profile = new DeviceProfile
        {
            Id = source.Id,
            Manufacturer = source.Manufacturer,
            MarketingName = source.MarketingName,
            ModelNumbers = source.ModelNumbers,
            DeviceCodes = source.DeviceCodes,
            ProductNames = source.ProductNames,
            ModelAliases = source.ModelAliases,
            SocAliases = source.SocAliases,
            IdentitySource = source.IdentitySource,
            IdentitySourceUrl = source.IdentitySourceUrl,
            IdentityVerified = false
        };
        var result = DeviceCapabilityEngine.ResolveIdentity(Device(), [profile]);

        Assert.Equal(DeviceIdentityState.Probable, result.State);
        Assert.False(result.VerifiedIdentitySource);
        Assert.False(result.IsExact);
    }

    [Fact]
    public void ADBReadAndScreenCapabilitiesRequireACurrentlyAuthorizedSafeSerial()
    {
        var supported = DeviceCapabilityEngine.Evaluate(
            new DeviceCapabilityContext(Device(), AdbAuthorized: true,
                AgentConnected: false, FastbootDetected: false,
                VerifiedBoardMapAvailable: false),
            [Profile()]);

        Assert.True(Get(supported, DeviceCapabilityOperation.ReadAndroidDiagnostics).CanExecute);
        Assert.True(Get(supported, DeviceCapabilityOperation.MirrorScreen).CanExecute);
        Assert.Equal(CapabilityDecisionState.NeedsSetup,
            Get(supported, DeviceCapabilityOperation.RunAgentHardwareTests).State);

        var denied = DeviceCapabilityEngine.Evaluate(
            new DeviceCapabilityContext(Device(), AdbAuthorized: false,
                AgentConnected: true, FastbootDetected: false,
                VerifiedBoardMapAvailable: false),
            [Profile()]);

        Assert.False(Get(denied, DeviceCapabilityOperation.ReadAndroidDiagnostics).CanExecute);
        Assert.False(Get(denied, DeviceCapabilityOperation.MirrorScreen).CanExecute);
        Assert.False(Get(denied, DeviceCapabilityOperation.RunAgentHardwareTests).CanExecute);
    }

    [Fact]
    public void InvalidSerialNeverReceivesADBCapability()
    {
        var result = DeviceCapabilityEngine.Evaluate(
            new DeviceCapabilityContext(Device(serial: "--dangerous"),
                AdbAuthorized: true, AgentConnected: true,
                FastbootDetected: true, VerifiedBoardMapAvailable: true),
            [Profile()]);

        Assert.False(Get(result, DeviceCapabilityOperation.ReadAndroidDiagnostics).CanExecute);
        Assert.False(Get(result, DeviceCapabilityOperation.MirrorScreen).CanExecute);
        Assert.False(Get(result, DeviceCapabilityOperation.ReadFastbootVariables).CanExecute);
    }

    [Fact]
    public void FastbootReadOnlyRequiresFastbootDetectionAndDoesNotFollowFromAdbAuthorization()
    {
        var adbOnly = DeviceCapabilityEngine.Evaluate(
            new DeviceCapabilityContext(Device(), AdbAuthorized: true,
                AgentConnected: false, FastbootDetected: false,
                VerifiedBoardMapAvailable: false),
            [Profile()]);
        var fastboot = DeviceCapabilityEngine.Evaluate(
            new DeviceCapabilityContext(Device(), AdbAuthorized: false,
                AgentConnected: false, FastbootDetected: true,
                VerifiedBoardMapAvailable: false),
            [Profile()]);

        Assert.Equal(CapabilityDecisionState.Unknown,
            Get(adbOnly, DeviceCapabilityOperation.ReadFastbootVariables).State);
        Assert.True(Get(fastboot, DeviceCapabilityOperation.ReadFastbootVariables).CanExecute);
        Assert.False(Get(fastboot, DeviceCapabilityOperation.ReadAndroidDiagnostics).CanExecute);
    }

    [Fact]
    public void VerifiedBoardMapRequiresExactDeviceIdentity()
    {
        var verified = DeviceCapabilityEngine.Evaluate(
            new DeviceCapabilityContext(Device(), true, false, false, true),
            [Profile()]);
        var probable = DeviceCapabilityEngine.Evaluate(
            new DeviceCapabilityContext(Device(modelNumber: "غير متاح", deviceCode: "a52q"),
                true, false, false, true),
            [Profile()]);

        Assert.True(Get(verified, DeviceCapabilityOperation.ShowVerifiedBoardMap).CanExecute);
        Assert.False(Get(probable, DeviceCapabilityOperation.ShowVerifiedBoardMap).CanExecute);
        Assert.Equal(CapabilityDecisionState.Unknown,
            Get(probable, DeviceCapabilityOperation.ShowVerifiedBoardMap).State);
    }

    [Fact]
    public void ValidLocalFirmwareHashAndExactModelOnlyRequireFurtherPublisherAndRollbackReview()
    {
        var firmware = Firmware();
        var integrity = new FirmwareIntegrityResult(
            firmware.Id, firmware.FileName, true, true, true,
            firmware.SizeBytes, firmware.Sha256, "local file hash matches");
        var report = DeviceCapabilityEngine.Evaluate(
            new DeviceCapabilityContext(Device(), true, false, false, false, firmware, integrity),
            [Profile()]);

        var decision = Get(report, DeviceCapabilityOperation.ReviewFirmwareCandidate);
        Assert.Equal(CapabilityDecisionState.ReviewRequired, decision.State);
        Assert.False(decision.CanExecute);
        Assert.Contains("Anti-Rollback", decision.Evidence);
    }

    [Fact]
    public void FirmwareModelMismatchBlocksCandidateEvenWhenLocalHashMatches()
    {
        var firmware = Firmware(model: "SM-A515F");
        var integrity = new FirmwareIntegrityResult(
            firmware.Id, firmware.FileName, true, true, true,
            firmware.SizeBytes, firmware.Sha256, "local file hash matches");
        var report = DeviceCapabilityEngine.Evaluate(
            new DeviceCapabilityContext(Device(), true, false, false, false, firmware, integrity),
            [Profile()]);

        Assert.Equal(CapabilityDecisionState.Blocked,
            Get(report, DeviceCapabilityOperation.ReviewFirmwareCandidate).State);
    }

    [Fact]
    public void InvalidFirmwareHashBlocksCandidate()
    {
        var firmware = Firmware();
        var integrity = new FirmwareIntegrityResult(
            firmware.Id, firmware.FileName, true, true, false,
            firmware.SizeBytes, "bad", "hash mismatch");
        var report = DeviceCapabilityEngine.Evaluate(
            new DeviceCapabilityContext(Device(), true, false, false, false, firmware, integrity),
            [Profile()]);

        Assert.Equal(CapabilityDecisionState.Blocked,
            Get(report, DeviceCapabilityOperation.ReviewFirmwareCandidate).State);
    }

    [Theory]
    [InlineData(DeviceCapabilityOperation.FlashFirmware)]
    [InlineData(DeviceCapabilityOperation.UnlockBootloader)]
    [InlineData(DeviceCapabilityOperation.EraseDeviceData)]
    [InlineData(DeviceCapabilityOperation.QualcommEmergencyDownload)]
    [InlineData(DeviceCapabilityOperation.MediaTekBromRecovery)]
    [InlineData(DeviceCapabilityOperation.SamsungDownloadMode)]
    [InlineData(DeviceCapabilityOperation.XiaomiFastbootFlash)]
    public void DestructiveAndOEMServiceOperationsRemainBlockedEvenWithAllEvidence(
        DeviceCapabilityOperation operation)
    {
        var report = DeviceCapabilityEngine.Evaluate(
            new DeviceCapabilityContext(Device(), true, true, true, true, Firmware(),
                new FirmwareIntegrityResult(Guid.NewGuid(), "file.zip", true, true, true, 10, new string('a', 64), "matched")),
            [Profile()]);

        var decision = Get(report, operation);
        Assert.Equal(CapabilityDecisionState.Blocked, decision.State);
        Assert.False(decision.CanExecute);
    }

    private static DeviceCapabilityDecision Get(
        DeviceCapabilityReport report,
        DeviceCapabilityOperation operation)
        => Assert.Single(report.Decisions, item => item.Operation == operation);

    private static DeviceSnapshot Device(
        string serial = "R58N12345",
        string manufacturer = "Samsung",
        string modelNumber = "SM-A525F",
        string deviceCode = "a52q")
        => new()
        {
            Serial = serial,
            Manufacturer = manufacturer,
            Model = "Galaxy A52",
            ModelNumber = modelNumber,
            DeviceCode = deviceCode,
            Product = "a52q",
            Board = "lahaina",
            Soc = "SM7125"
        };

    private static DeviceProfile Profile(string id = "samsung-a52")
        => new()
        {
            Id = id,
            Manufacturer = "Samsung",
            MarketingName = "Samsung Galaxy A52",
            ModelNumbers = ["SM-A525F"],
            DeviceCodes = ["a52q"],
            ProductNames = ["a52q"],
            ModelAliases = ["Galaxy A52"],
            SocAliases = ["SM7125"],
            IdentitySource = "Manufacturer model number and Android device properties",
            IdentitySourceUrl = "https://www.samsung.com/",
            IdentityVerified = true
        };

    private static FirmwareLibraryItem Firmware(string model = "SM-A525F")
        => new(
            Guid.NewGuid(),
            "Samsung",
            model,
            "XSG",
            "A525FXXU",
            "firmware.zip",
            "Samsung/SM-A525F/XSG/firmware.zip",
            1024,
            new string('a', 64),
            DateTimeOffset.UtcNow,
            "Official-source details supplied by technician",
            "");
}
