using System.Net.Http;
using System.Net;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class OemResourceAvailabilityServiceTests
{
    [Fact]
    public async Task ValidOfficialHttpsPageIsReportedReachableWithoutClaimingPackageCompatibility()
    {
        var handler = new SequenceHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new HttpClient(handler);
        using var service = new OemResourceAvailabilityService(client);

        var report = await service.CheckAsync("https://support.example.com/driver");

        Assert.Equal(OemResourceAvailabilityStatus.Reachable, report.Status);
        Assert.Equal(HttpStatusCode.OK, report.HttpStatusCode);
        Assert.Equal("HEAD", report.MethodUsed);
        Assert.Contains("لا تثبت وجود ملف تعريف مباشر", report.Limitation);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task HeadNotSupportedFallsBackToGetWithoutReadingResponseBody()
    {
        var handler = new SequenceHandler((request, index) =>
        {
            if (index == 0)
            {
                Assert.Equal(HttpMethod.Head, request.Method);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("bytes=0-0", request.Headers.Range?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new HttpClient(handler);
        using var service = new OemResourceAvailabilityService(client);

        var report = await service.CheckAsync("https://vendor.example.com/downloads");

        Assert.Equal(OemResourceAvailabilityStatus.Reachable, report.Status);
        Assert.Equal("GET", report.MethodUsed);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task HttpsRedirectIsFollowedAndFinalTargetIsReported()
    {
        var handler = new SequenceHandler((request, index) =>
        {
            if (index == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MovedPermanently)
                {
                    Headers = { Location = new Uri("https://support.example.com/new-path") }
                });
            Assert.Equal("https://support.example.com/new-path", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new HttpClient(handler);
        using var service = new OemResourceAvailabilityService(client);

        var report = await service.CheckAsync("https://vendor.example.com/old-path");

        Assert.Equal(OemResourceAvailabilityStatus.Reachable, report.Status);
        Assert.Equal("https://support.example.com/new-path", report.FinalUrl);
        Assert.Equal(1, report.RedirectCount);
    }

    [Fact]
    public async Task HttpsToHttpDowngradeIsBlockedWithoutFollowingIt()
    {
        var handler = new SequenceHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("http://unsafe.example.com/file.inf") }
            }));
        using var client = new HttpClient(handler);
        using var service = new OemResourceAvailabilityService(client);

        var report = await service.CheckAsync("https://vendor.example.com/driver");

        Assert.Equal(OemResourceAvailabilityStatus.InsecureRedirectBlocked, report.Status);
        Assert.Equal(1, handler.RequestCount);
        Assert.StartsWith("http://", report.FinalUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotFoundIsDifferentFromRestrictedOrReachable()
    {
        var handler = new SequenceHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var client = new HttpClient(handler);
        using var service = new OemResourceAvailabilityService(client);

        var report = await service.CheckAsync("https://vendor.example.com/missing");

        Assert.Equal(OemResourceAvailabilityStatus.NotFound, report.Status);
        Assert.Equal(HttpStatusCode.NotFound, report.HttpStatusCode);
    }

    [Fact]
    public async Task RestrictedVendorPortalIsNotReportedAsMissing()
    {
        var handler = new SequenceHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        using var client = new HttpClient(handler);
        using var service = new OemResourceAvailabilityService(client);

        var report = await service.CheckAsync("https://vendor.example.com/account");

        Assert.Equal(OemResourceAvailabilityStatus.AccessRestricted, report.Status);
        Assert.Contains("رفض المصدر الوصول", report.Explanation);
    }

    [Fact]
    public async Task InvalidNonHttpsUrlIsRejectedBeforeNetworkAccess()
    {
        var handler = new SequenceHandler((_, _) =>
            throw new InvalidOperationException("Network must not be called for an invalid URL."));
        using var client = new HttpClient(handler);
        using var service = new OemResourceAvailabilityService(client);

        var report = await service.CheckAsync("http://vendor.example.com/driver");

        Assert.Equal(OemResourceAvailabilityStatus.InvalidUrl, report.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task MoreThanFiveRedirectsAreStopped()
    {
        var handler = new SequenceHandler((request, index) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri($"https://vendor.example.com/path/{index + 1}") }
            }));
        using var client = new HttpClient(handler);
        using var service = new OemResourceAvailabilityService(client);

        var report = await service.CheckAsync("https://vendor.example.com/start");

        Assert.Equal(OemResourceAvailabilityStatus.RedirectLimitExceeded, report.Status);
        Assert.Equal(5, report.RedirectCount);
        Assert.Equal(6, handler.RequestCount);
    }

    private sealed class SequenceHandler(
        Func<HttpRequestMessage, int, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return responseFactory(request, RequestCount - 1);
        }
    }
}
