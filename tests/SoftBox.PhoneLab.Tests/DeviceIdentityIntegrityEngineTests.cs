using SoftBox.PhoneLab.Core.Diagnostics;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceIdentityIntegrityEngineTests
{
    [Fact]
    public void ExactIdentifierReadByTwoSourcesIsReportedAsConsistentEvidence()
    {
        var report = DeviceIdentityIntegrityEngine.Analyze(
        [
            Observation("Android", "OS", "Device Serial", "R58M123456A", "Samsung", "SM-A315G", "ADB", "Authorized transport"),
            Observation("Android", "Bootloader", "Device Serial", "R58M123456A", "Samsung", "SM-A315G", "Fastboot", "Bootloader transport")
        ]);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(DeviceIdentityIntegrityStatus.ConsistentEvidence, finding.Status);
        Assert.Equal("••••456A", finding.MaskedIdentifier);
        Assert.Equal(2, finding.Sources.Count);
        Assert.Equal(DeviceIdentityIntegrityStatus.ConsistentEvidence, report.OverallStatus);
    }

    [Fact]
    public void SameExactIdentifierWithConflictingModelsRequiresReviewAndIsNotAutoResolved()
    {
        var report = DeviceIdentityIntegrityEngine.Analyze(
        [
            Observation("Android", "OS", "Device Serial", "R58M123456A", "Samsung", "SM-A315G", "ADB", "Authorized transport"),
            Observation("Android", "Bootloader", "Device Serial", "R58M123456A", "Xiaomi", "Redmi Note 12", "Fastboot", "Bootloader transport")
        ]);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(DeviceIdentityIntegrityStatus.ConflictDetected, finding.Status);
        Assert.Contains(finding.Warnings, warning => warning.Contains("مختلفة"));
    }

    [Fact]
    public void ASingleSourceIsNotPresentedAsCorroboratedIdentity()
    {
        var report = DeviceIdentityIntegrityEngine.Analyze(
        [
            Observation("Android", "OS", "ADB transport serial", "R58M123456A", "Samsung", "SM-A315G", "ADB", "Authorized transport")
        ]);

        Assert.Equal(DeviceIdentityIntegrityStatus.InsufficientEvidence, report.OverallStatus);
        Assert.Equal(DeviceIdentityIntegrityStatus.InsufficientEvidence, Assert.Single(report.Findings).Status);
    }

    [Fact]
    public void UsbVendorHintIsAlwaysReviewRequiredRatherThanAConfirmedSerial()
    {
        var report = DeviceIdentityIntegrityEngine.Analyze(
        [
            Observation("Android", "Unknown USB mode", "USB hardware ID", "04E8:6860", "Samsung", "", "Windows USB PnP", "VID/PID hint")
        ]);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(DeviceIdentityIntegrityStatus.ReviewRequired, finding.Status);
        Assert.Equal("••••6860", finding.MaskedIdentifier);
    }

    [Fact]
    public void RepeatedUsbHardwareIdsDoNotCountAsIndependentDeviceIdentityEvidence()
    {
        var report = DeviceIdentityIntegrityEngine.Analyze(
        [
            Observation("USB", "Samsung USB", "USB VID/PID", "04E8:6860", "Samsung", "", "Windows USB PnP", "VID/PID hint"),
            Observation("USB", "Samsung USB", "USB VID/PID", "04E8:6860", "Samsung", "", "Windows USB PnP", "VID/PID hint")
        ]);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(DeviceIdentityIntegrityStatus.ReviewRequired, finding.Status);
        Assert.Contains(finding.Warnings, warning => warning.Contains("قد يتكرر"));
    }

    [Fact]
    public void DifferentTransportIdentifiersAreNotJoinedByGuessing()
    {
        var report = DeviceIdentityIntegrityEngine.Analyze(
        [
            Observation("Android", "OS", "ADB transport serial", "R58M123456A", "Samsung", "SM-A315G", "ADB", "Authorized transport"),
            Observation("Android", "Bootloader", "Fastboot transport serial", "F9AB9876543", "Samsung", "SM-A315G", "Fastboot", "Bootloader transport")
        ]);

        Assert.Equal(2, report.Findings.Count);
        Assert.DoesNotContain(report.Findings, finding => finding.Status == DeviceIdentityIntegrityStatus.ConflictDetected);
        Assert.Contains(report.GlobalWarnings, warning => warning.Contains("لا تتم مطابقة"));
    }

    [Fact]
    public void UnsafeIdentifierValuesAreRejected()
    {
        Assert.False(DeviceIdentityIntegrityEngine.IsValid(
            Observation("Android", "OS", "Serial", "serial\nFLASH", "Samsung", "SM-A315G", "ADB", "Authorized")));
        Assert.False(DeviceIdentityIntegrityEngine.IsValid(
            Observation("Android", "OS", "Serial", "x", "Samsung", new string('m', 129), "ADB", "Authorized")));
    }

    [Fact]
    public void AnalysisHasAHardObservationLimit()
    {
        var observations = Enumerable.Range(0, 258)
            .Select(index => Observation("Android", "OS", "ADB serial", "S" + index, "", "", "ADB", "Authorized"));

        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceIdentityIntegrityEngine.Analyze(observations));
    }

    private static DeviceIdentityObservation Observation(
        string platform,
        string mode,
        string kind,
        string identifier,
        string manufacturer,
        string model,
        string source,
        string confidence)
        => new(platform, mode, kind, identifier, manufacturer, model, source, confidence);
}
