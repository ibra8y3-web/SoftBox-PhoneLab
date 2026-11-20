using System.Net.Http;
using System.Net;
using SoftBox.PhoneLab.Core.Updates;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class ReleaseUpdateServiceTests
{
    [Fact]
    public async Task StableChannelIgnoresPrereleasesAndReportsLatestStableUpdate()
    {
        using var client = new HttpClient(new StubHandler(ReleasePayload()));
        var service = new ReleaseUpdateService(client);

        var result = await service.CheckAsync("0.8.0", UpdateChannel.Stable);

        Assert.True(result.UpdateAvailable);
        Assert.Equal("v0.9.0", result.LatestRelease?.TagName);
        Assert.False(result.LatestRelease!.IsPrerelease);
        Assert.True(ReleaseUpdateService.IsOfficialReleaseUrl(result.LatestRelease.HtmlUrl));
        Assert.All(result.LatestRelease.Assets, asset => Assert.True(ReleaseUpdateService.IsOfficialAssetUrl(asset.DownloadUrl)));
    }

    [Fact]
    public async Task BetaChannelCanSelectPrereleaseAndCompareSemVer()
    {
        using var client = new HttpClient(new StubHandler(ReleasePayload()));
        var service = new ReleaseUpdateService(client);

        var result = await service.CheckAsync("0.9.0", UpdateChannel.Beta);

        Assert.True(result.UpdateAvailable);
        Assert.Equal("1.0.0-beta.2", result.LatestRelease?.TagName);
        Assert.True(result.LatestRelease!.IsPrerelease);
    }

    [Fact]
    public async Task CurrentVersionNewerThanReleaseDoesNotOfferDowngrade()
    {
        using var client = new HttpClient(new StubHandler(ReleasePayload()));
        var service = new ReleaseUpdateService(client);

        var result = await service.CheckAsync("2.0.0", UpdateChannel.Beta);

        Assert.False(result.UpdateAvailable);
        Assert.Equal("1.0.0-beta.2", result.LatestRelease?.TagName);
    }

    [Theory]
    [InlineData("https://evil.example/ibra8y3-web/SoftBox-PhoneLab/releases/tag/v1.0.0")]
    [InlineData("http://github.com/ibra8y3-web/SoftBox-PhoneLab/releases/tag/v1.0.0")]
    [InlineData("https://github.com.evil.example/ibra8y3-web/SoftBox-PhoneLab/releases/tag/v1.0.0")]
    [InlineData("https://attacker@github.com/ibra8y3-web/SoftBox-PhoneLab/releases/tag/v1.0.0")]
    [InlineData("https://github.com/another-owner/SoftBox-PhoneLab/releases/tag/v1.0.0")]
    public void ReleaseAndAssetLinksMustUseTheOfficialHttpsRepository(string url)
    {
        Assert.False(ReleaseUpdateService.IsOfficialReleaseUrl(url));
        Assert.False(ReleaseUpdateService.IsOfficialAssetUrl(url));
    }

    [Fact]
    public async Task InvalidSemanticVersionAndOversizedResponseFailClosed()
    {
        using var client = new HttpClient(new StubHandler("""
            [
              { "tag_name": "latest", "html_url": "https://github.com/ibra8y3-web/SoftBox-PhoneLab/releases/tag/latest", "draft": false, "prerelease": false }
            ]
            """));
        var service = new ReleaseUpdateService(client);

        var result = await service.CheckAsync("1.0.0", UpdateChannel.Stable);
        Assert.Null(result.LatestRelease);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CheckAsync("latest", UpdateChannel.Stable));
    }

    private static string ReleasePayload() => """
        [
          {
            "tag_name": "v0.9.0",
            "name": "SoftBox 0.9.0",
            "body": "Stable release notes.",
            "html_url": "https://github.com/ibra8y3-web/SoftBox-PhoneLab/releases/tag/v0.9.0",
            "draft": false,
            "prerelease": false,
            "published_at": "2026-11-01T12:00:00Z",
            "assets": [
              {
                "name": "SoftBox-Windows-x64.zip",
                "size": 123456,
                "browser_download_url": "https://github.com/ibra8y3-web/SoftBox-PhoneLab/releases/download/v0.9.0/SoftBox-Windows-x64.zip"
              }
            ]
          },
          {
            "tag_name": "1.0.0-beta.2",
            "name": "SoftBox 1.0.0 Beta 2",
            "body": "Beta notes.",
            "html_url": "https://github.com/ibra8y3-web/SoftBox-PhoneLab/releases/tag/1.0.0-beta.2",
            "draft": false,
            "prerelease": true,
            "published_at": "2026-11-10T12:00:00Z",
            "assets": []
          },
          {
            "tag_name": "v1.0.1",
            "name": "Draft release",
            "body": "Hidden.",
            "html_url": "https://github.com/ibra8y3-web/SoftBox-PhoneLab/releases/tag/v1.0.1",
            "draft": true,
            "prerelease": false,
            "published_at": "2026-11-15T12:00:00Z",
            "assets": []
          }
        ]
        """;

    private sealed class StubHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(ReleaseUpdateService.ReleasesApiUrl, request.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload)
            });
        }
    }
}
