using System.Text;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;
namespace SoftBox.PhoneLab.Tests;
public sealed class BoardViewNativeFormatDetectorTests
{
    [Fact]
    public async Task DetectsToptestBrd2FromContent()
    {
        var path = CreateFile(".brd", "BRDOUT:\r\nNETS:\r\nPARTS:\r\n");
        try { var result = await BoardViewNativeFormatDetector.DetectAsync(path); Assert.True(result.ContentRecognized); Assert.Equal("toptest-brd2", result.FormatId); }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task DetectsHonhanBdvMarkers()
    {
        var path = CreateFile(".bdv", "<<format.asc>>\r\n<<pins.asc>>\r\n");
        try { var result = await BoardViewNativeFormatDetector.DetectAsync(path); Assert.True(result.ContentRecognized); Assert.Equal("honhan-bdv", result.FormatId); }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task UnknownBdvIsNotTreatedAsDecodedBoardData()
    {
        var path = CreateFile(".bdv", "this is not a valid board signature");
        try { var result = await BoardViewNativeFormatDetector.DetectAsync(path); Assert.False(result.ContentRecognized); Assert.Equal("unidentified", result.FormatId); }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task DetectsAltiumCompoundFileSignature()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-board-" + Guid.NewGuid().ToString("N") + ".pcbdoc");
        await File.WriteAllBytesAsync(path, [0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1]);
        try { var result = await BoardViewNativeFormatDetector.DetectAsync(path); Assert.True(result.ContentRecognized); Assert.Equal("altium-pcbdoc", result.FormatId); }
        finally { File.Delete(path); }
    }
    private static string CreateFile(string extension, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-board-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllText(path, content, Encoding.ASCII); return path;
    }
}
