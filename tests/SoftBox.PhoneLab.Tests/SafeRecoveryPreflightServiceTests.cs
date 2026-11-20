using SoftBox.PhoneLab.Core.Firmware;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Core.Recovery;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SafeRecoveryPreflightServiceTests
{
    [Fact]
    public void PreflightNeverAllowsFlashWhenPublisherCompatibilityAndRollbackTrustAreUnknown()
    {
        var firmware = CreateFirmware();
        var integrity = new FirmwareIntegrityResult(
            firmware.Id, firmware.FileName, true, true, true, firmware.SizeBytes,
            firmware.Sha256, "Local hash matches import record.");

        var report = SafeRecoveryPreflightService.Evaluate(
            new DeviceSnapshot
            {
                Serial = "authorized-device-serial",
                Manufacturer = "Samsung",
                Model = "Galaxy A52",
                ModelNumber = "SM-A525F"
            },
            adbAuthorized: true,
            firmware,
            integrity);

        Assert.False(report.CanProceed);
        Assert.Contains(report.Checks, check =>
            check.Id == "publisher-authenticity" && check.State == RecoveryCheckState.Blocked);
        Assert.Contains(report.Checks, check =>
            check.Id == "device-compatibility" && check.State == RecoveryCheckState.Blocked);
        Assert.Contains(report.Checks, check =>
            check.Id == "anti-rollback" && check.State == RecoveryCheckState.Blocked);
        Assert.Contains(report.Checks, check =>
            check.Id == "local-file-integrity" && check.State == RecoveryCheckState.Passed);
    }

    [Fact]
    public void MismatchedFirmwareManufacturerAndModelFailEvenWhenLocalHashMatches()
    {
        var firmware = CreateFirmware(manufacturer: "Xiaomi", model: "Redmi Note 12");
        var integrity = new FirmwareIntegrityResult(
            firmware.Id, firmware.FileName, true, true, true, firmware.SizeBytes,
            firmware.Sha256, "Local hash matches import record.");

        var report = SafeRecoveryPreflightService.Evaluate(
            new DeviceSnapshot
            {
                Serial = "authorized-device-serial",
                Manufacturer = "Samsung",
                Model = "Galaxy A52",
                ModelNumber = "SM-A525F"
            },
            adbAuthorized: true,
            firmware,
            integrity);

        Assert.False(report.CanProceed);
        Assert.Contains(report.Checks, check =>
            check.Id == "manufacturer-match" && check.State == RecoveryCheckState.Failed);
        Assert.Contains(report.Checks, check =>
            check.Id == "model-match" && check.State == RecoveryCheckState.Failed);
    }

    [Fact]
    public void MissingAuthorizedDeviceSnapshotIsReportedAsUnknownOrFailedAndNeverAllowed()
    {
        var firmware = CreateFirmware();
        var report = SafeRecoveryPreflightService.Evaluate(
            device: null,
            adbAuthorized: false,
            firmware,
            integrity: null);

        Assert.False(report.CanProceed);
        Assert.Contains(report.Checks, check =>
            check.Id == "device-authorization" && check.State == RecoveryCheckState.Failed);
        Assert.Contains(report.Checks, check =>
            check.Id == "device-identity" && check.State == RecoveryCheckState.Unknown);
    }

    private static FirmwareLibraryItem CreateFirmware(
        string manufacturer = "Samsung",
        string model = "SM-A525F")
        => new(
            Guid.NewGuid(),
            manufacturer,
            model,
            "XSG",
            "A525FXXU",
            "firmware.zip",
            "Samsung/SM-A525F/XSG/firmware.zip",
            1024,
            new string('a', 64),
            DateTimeOffset.UtcNow,
            "Test metadata only",
            "");
}
