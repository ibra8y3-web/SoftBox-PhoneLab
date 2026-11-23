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
        Assert.Contains("توقيع CAT", report.Limitation);
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
            .Replace("NTx86.10.0...19041", "NTx86.10.0...26100", StringComparison.Ordinal);
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
    public async Task AnalyzeFileAsyncReadsExtractedInfAndReturnsCanonicalPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "softbox-inf-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "oem-device.inf");
        try
        {
            await File.WriteAllTextAsync(path, MatchingInf);
            var report = await DriverInfCompatibilityAnalyzer.AnalyzeFileAsync(
                path, ["USB\\VID_1234&PID_5678"], Windows11X64);

            Assert.Equal(Path.GetFullPath(path), report.InfPath);
            Assert.Equal(DriverInfMatchStatus.Compatible, report.Status);
            Assert.Equal("amd64", report.Architecture);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzeFileAsyncRejectsNonInfInputBeforeParsing()
    {
        var root = Path.Combine(Path.GetTempPath(), "softbox-inf-extension-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "driver.exe");
        try
        {
            await File.WriteAllTextAsync(path, "not an INF");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                DriverInfCompatibilityAnalyzer.AnalyzeFileAsync(
                    path, ["USB\\VID_1234&PID_5678"], Windows11X64));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProductTypeAndSuiteMaskDecorationIsReportedAsUnresolvedRatherThanCompatible()
    {
        const string inf = """
            [Version]
            Signature="$Windows NT$"
            [Manufacturer]
            %Mfg%=Models,NTamd64.10.0.1.0.19041
            [Models.NTamd64.10.0.1.0.19041]
            %Phone%=Install,USB\VID_1234&PID_5678
            """;

        var report = DriverInfCompatibilityAnalyzer.Analyze(
            inf,
            ["USB\\VID_1234&PID_5678"],
            Windows11X64);

        Assert.Equal(DriverInfMatchStatus.UnsupportedTargetDecoration, report.Status);
        Assert.Contains("ProductType/SuiteMask", report.Explanation);
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

        var x86Target = new WindowsDriverTarget(
            new Version(10, 0, 19045), "x86", "Windows x86");
        var x64Report = DriverInfCompatibilityAnalyzer.Analyze(
            inf, ["USB\\VID_ABCD&PID_0001"], Windows11X64);
        var x86Compatible = DriverInfCompatibilityAnalyzer.Analyze(
            inf, ["USB\\VID_ABCD&PID_0001"], x86Target);
        var mismatch = DriverInfCompatibilityAnalyzer.Analyze(
            inf, ["USB\\VID_ABCD&PID_0002"], x86Target);

        Assert.Equal(DriverInfMatchStatus.ArchitectureMismatch, x64Report.Status);
        Assert.Equal(DriverInfMatchStatus.Compatible, x86Compatible.Status);
        Assert.Equal(DriverInfMatchStatus.HardwareIdNotFound, mismatch.Status);
    }
}
