using SoftBox.PhoneLab.Core.Diagnostics;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class ServiceTransportReadinessPolicyTests
{
    [Fact]
    public void QualcommEdlModeIsDetectedButNeverMarkedFlashReady()
    {
        var result = ServiceTransportReadinessPolicy.Evaluate(
        [
            new("USB\\VID_05C6&PID_9008", "Qualcomm HS-USB QDLoader", "05C6", "9008", 0, "QualcommUsb")
        ]).Single();

        Assert.Equal(ServiceTransportKind.QualcommEdl, result.Kind);
        Assert.Equal(ServiceTransportState.DetectedDiagnosticOnly, result.State);
        Assert.False(result.FlashAllowed);
        Assert.NotEmpty(result.MissingRequirements);
    }

    [Fact]
    public void MediaTekBromWithPnpProblemIsNotReady()
    {
        var result = ServiceTransportReadinessPolicy.Evaluate(
        [
            new("USB\\VID_0E8D&PID_0003", "MediaTek PreLoader", "0E8D", "0003", 10, "")
        ]).Single();

        Assert.Equal(ServiceTransportKind.MediaTekPreloaderBrom, result.Kind);
        Assert.Equal(ServiceTransportState.DetectedWithDriverProblem, result.State);
        Assert.Contains(result.Findings, item => item.Contains("10", StringComparison.Ordinal));
        Assert.False(result.FlashAllowed);
    }

    [Fact]
    public void IgnoresUnknownUsbIdentities()
    {
        Assert.Empty(ServiceTransportReadinessPolicy.Evaluate(
        [
            new("USB\\VID_1234&PID_5678", "Unknown", "1234", "5678", 0, "")
        ]));
    }
}
