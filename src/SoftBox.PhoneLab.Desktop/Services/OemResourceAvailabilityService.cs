using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;

namespace SoftBox.PhoneLab.Desktop.Services;

public enum OemResourceAvailabilityStatus
{
    Reachable,
    NotFound,
    AccessRestricted,
    ServerError,
    RedirectLimitExceeded,
    InsecureRedirectBlocked,
    InvalidUrl,
    NetworkError
}

public sealed record OemResourceAvailabilityReport(
    string OriginalUrl,
    string FinalUrl,
    OemResourceAvailabilityStatus Status,
    HttpStatusCode? HttpStatusCode,
    string MethodUsed,
    int RedirectCount,
    string Explanation,
    string Limitation);

/// <summary>
/// Checks whether an official HTTPS OEM resource page can be reached.
/// It does not download a driver payload, install software, or infer model compatibility.
/// Redirects are followed manually and HTTPS downgrade is rejected.
/// </summary>
public sealed class OemResourceAvailabilityService : IDisposable
{
    private const int MaximumRedirects = 5;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly TimeSpan _requestTimeout;

    public OemResourceAvailabilityService(
        HttpClient? httpClient = null,
        TimeSpan? requestTimeout = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        });
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(12);
    }

    public async Task<OemResourceAvailabilityReport> CheckAsync(
        string? value,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return Report(
                value ?? string.Empty, value ?? string.Empty,
                OemResourceAvailabilityStatus.InvalidUrl, null, "NONE", 0,
                "المصدر ليس رابط HTTPS صالحًا؛ تم رفض فحصه.",
                "يقبل SoftBox روابط HTTPS فقط ولا يفتح أو يتصل بروابط HTTP أو مسارات غير مطلقة.");
        }

        var original = uri;
        var redirects = 0;
        var method = HttpMethod.Head;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);

        try
        {
            while (true)
            {
                using var request = new HttpRequestMessage(method, uri);
                request.Headers.UserAgent.ParseAdd("SoftBox-PhoneLab-ResourceCheck/1.0");
                if (method == HttpMethod.Get)
                {
                    // Some OEM servers reject HEAD. Ask for at most one byte and use
                    // ResponseHeadersRead so SoftBox never buffers a driver payload.
                    request.Headers.Range = new RangeHeaderValue(0, 0);
                    request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,*/*;q=0.1");
                }
                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if ((response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                     response.StatusCode == HttpStatusCode.NotImplemented) &&
                    method == HttpMethod.Head)
                {
                    method = HttpMethod.Get;
                    continue;
                }

                if (IsRedirect(response.StatusCode))
                {
                    if (redirects >= MaximumRedirects)
                    {
                        return Report(
                            original.AbsoluteUri, uri.AbsoluteUri,
                            OemResourceAvailabilityStatus.RedirectLimitExceeded,
                            response.StatusCode, method.Method, redirects,
                            "تجاوز المصدر الحد الأقصى للتحويلات؛ لم يتم اعتماد حالة الوصول النهائية.",
                            "تم إيقاف تتبع التحويلات بعد خمس خطوات لحماية الفحص من حلقات وإعادة توجيه غير منتهية.");
                    }

                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        return Report(
                            original.AbsoluteUri, uri.AbsoluteUri,
                            OemResourceAvailabilityStatus.NetworkError,
                            response.StatusCode, method.Method, redirects,
                            "أعاد المصدر تحويلًا دون عنوان Location صالح.",
                            "لم يتم اعتبار الصفحة متاحة لأن وجهة التحويل لم تُحدد.");
                    }

                    var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (!next.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    {
                        return Report(
                            original.AbsoluteUri, next.AbsoluteUri,
                            OemResourceAvailabilityStatus.InsecureRedirectBlocked,
                            response.StatusCode, method.Method, redirects + 1,
                            "حاول المصدر التحويل إلى عنوان غير HTTPS؛ تم منع متابعة الاتصال.",
                            "لا يتبع SoftBox تحويلات إلى HTTP أو مخططات أخرى، ولا ينزّل أي ملف من هذا الفحص.");
                    }

                    uri = next;
                    redirects++;
                    continue;
                }

                if ((int)response.StatusCode is >= 200 and <= 299)
                {
                    var explanation = method == HttpMethod.Head
                        ? "استجاب رابط المصدر الرسمي عبر HTTPS."
                        : "استجاب رابط المصدر الرسمي عبر HTTPS بعد أن رفض HEAD ونجح GET دون تنزيل محتوى الصفحة.";
                    return Report(
                        original.AbsoluteUri, uri.AbsoluteUri,
                        OemResourceAvailabilityStatus.Reachable,
                        response.StatusCode, method.Method, redirects,
                        explanation,
                        "هذه نتيجة وصول إلى صفحة/بوابة المصدر فقط؛ لا تثبت وجود ملف تعريف مباشر أو توافقه مع موديل الهاتف أو Windows.");
                }

                var status = response.StatusCode switch
                {
                    HttpStatusCode.NotFound or HttpStatusCode.Gone =>
                        OemResourceAvailabilityStatus.NotFound,
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                        OemResourceAvailabilityStatus.AccessRestricted,
                    _ when (int)response.StatusCode >= 500 =>
                        OemResourceAvailabilityStatus.ServerError,
                    _ => OemResourceAvailabilityStatus.NetworkError
                };
                var detail = status switch
                {
                    OemResourceAvailabilityStatus.NotFound => "أعاد المصدر حالة Not Found/Gone؛ قد يكون الرابط قد تغير أو أزيل.",
                    OemResourceAvailabilityStatus.AccessRestricted => "رفض المصدر الوصول المجهول أو يتطلب تسجيل الدخول/المنطقة.",
                    OemResourceAvailabilityStatus.ServerError => "أعاد خادم المصدر خطأ من فئة 5xx.",
                    _ => "لم يُرجع المصدر استجابة نجاح يمكن اعتمادها."
                };
                return Report(
                    original.AbsoluteUri, uri.AbsoluteUri, status,
                    response.StatusCode, method.Method, redirects, detail,
                    "فشل الوصول لا يثبت أن الشركة لا توفر تعريفًا؛ قد تمنع البوابة الفحص الآلي أو تتطلب متصفحًا/حسابًا/منطقة.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Report(
                original.AbsoluteUri, uri.AbsoluteUri,
                OemResourceAvailabilityStatus.NetworkError, null, method.Method, redirects,
                "انتهت مهلة الاتصال بالمصدر الرسمي.",
                "لم يتم تنزيل أو تثبيت أي ملف. تحقق من الاتصال أو افتح الصفحة يدويًا.");
        }
        catch (HttpRequestException ex)
        {
            return Report(
                original.AbsoluteUri, uri.AbsoluteUri,
                OemResourceAvailabilityStatus.NetworkError, null, method.Method, redirects,
                "تعذر الاتصال بالمصدر الرسمي: " + Limit(ex.Message, 240),
                "قد يكون السبب DNS أو TLS أو Proxy أو جدارًا ناريًا أو حجبًا من بوابة الشركة.");
        }
    }

    private static bool IsRedirect(HttpStatusCode code)
        => code is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static OemResourceAvailabilityReport Report(
        string original, string final, OemResourceAvailabilityStatus status,
        HttpStatusCode? code, string method, int redirects, string explanation, string limitation)
        => new(original, final, status, code, method, redirects, explanation, limitation);

    private static string Limit(string value, int maximum)
        => string.IsNullOrWhiteSpace(value) ? string.Empty :
            value.Length <= maximum ? value : value[..maximum] + "…";

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
