using System.Net.Http;
using SoftBox.PhoneLab.Core.Diagnostics;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class ScreenMirrorFailureAnalyzerTests
{
    [Fact]
    public void UnauthorizedAdbHasConsentSpecificGuidance()
    {
        var report = ScreenMirrorFailureAnalyzer.Analyze(new UnauthorizedAccessException("unauthorized"));
        Assert.Equal("ADB_UNAUTHORIZED", report.Code);
        Assert.Contains("USB debugging", report.RecommendedAction);
        Assert.DoesNotContain("bypass", report.RecommendedAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidArchiveIsReportedAsIntegrityFailure()
    {
        var report = ScreenMirrorFailureAnalyzer.Analyze(new InvalidDataException("SHA-256 mismatch"));
        Assert.Equal("SCRCPY_INTEGRITY_FAILED", report.Code);
        Assert.Contains("SHA-256", report.RecommendedAction);
    }

    [Fact]
    public void DownloadTimeoutIsDistinctFromDeviceAuthorization()
    {
        var report = ScreenMirrorFailureAnalyzer.Analyze(new HttpRequestException("network timeout"));
        Assert.Equal("SCRCPY_DOWNLOAD_FAILED", report.Code);
        Assert.True(report.Retryable);
    }

    [Fact]
    public void WindowTimeoutHasHostAndAdbRecoveryGuidance()
    {
        var report = ScreenMirrorFailureAnalyzer.Analyze(new TimeoutException("No scrcpy window appeared"));
        Assert.Equal("WINDOW_TIMEOUT", report.Code);
        Assert.Contains("كابل", report.RecommendedAction);
    }

    [Theory]
    [InlineData("device offline", "ADB_OFFLINE")]
    [InlineData("device unauthorized", "ADB_UNAUTHORIZED")]
    [InlineData("no devices/emulators found", "ADB_DEVICE_MISSING")]
    [InlineData("ADB server is out of date", "ADB_SERVER_CONFLICT")]
    public void ScrcpyOutputIsMappedToTheCorrectAdbFailure(string output, string expectedCode)
    {
        var report = ScreenMirrorFailureAnalyzer.Analyze(new InvalidOperationException(output));
        Assert.Equal(expectedCode, report.Code);
        Assert.Contains(output, report.TechnicalDetail);
    }

    [Fact]
    public void UnknownFailuresKeepTheOriginalTechnicalDetail()
    {
        var report = ScreenMirrorFailureAnalyzer.Analyze(new Exception("unclassified failure"));
        Assert.Equal("SCREEN_MIRROR_UNKNOWN", report.Code);
        Assert.Contains("unclassified failure", report.TechnicalDetail);
    }
}
