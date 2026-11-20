using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SoftBox.PhoneLab.Core.Firmware;

public sealed record FirmwareDownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    bool Resumed,
    string Status);

public sealed record FirmwareDownloadResult(
    FirmwareLibraryItem LibraryItem,
    long DownloadedBytes,
    string VerifiedSha256,
    bool Resumed);

/// <summary>
/// Downloads a user-supplied direct HTTPS firmware URL, supports HTTP Range resume,
/// requires a user-supplied trusted SHA-256, then imports the verified bytes into the
/// local FirmwareLibraryService. Redirects and non-HTTPS sources are deliberately rejected.
/// </summary>
public sealed class FirmwareDownloadService
{
    public const long MaximumDownloadBytes = 16L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".img", ".bin", ".tar", ".md5", ".ofp", ".ozip", ".payload"
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly FirmwareLibraryService _library;
    private readonly HttpClient _http;
    private readonly string _downloadRoot;
    private readonly bool _ownsHttpClient;

    public FirmwareDownloadService(
        FirmwareLibraryService library,
        HttpClient? httpClient = null,
        string? downloadRoot = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _downloadRoot = Path.GetFullPath(downloadRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "FirmwareDownloads"));
        Directory.CreateDirectory(_downloadRoot);

        if (httpClient is null)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                UseDefaultCredentials = false
            };
            _http = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            _ownsHttpClient = true;
        }
        else
        {
            _http = httpClient;
        }
    }

    public async Task<FirmwareDownloadResult> DownloadAndImportAsync(
        Uri sourceUri,
        string expectedSha256,
        string manufacturer,
        string model,
        string region,
        string version,
        string notes,
        string? sourceDescription = null,
        IProgress<FirmwareDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSourceUri(sourceUri);
        expectedSha256 = NormalizeExpectedSha256(expectedSha256);
        var expectedBytes = Convert.FromHexString(expectedSha256);
        var extension = GetSupportedExtension(sourceUri);
        var sourceFingerprint = ComputeSourceFingerprint(sourceUri);

        var partialPath = Path.Combine(_downloadRoot, expectedSha256 + ".part");
        var metadataPath = Path.Combine(_downloadRoot, expectedSha256 + ".resume.json");
        var metadata = await LoadResumeMetadataAsync(metadataPath, cancellationToken);
        var partialExists = File.Exists(partialPath);

        if (partialExists && (metadata is null ||
                              string.IsNullOrWhiteSpace(metadata.SourceFingerprint) ||
                              !CryptographicOperations.FixedTimeEquals(
                                  Encoding.ASCII.GetBytes(metadata.SourceFingerprint),
                                  Encoding.ASCII.GetBytes(sourceFingerprint))))
        {
            TryDelete(partialPath);
            TryDelete(metadataPath);
            metadata = null;
            partialExists = false;
        }

        var currentLength = partialExists ? new FileInfo(partialPath).Length : 0L;
        if (currentLength > MaximumDownloadBytes)
        {
            TryDelete(partialPath);
            TryDelete(metadataPath);
            throw new InvalidDataException("الملف الجزئي تجاوز حد تنزيل Firmware البالغ 16 GiB.");
        }

        var resumed = currentLength > 0;
        long? totalBytes = null;
        long finalLength;

        // A previously completed partial is reusable after a crash between hashing and import.
        if (currentLength > 0 &&
            CryptographicOperations.FixedTimeEquals(
                await ComputeSha256Async(partialPath, cancellationToken),
                expectedBytes))
        {
            totalBytes = currentLength;
            finalLength = currentLength;
            progress?.Report(new FirmwareDownloadProgress(
                currentLength, totalBytes, true, "تم العثور على تنزيل مكتمل سابقًا؛ يعاد التحقق منه واستيراده."));
        }
        else
        {
            if (currentLength > 0 &&
                (metadata is null ||
                 (string.IsNullOrWhiteSpace(metadata.ETag) &&
                  string.IsNullOrWhiteSpace(metadata.LastModifiedUtc))))
            {
                // Without a validator, a resumed range could combine two different server versions.
                TryDelete(partialPath);
                currentLength = 0;
                resumed = false;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromHours(6));

            using var response = await SendDownloadRequestAsync(
                sourceUri,
                currentLength,
                metadata,
                timeout.Token);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && currentLength > 0)
            {
                // The server no longer has the requested range. Restart once from byte zero.
                TryDelete(partialPath);
                TryDelete(metadataPath);
                currentLength = 0;
                resumed = false;
                using var retryResponse = await SendDownloadRequestAsync(
                    sourceUri, 0, null, timeout.Token);
                totalBytes = await SaveResponseAsync(
                    retryResponse, sourceFingerprint, metadataPath, partialPath,
                    currentLength, resumed, progress, timeout.Token);
            }
            else
            {
                totalBytes = await SaveResponseAsync(
                    response, sourceFingerprint, metadataPath, partialPath,
                    currentLength, resumed, progress, timeout.Token);
                resumed = currentLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            }

            finalLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            if (finalLength is <= 0 or > MaximumDownloadBytes)
                throw new InvalidDataException("التنزيل الناتج فارغ أو يتجاوز حد 16 GiB.");

            if (totalBytes.HasValue && finalLength != totalBytes.Value)
                throw new IOException(
                    $"لم يكتمل تنزيل Firmware: وصل {finalLength:N0} من {totalBytes.Value:N0} بايت. أعد المحاولة لاستكمال التنزيل.");

            progress?.Report(new FirmwareDownloadProgress(
                finalLength, totalBytes ?? finalLength, resumed, "اكتمل تنزيل الملف؛ جارٍ التحقق من SHA-256..."));
        }

        var actualBytes = await ComputeSha256Async(partialPath, cancellationToken);
        var actualSha256 = Convert.ToHexString(actualBytes).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
        {
            TryDelete(partialPath);
            TryDelete(metadataPath);
            throw new InvalidDataException(
                $"فشل التحقق من SHA-256. المتوقع {expectedSha256}، والمحسوب {actualSha256}. حُذف الملف غير المطابق.");
        }

        var stagePath = Path.Combine(
            _downloadRoot,
            "verified-" + Guid.NewGuid().ToString("N") + extension);
        File.Move(partialPath, stagePath, overwrite: false);
        try
        {
            var item = await _library.ImportAsync(
                stagePath,
                manufacturer,
                model,
                region,
                version,
                $"User-supplied HTTPS source ({sourceUri.IdnHost})" +
                    (string.IsNullOrWhiteSpace(sourceDescription) ? "" : "; " + sourceDescription.Trim()) +
                    "; SHA-256 verified against the supplied checksum.",
                notes,
                cancellationToken);
            TryDelete(stagePath);
            TryDelete(metadataPath);
            return new FirmwareDownloadResult(
                item,
                finalLength,
                actualSha256,
                resumed);
        }
        catch
        {
            // Keep the verified payload resumable/re-importable if local library insertion fails.
            if (File.Exists(stagePath) && !File.Exists(partialPath))
                File.Move(stagePath, partialPath, overwrite: false);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendDownloadRequestAsync(
        Uri sourceUri,
        long offset,
        ResumeMetadata? metadata,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        request.Headers.UserAgent.ParseAdd("SoftBox-PhoneLab-FirmwareDownloader/1.0");

        if (offset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(offset, null);
            if (metadata is not null && !string.IsNullOrWhiteSpace(metadata.ETag) &&
                EntityTagHeaderValue.TryParse(metadata.ETag, out var tag) && !tag.IsWeak)
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(tag);
            }
            else if (metadata is not null &&
                     DateTimeOffset.TryParse(
                         metadata.LastModifiedUtc,
                         CultureInfo.InvariantCulture,
                         DateTimeStyles.None,
                         out var lastModified))
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(lastModified);
            }
        }

        var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if ((int)response.StatusCode is >= 300 and < 400)
        {
            response.Dispose();
            throw new HttpRequestException(
                "رفض SoftBox إعادة توجيه رابط Firmware تلقائيًا. استخدم رابط HTTPS مباشرًا للملف النهائي من المصدر الذي تثق به.");
        }

        if (response.StatusCode is not HttpStatusCode.OK and
            not HttpStatusCode.PartialContent and
            not HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException(
                $"رفض خادم Firmware التنزيل برمز HTTP {(int)status} ({status}).");
        }

        return response;
    }

    private async Task<long?> SaveResponseAsync(
        HttpResponseMessage response,
        string sourceFingerprint,
        string metadataPath,
        string partialPath,
        long requestedOffset,
        bool requestedResume,
        IProgress<FirmwareDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            throw new HttpRequestException("لم يقبل خادم Firmware طلب الاستكمال؛ أعد المحاولة برابط مباشر صالح.");

        if (response.Content.Headers.ContentEncoding.Count > 0)
            throw new InvalidDataException("أعاد الخادم محتوى مضغوطًا تلقائيًا؛ لا يمكن ضمان مطابقة بايتات الملف الأصلي.");

        var isPartial = response.StatusCode == HttpStatusCode.PartialContent;
        long writeOffset = requestedOffset;
        if (isPartial)
        {
            var range = response.Content.Headers.ContentRange;
            if (range is null || !range.HasRange ||
                range.From != requestedOffset || !range.HasLength)
            {
                TryDelete(partialPath);
                throw new InvalidDataException("ترويسة Content-Range لا تطابق موضع الاستكمال؛ رُفض ضم أجزاء غير متطابقة.");
            }

            if (!range.To.HasValue || range.To.Value < range.From.Value ||
                range.To.Value - range.From.Value + 1 > MaximumDownloadBytes)
                throw new InvalidDataException("نطاق التنزيل الذي أرسله الخادم غير صالح أو أكبر من الحد الآمن.");

            if (response.Content.Headers.ContentLength.HasValue &&
                response.Content.Headers.ContentLength.Value != range.To - range.From + 1)
                throw new InvalidDataException("حجم محتوى الاستكمال لا يطابق ترويسة Content-Range.");

            writeOffset = range.From.Value;
        }
        else
        {
            // HTTP 200 means either a fresh request or the server ignored/invalidated Range.
            writeOffset = 0;
        }

        var totalBytes = response.Content.Headers.ContentRange?.Length;
        if (!totalBytes.HasValue && response.Content.Headers.ContentLength.HasValue)
            totalBytes = checked(writeOffset + response.Content.Headers.ContentLength.Value);
        if (totalBytes > MaximumDownloadBytes)
            throw new InvalidDataException("حجم ملف Firmware يتجاوز الحد المسموح 16 GiB.");
        if (totalBytes.HasValue && totalBytes.Value < writeOffset)
            throw new InvalidDataException("طول ملف Firmware المعلن أقل من موضع الاستكمال.");

        var responseEtag = response.Headers.ETag;
        var lastModified = response.Content.Headers.LastModified;
        await SaveResumeMetadataAsync(
            metadataPath,
            new ResumeMetadata(
                sourceFingerprint,
                responseEtag?.ToString(),
                lastModified?.ToString("O", CultureInfo.InvariantCulture)),
            cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        var mode = writeOffset > 0 ? FileMode.OpenOrCreate : FileMode.Create;
        await using var output = new FileStream(
            partialPath,
            mode,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (writeOffset > 0)
        {
            if (output.Length != writeOffset)
                throw new InvalidDataException("حجم الملف الجزئي تغير قبل الاستكمال؛ أعد المحاولة.");
            output.Position = writeOffset;
        }
        else
        {
            output.SetLength(0);
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[128 * 1024];
        var received = writeOffset;
        var reportTotal = totalBytes;
        progress?.Report(new FirmwareDownloadProgress(
            received, reportTotal, requestedResume && isPartial,
            isPartial ? "جارٍ استكمال تنزيل Firmware..." : "جارٍ تنزيل Firmware..."));

        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;

            received = checked(received + read);
            if (received > MaximumDownloadBytes ||
                (reportTotal.HasValue && received > reportTotal.Value))
                throw new InvalidDataException("تجاوز حجم التنزيل الحد أو الطول الذي أعلنه الخادم.");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            progress?.Report(new FirmwareDownloadProgress(
                received, reportTotal, requestedResume && isPartial,
                reportTotal.HasValue
                    ? $"تنزيل Firmware: {received:N0} / {reportTotal.Value:N0} بايت"
                    : $"تنزيل Firmware: {received:N0} بايت"));
        }

        await output.FlushAsync(cancellationToken);
        return totalBytes ?? received;
    }

    private static void ValidateSourceUri(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            System.Net.IPAddress.TryParse(uri.Host, out _))
            throw new ArgumentException(
                "مصدر Firmware يجب أن يكون رابط HTTPS مباشرًا على المنفذ 443، دون بيانات دخول أو IP مباشر أو Fragment. احصل عليه من بوابة الشركة المصنعة الرسمية.",
                nameof(uri));

        if (uri.AbsoluteUri.Length > 4096)
            throw new ArgumentException("رابط Firmware أطول من الحد المسموح.", nameof(uri));
    }

    private static string NormalizeExpectedSha256(string? value)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("أدخل SHA-256 موثوقًا مكوّنًا من 64 رقمًا سداسيًا.", nameof(value));
        return normalized.ToLowerInvariant();
    }

    private static string GetSupportedExtension(Uri sourceUri)
    {
        var fileName = Uri.UnescapeDataString(Path.GetFileName(sourceUri.AbsolutePath));
        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(extension))
            throw new ArgumentException(
                "يجب أن ينتهي مسار رابط Firmware بامتداد مدعوم: ZIP أو IMG أو BIN أو TAR أو MD5 أو OFP أو OZIP أو PAYLOAD.",
                nameof(sourceUri));
        return extension.ToLowerInvariant();
    }

    private static string ComputeSourceFingerprint(Uri uri)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<ResumeMetadata?> LoadResumeMetadataAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > 32 * 1024)
                return null;
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<ResumeMetadata>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task SaveResumeMetadataAsync(
        string path,
        ResumeMetadata metadata,
        CancellationToken cancellationToken)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static async Task<byte[]> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }

    private sealed record ResumeMetadata(
        string SourceFingerprint,
        string? ETag,
        string? LastModifiedUtc);
}
