using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AirPlayReceiverBootstrapServiceTests
{
    [Fact]
    public void ParsesOfficialWindowsX64ReleaseAndItsSha256Digest()
    {
        var json = """
        {
          "tag_name": "v0.5.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            {
              "name": "airplay-windows-v0.5.0-x64.zip",
              "browser_download_url": "https://github.com/moieric11/AirPlay-Windows/releases/download/v0.5.0/airplay-windows-v0.5.0-x64.zip",
              "digest": "sha256:f6d98cfd98826a17d3c12a6209c7a07d33beeb8c64c1705f0ea50c507309becc"
            }
          ]
        }
        """;

        var release = AirPlayReceiverBootstrapService.ParseLatestReleaseManifest(json);

        Assert.Equal("v0.5.0", release.TagName);
        Assert.Equal("f6d98cfd98826a17d3c12a6209c7a07d33beeb8c64c1705f0ea50c507309becc", release.Sha256);
        Assert.Equal("github.com", release.DownloadUri.Host);
    }

    [Theory]
    [InlineData("http://github.com/moieric11/AirPlay-Windows/releases/download/v1/airplay-windows-v1-x64.zip")]
    [InlineData("https://example.com/airplay-windows-v1-x64.zip")]
    public void RejectsNonOfficialOrNonHttpsDownloadUrls(string url)
    {
        var json = """
        {
          "tag_name": "v1",
          "draft": false,
          "prerelease": false,
          "assets": [{
            "name": "airplay-windows-v1-x64.zip",
            "browser_download_url": "PLACEHOLDER",
            "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
          }]
        }
        """.Replace("PLACEHOLDER", url, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            AirPlayReceiverBootstrapService.ParseLatestReleaseManifest(json));
    }

    [Fact]
    public void RejectsMissingOfficialSha256Digest()
    {
        var json = """
        {
          "tag_name": "v1",
          "draft": false,
          "prerelease": false,
          "assets": [{
            "name": "airplay-windows-v1-x64.zip",
            "browser_download_url": "https://github.com/moieric11/AirPlay-Windows/releases/download/v1/airplay-windows-v1-x64.zip"
          }]
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            AirPlayReceiverBootstrapService.ParseLatestReleaseManifest(json));
    }
}
