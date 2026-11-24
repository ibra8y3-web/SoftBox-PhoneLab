using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class HdmiCaptureServiceTests
{
    [Fact]
    public void ParsesOnlyVideoDevicesAndIgnoresAudioAndAlternativeNames()
    {
        var lines = new[]
        {
            "[dshow @ 000001] DirectShow video devices",
            "[dshow @ 000001]   \"USB Video Capture\" (video)",
            "[dshow @ 000001]   \"USB Audio Capture\" (audio)",
            "[dshow @ 000001]   Alternative name \"@device_pnp_\\\\?\\usb#vid_1234\"",
            "[dshow @ 000001]   \"Integrated Camera\" (video)",
            "[dshow @ 000001]   \"usb video capture\" (video)"
        };

        var devices = HdmiCaptureService.ParseVideoDeviceNames(lines);

        Assert.Equal(new[] { "USB Video Capture", "Integrated Camera" }, devices);
    }

    [Fact]
    public void IgnoresEmptyAndMalformedDirectShowLines()
    {
        var devices = HdmiCaptureService.ParseVideoDeviceNames(new[]
        {
            "",
            "No camera listed",
            "[dshow @ 000001] \"Audio only\" (audio)",
            "[dshow @ 000001] \"\" (video)"
        });

        Assert.Empty(devices);
    }
}
