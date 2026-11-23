using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DriverInfCompatibilityAnalyzerTests
{
    private static readonly WindowsDriverTarget Windows11X64 =
        new(new Version(10, 0, 22621), "amd64", "Windows 11 22H2 x64");

    private const string MatchingInf = """
        [Version]
        Signature="$Windows NT$"
        Provider=%OEM%
        DriverVer=11/20/2026,1.2.3.4

        [Manufacturer]
        %Mfg%=PhoneModels,NTamd64.10.0...19041,NTx86.10.0...19041

        [PhoneModels.NTamd64.10.0...19041]
        %Phone%=InstallSection,USB\VID_1234&PID_5678,USB\VID_1234&PID_5678&REV_0100

        [PhoneModels.NTx86.10.0...19041]
        %Phone%=InstallSection,USB\VID_1234&PID_5678

        [InstallSection]
        CopyFiles=DriverCopy

        """;

    [Fact]
    public void ExactHardwareIdAndMatchingArchitectureAndBuildAreAccepted()
    {
        var report = DriverInfCompatibilityAnalyzer.Analyze(
            MatchingInf,
            ["USB\\VID_1234&PID_5678", "USB\\VID_1234&PID_5678&REV_0100"],
            Windows11X64,
            "oem-device.inf");

        Assert.Equal(DriverInfMatchStatus.Compatible, report.Status);
        Assert.Equal("%OEM%", report.Provider);
        Assert.Equal("11/20/2026,1.2.3.4", report.DriverVersion);
        Assert.Contains("PhoneModels.NTamd64.10.0...19041", report.MatchingModelSections);
        Assert.Contains("USB\\VID_1234&PID_5678", report.MatchedHardwareIds);
        Assert.Contains("لا تثبت", report.Limitation);
    }

    [Fact]
    public void MatchingHardwareIdForDifferentArchitectureIsRejected()
    {
        var armTarget = new WindowsDriverTarget(
            new Version(10, 0, 22631), "arm64", "Windows 11 ARM64");

        var report = DriverInfCompatibilityAnalyzer.Analyze(
            MatchingInf,
            ["USB\\VID_1234&PID_5678"],
            armTarget);

        Assert.Equal(DriverInfMatchStatus.ArchitectureMismatch, report.Status);
        Assert.Empty(report.MatchingModelSections.Where(x => x.Contains("arm64", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void MatchingHardwareIdInSectionForNewerWindowsBuildIsRejected()
    {
        var inf = MatchingInf
            .Replace("NTamd64.10.0...19041", "NTamd64.10.0...26100", StringComparison.Ordinal)
            .Replace("NTx86.10.0...19041", "NTx86.10.0...26100", StringComparison.Ordinal)
            .Replace("[PhoneModels.NTamd64.10.0...26100]", "[PhoneModels.NTamd64.10.0...26100]", StringComparison.Ordinal);
        var report = DriverInfCompatibilityAnalyzer.Analyze(
            inf,
            ["USB\\VID_1234&PID_5678"],
            Windows11X64);

        Assert.Equal(DriverInfMatchStatus.WindowsVersionMismatch, report.Status);
    }

    [Fact]
    public void UnknownHardwareIdIsNotMatchedByVendorNameOrDescription()
    {
        var report = DriverInfCompatibilityAnalyzer.Analyze(
            MatchingInf,
            ["USB\\VID_9999&PID_0001"],
            Windows11X64);

        Assert.Equal(DriverInfMatchStatus.HardwareIdNotFound, report.Status);
        Assert.Empty(report.MatchedHardwareIds);
    }

    [Fact]
    public void MissingWindowsPnpHardwareIdsRemainUnknown()
    {
        var report = DriverInfCompatibilityAnalyzer.Analyze(
            MatchingInf,
            [],
            Windows11X64);

        Assert.Equal(DriverInfMatchStatus.NoHardwareIdentity, report.Status);
        Assert.Contains("لا يمكن إثبات", report.Explanation);
    }

    [Fact]
    public void InfWithoutManufacturerSectionIsNotDeclaredCompatible()
    {
        var report = DriverInfCompatibilityAnalyzer.Analyze(
            "[Version]\nSignature=\"$Windows NT$\"\n",
            ["USB\\VID_1234&PID_5678"],
            Windows11X64);

        Assert.Equal(DriverInfMatchStatus.UnsupportedInf, report.Status);
    }

    [Fact]
    public void UndecoratedModelSectionCanMatchOnlyWhenHardwareIdIsExact()
    {
        const string inf = """
            [Version]
            Signature="$Windows NT$"
            [Manufacturer]
            %Mfg%=Models
            [Models]
            %Phone%=Install,USB\VID_ABCD&PID_0001
            """;

        var compatible = DriverInfCompatibilityAnalyzer.Analyze(
            inf, ["USB\\VID_ABCD&PID_0001"], Windows11X64);
        var mismatch = DriverInfCompatibilityAnalyzer.Analyze(
            inf, ["USB\\VID_ABCD&PID_0002"], Windows11X64);

        Assert.Equal(DriverInfMatchStatus.Compatible, compatible.Status);
        Assert.Equal(DriverInfMatchStatus.HardwareIdNotFound, mismatch.Status);
    }
}
