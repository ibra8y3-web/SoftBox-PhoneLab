using System.Net.Http;

namespace SoftBox.PhoneLab.Core.Diagnostics;

public sealed record ScreenMirrorFailureReport(
    string Code,
    string Title,
    string Cause,
    string RecommendedAction,
    bool Retryable,
    string TechnicalDetail);

/// <summary>
/// Turns real launch exceptions into actionable diagnostics. It never treats a failed
/// or missing transport as a successful screen session.
/// </summary>
public static class ScreenMirrorFailureAnalyzer
{
    public static ScreenMirrorFailureReport Analyze(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var detail = Limit(error.Message, 600);

        if (ContainsAny(detail, "device unauthorized", "unauthorized", "not authorized"))
            return Report("ADB_UNAUTHORIZED", "الهاتف لم يمنح تصريح ADB",
                "Android has not authorized this Windows computer for the selected ADB device.",
                "افتح قفل الهاتف، وفعّل USB debugging، ثم وافق على رسالة السماح لهذا الكمبيوتر. لا يمكن لـSoftBox تخطي موافقة الهاتف.",
                true, detail);

        if (ContainsAny(detail, "device offline", "offline"))
            return Report("ADB_OFFLINE", "قناة ADB للجهاز غير مستقرة",
                "ADB sees the selected device but reports it as offline.",
                "افصل كابل البيانات وأعد توصيله، ثم أعد تشغيل اتصال ADB من SoftBox. لا تحذف بيانات الهاتف ولا تغيّر وضع الإقلاع.",
                true, detail);

        if (ContainsAny(detail, "no devices/emulators found", "device not found", "no device with serial"))
            return Report("ADB_DEVICE_MISSING", "الجهاز غير موجود في قناة ADB",
                "The selected serial is no longer present in the ADB device inventory.",
                "أعد فحص الأجهزة، وتأكد من اختيار الهاتف الصحيح وأن كابل البيانات وتعريف USB يعملان.",
                true, detail);

        if (ContainsAny(detail, "adb server is out of date", "server version", "cannot bind", "failed to start daemon"))
            return Report("ADB_SERVER_CONFLICT", "يوجد تعارض في خادم ADB",
                "The ADB client/server versions differ or another process owns the ADB server port.",
                "أغلق أدوات Android الأخرى التي تشغّل ADB، ثم أعد فحص الاتصال باستخدام Platform-Tools المرفقة مع SoftBox.",
                true, detail);

        if (error is UnauthorizedAccessException)
            return Report("ADB_UNAUTHORIZED", "الهاتف لم يمنح تصريح ADB",
                "Windows may see the USB device, but Android has not authorized this computer for ADB.",
                "افتح قفل الهاتف، وفعّل USB debugging، ثم وافق على رسالة السماح لهذا الكمبيوتر. لا يمكن لـSoftBox تخطي موافقة الهاتف.",
                true, detail);

        if (error is HttpRequestException or TaskCanceledException)
            return Report("SCRCPY_DOWNLOAD_FAILED", "تعذر تنزيل مكوّن العرض",
                "The official scrcpy package could not be downloaded or the request timed out.",
                "تحقق من الإنترنت والجدار الناري ثم أعد المحاولة. لن يستخدم SoftBox ملفًا لم يجتز تحقق SHA-256.",
                true, detail);

        if (error is InvalidDataException)
            return Report("SCRCPY_INTEGRITY_FAILED", "فشل التحقق من سلامة حزمة العرض",
                "The downloaded archive or extracted runtime did not pass the expected integrity checks.",
                "احذف النسخة غير السليمة وأعد تنزيل الحزمة الرسمية بعد إعادة التحقق من SHA-256. لا تشغّل نسخة معدّلة أو غير موثوقة.",
                true, detail);

        if (error is FileNotFoundException or DirectoryNotFoundException)
            return Report("RUNTIME_MISSING", "ملف تشغيل العرض غير موجود",
                "A required ADB/scrcpy executable or runtime file could not be found.",
                "افتح مركز Offline للتحقق من وجود ADB وscrcpy وأعد تجهيز الحزمة الرسمية.",
                true, detail);

        if (error is PlatformNotSupportedException)
            return Report("PLATFORM_UNSUPPORTED", "مسار العرض غير مدعوم على هذا النظام",
                "The selected mirroring backend is not available on the current operating system.",
                "اختر موصل العرض المناسب لنظام الهاتف؛ scrcpy مخصص لأجهزة Android التي تتيح ADB.",
                false, detail);

        if (error is TimeoutException)
            return Report("WINDOW_TIMEOUT", "لم تظهر نافذة بث الشاشة",
                "The screen process did not create a usable window before the startup timeout.",
                "أعد توصيل كابل البيانات، وأغلق جلسة scrcpy قديمة، ثم حدّث اتصال ADB. افحص سجل الخطأ قبل تكرار المحاولة.",
                true, detail);

        if (error is InvalidOperationException &&
            detail.Contains("exited with code", StringComparison.OrdinalIgnoreCase))
            return Report("SCRCPY_EXITED", "أغلق scrcpy نفسه أثناء بدء العرض",
                "The scrcpy process exited before its window was attached.",
                "تحقق من إصدار Android وADB وتعريف USB، ثم راجع كود الخروج والتفاصيل الفنية أدناه.",
                true, detail);

        if (error is InvalidOperationException &&
            (detail.Contains("host is not ready", StringComparison.OrdinalIgnoreCase) ||
             detail.Contains("host handle", StringComparison.OrdinalIgnoreCase)))
            return Report("VIEW_HOST_NOT_READY", "مساحة عرض الهاتف لم تجهز بعد",
                "The WPF native screen host has not created its Windows handle yet.",
                "ارجع إلى Phone Link Studio وانتظر اكتمال تحميل الواجهة ثم أعد تشغيل العرض.",
                true, detail);

        return Report("SCREEN_MIRROR_UNKNOWN", "تعذر تشغيل عرض الشاشة",
            "The failure did not match a known transport, runtime or host diagnostic.",
            "احتفظ بالتفاصيل الفنية، وافحص حالة ADB وملفات Offline ثم أعد المحاولة بعد معالجة السبب.",
            true, detail);
    }

    private static bool ContainsAny(string value, params string[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static ScreenMirrorFailureReport Report(
        string code, string title, string cause, string action, bool retryable, string detail)
        => new(code, title, cause, action, retryable, detail);

    private static string Limit(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "لا توجد تفاصيل فنية إضافية.";
        var normalized = value.Trim();
        return normalized.Length <= max ? normalized : normalized[..max] + "…";
    }
}
