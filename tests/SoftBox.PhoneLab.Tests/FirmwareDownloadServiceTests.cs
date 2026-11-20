using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using SoftBox.PhoneLab.Core.Firmware;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class FirmwareDownloadServiceTests
{
    [Fact]
    public async Task DownloadsDirectHttpsFileVerifiesSha256AndImportsItIntoTheLibrary()
    {
        var root = CreateTempDirectory();
        var payload = CreatePayload(4096);
        var expected = Hash(payload);
        var handler = new DelegateHandler(_ => Success(payload, HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        var library = new FirmwareLibraryService(Path.Combine(root, "library"));
        using var downloader = new FirmwareDownloadService(
            library, client, Path.Combine(root, "downloads"));

        try
        {
            var result = await downloader.DownloadAndImportAsync(
                new Uri("https://firmware.example/vendor/device-build.zip"),
                expected,
                "Example",
                "Device X",
                "Global",
                "1.2.3",
                "Test download");

            Assert.Equal(expected, result.VerifiedSha256);
            Assert.Equal(payload.LongLength, result.DownloadedBytes);
            Assert.False(result.Resumed);
            var item = Assert.Single(await library.GetItemsAsync());
            Assert.Equal("Example", item.Manufacturer);
            Assert.Equal("Device X", item.Model);
            Assert.Equal("Global", item.Region);
            var integrity = await library.VerifyAsync(item.Id);
            Assert.True(integrity.FileExists);
            Assert.True(integrity.Sha256Matches);
            Assert.Equal(expected, integrity.ActualSha256);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InterruptedDownloadResumesFromTheLastWrittenByteAndImportsOnce()
    {
        var root = CreateTempDirectory();
        var payload = CreatePayload(32_000);
        var expected = Hash(payload);
        const int firstPartLength = 7_000;
        var requestCount = 0;
        var handler = new DelegateHandler(request =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new ThrowAfterBytesStream(payload, firstPartLength))
                };
                response.Headers.ETag = new EntityTagHeaderValue("\"firmware-v1\"");
                response.Content.Headers.ContentLength = payload.Length;
                return response;
            }

            Assert.Equal((long?)firstPartLength, request.Headers.Range?.Ranges.Single().From);
            Assert.Equal("\"firmware-v1\"", request.Headers.IfRange?.EntityTag?.Tag);
            var remaining = payload[firstPartLength..];
            var resumed = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(remaining)
            };
            resumed.Headers.ETag = new EntityTagHeaderValue("\"firmware-v1\"");
            resumed.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                firstPartLength, payload.Length - 1, payload.Length);
            return resumed;
        });
        using var client = new HttpClient(handler);
        var library = new FirmwareLibraryService(Path.Combine(root, "library"));
        using var downloader = new FirmwareDownloadService(
            library, client, Path.Combine(root, "downloads"));

        try
        {
            await Assert.ThrowsAsync<IOException>(() => downloader.DownloadAndImportAsync(
                new Uri("https://firmware.example/vendor/device-build.zip"),
                expected,
                "Example",
                "Device X",
                "Global",
                "1.2.3",
                "Resumable transfer"));

            var partialPath = Path.Combine(root, "downloads", expected + ".part");
            Assert.True(File.Exists(partialPath));
            Assert.Equal(firstPartLength, new FileInfo(partialPath).Length);
            Assert.Empty(await library.GetItemsAsync());

            var result = await downloader.DownloadAndImportAsync(
                new Uri("https://firmware.example/vendor/device-build.zip"),
                expected,
                "Example",
                "Device X",
                "Global",
                "1.2.3",
                "Resumed transfer");

            Assert.True(result.Resumed);
            Assert.Equal(payload.LongLength, result.DownloadedBytes);
            Assert.Equal(2, requestCount);
            Assert.Single(await library.GetItemsAsync());
            Assert.False(File.Exists(partialPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ServerIgnoringRangeRestartsFromZeroInsteadOfAppendingNewBytes()
    {
        var root = CreateTempDirectory();
        var payload = CreatePayload(5000);
        var expected = Hash(payload);
        var calls = 0;
        var handler = new DelegateHandler(request =>
        {
            calls++;
            if (calls == 1)
            {
                var first = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new ThrowAfterBytesStream(payload, 1200))
                };
                first.Content.Headers.ContentLength = payload.Length;
                first.Headers.ETag = new EntityTagHeaderValue("\"old-version\"");
                return first;
            }

            Assert.Equal((long?)1200, request.Headers.Range?.Ranges.Single().From);
            var fresh = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
            fresh.Content.Headers.ContentLength = payload.Length;
            fresh.Headers.ETag = new EntityTagHeaderValue("\"new-version\"");
            return fresh;
        });
        using var client = new HttpClient(handler);
        var library = new FirmwareLibraryService(Path.Combine(root, "library"));
        using var downloader = new FirmwareDownloadService(library, client, Path.Combine(root, "downloads"));

        try
        {
            await Assert.ThrowsAsync<IOException>(() => downloader.DownloadAndImportAsync(
                new Uri("https://vendor.example/full.zip"), expected,
                "Vendor", "Device", "", "", ""));

            var result = await downloader.DownloadAndImportAsync(
                new Uri("https://vendor.example/full.zip"), expected,
                "Vendor", "Device", "", "", "");

            Assert.False(result.Resumed);
            Assert.Equal(payload.LongLength, result.DownloadedBytes);
            var item = Assert.Single(await library.GetItemsAsync());
            var integrity = await library.VerifyAsync(item.Id);
            Assert.True(integrity.Sha256Matches);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsRedirectInsteadOfFollowingAnUnverifiedHost()
    {
        var root = CreateTempDirectory();
        var payload = CreatePayload(128);
        var expected = Hash(payload);
        var handler = new DelegateHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://cdn.example/firmware.zip");
            return response;
        });
        using var client = new HttpClient(handler);
        var library = new FirmwareLibraryService(Path.Combine(root, "library"));
        using var downloader = new FirmwareDownloadService(library, client, Path.Combine(root, "downloads"));

        try
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
                downloader.DownloadAndImportAsync(
                    new Uri("https://vendor.example/firmware.zip"), expected,
                    "Vendor", "Device", "", "", ""));

            Assert.Contains("إعادة توجيه", error.Message);
            Assert.Empty(await library.GetItemsAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsSha256MismatchAndDeletesTheUntrustedPartialFile()
    {
        var root = CreateTempDirectory();
        var payload = CreatePayload(2048);
        var wrongHash = new string('0', 64);
        var handler = new DelegateHandler(_ => Success(payload, HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        var library = new FirmwareLibraryService(Path.Combine(root, "library"));
        using var downloader = new FirmwareDownloadService(library, client, Path.Combine(root, "downloads"));

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAndImportAsync(
                new Uri("https://vendor.example/firmware.zip"), wrongHash,
                "Vendor", "Device", "", "", ""));

            Assert.Empty(await library.GetItemsAsync());
            Assert.False(File.Exists(Path.Combine(root, "downloads", wrongHash + ".part")));
            Assert.False(File.Exists(Path.Combine(root, "downloads", wrongHash + ".resume.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("http://vendor.example/file.zip")]
    [InlineData("https://user:password@vendor.example/file.zip")]
    [InlineData("https://127.0.0.1/file.zip")]
    [InlineData("https://vendor.example:8443/file.zip")]
    [InlineData("https://vendor.example/file.zip#fragment")]
    public async Task RejectsInsecureOrAmbiguousSourceUris(string source)
    {
        var root = CreateTempDirectory();
        var library = new FirmwareLibraryService(Path.Combine(root, "library"));
        using var downloader = new FirmwareDownloadService(
            library, new HttpClient(new DelegateHandler(_ => Success([], HttpStatusCode.OK))),
            Path.Combine(root, "downloads"));

        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => downloader.DownloadAndImportAsync(
                new Uri(source), new string('a', 64),
                "Vendor", "Device", "", "", ""));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsNonFirmwareFileExtensionsAndMalformedHashesBeforeNetworkAccess()
    {
        var root = CreateTempDirectory();
        var requests = 0;
        var library = new FirmwareLibraryService(Path.Combine(root, "library"));
        using var downloader = new FirmwareDownloadService(
            library,
            new HttpClient(new DelegateHandler(_ =>
            {
                requests++;
                return Success([1, 2, 3], HttpStatusCode.OK);
            })),
            Path.Combine(root, "downloads"));

        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => downloader.DownloadAndImportAsync(
                new Uri("https://vendor.example/firmware.exe"), new string('a', 64),
                "Vendor", "Device", "", "", ""));
            await Assert.ThrowsAsync<ArgumentException>(() => downloader.DownloadAndImportAsync(
                new Uri("https://vendor.example/firmware.zip"), "not-a-hash",
                "Vendor", "Device", "", "", ""));
            Assert.Equal(0, requests);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsAContentRangeThatDoesNotBeginAtTheCurrentPartialLength()
    {
        var root = CreateTempDirectory();
        var payload = CreatePayload(4000);
        var expected = Hash(payload);
        var calls = 0;
        var handler = new DelegateHandler(_ =>
        {
            calls++;
            if (calls == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new ThrowAfterBytesStream(payload, 1000))
                };
                response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                response.Content.Headers.ContentLength = payload.Length;
                return response;
            }

            var response2 = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload[1000..])
            };
            response2.Content.Headers.ContentRange = new ContentRangeHeaderValue(900, payload.Length - 1, payload.Length);
            return response2;
        });
        using var client = new HttpClient(handler);
        var library = new FirmwareLibraryService(Path.Combine(root, "library"));
        using var downloader = new FirmwareDownloadService(library, client, Path.Combine(root, "downloads"));

        try
        {
            await Assert.ThrowsAsync<IOException>(() => downloader.DownloadAndImportAsync(
                new Uri("https://vendor.example/file.zip"), expected,
                "Vendor", "Device", "", "", ""));
            await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAndImportAsync(
                new Uri("https://vendor.example/file.zip"), expected,
                "Vendor", "Device", "", "", ""));
            Assert.Empty(await library.GetItemsAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static HttpResponseMessage Success(byte[] payload, HttpStatusCode status)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(payload)
        };
        return response;
    }

    private static byte[] CreatePayload(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < bytes.Length; index++)
            bytes[index] = (byte)((index * 37 + 13) % 251);
        return bytes;
    }

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-fw-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(send(request));
        }
    }

    private sealed class ThrowAfterBytesStream(byte[] bytes, int maximumBytes)
        : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = maximumBytes - (int)Position;
            if (remaining <= 0)
                throw new IOException("Simulated network interruption.");
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, remaining)], cancellationToken);
        }
    }
}
