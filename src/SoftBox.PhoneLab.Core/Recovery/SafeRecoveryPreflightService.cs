using SoftBox.PhoneLab.Core.Firmware;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Core.Recovery;

public enum RecoveryCheckState
{
    Passed,
    Failed,
    Unknown,
    Blocked
}

public sealed record RecoveryPreflightCheck(
    string Id,
    string Title,
    RecoveryCheckState State,
    string Evidence,
    string RequiredAction);

public sealed record SafeRecoveryPreflightReport(
    string DeviceLabel,
    string FirmwareLabel,
    DateTimeOffset EvaluatedAtUtc,
    bool CanProceed,
    IReadOnlyList<RecoveryPreflightCheck> Checks,
    IReadOnlyList<string> Limitations);

/// <summary>
/// Conservative, inspect-only preflight for firmware recovery. This service cannot enable or
/// perform flashing. Authenticity, exact-model compatibility, rollback policy and backup proof
/// remain blocked until verifiable trusted sources and device-specific rules exist.
/// </summary>
public static class SafeRecoveryPreflightService
{
    private static readonly string[] UnknownMarkers = ["", "غير متاح", "unknown", "n/a", "not available"];

    public static SafeRecoveryPreflightReport Evaluate(
        DeviceSnapshot? device,
        bool adbAuthorized,
        FirmwareLibraryItem? firmware,
        FirmwareIntegrityResult? integrity)
    {
        var checks = new List<RecoveryPreflightCheck>();
        var limitations = new List<string>
        {
            "هذه شاشة فحص مسبق فقط؛ لا توجد عملية Flash أو Recovery في هذا المسار.",
            "SHA-256 المحلي يثبت تطابق الملف مع سجل الاستيراد، ولا يثبت هوية الناشر أو المصدر الرسمي.",
            "لا تتوفر حاليًا قاعدة توافق رسمية وموقعة تربط ملف Firmware بكل موديل ولوحة ومنطقة وإصدار Bootloader.",
            "سياسة Anti-Rollback ومتطلبات Download Mode/EDL/BROM لا يمكن استنتاجها من اسم الملف أو خصائص getprop وحدها."
        };

        checks.Add(new RecoveryPreflightCheck(
            "device-authorization",
            "اتصال الجهاز وتصريح ADB",
            adbAuthorized ? RecoveryCheckState.Passed : RecoveryCheckState.Failed,
            adbAuthorized ? "الجهاز المحدد مصرح به عبر ADB." : "لم يتم تأكيد تصريح ADB للجهاز المحدد.",
            adbAuthorized ? "لا يلزم إجراء إضافي لهذا الفحص." : "وصل الجهاز ووافق على USB debugging ثم أعد الفحص."));

        var manufacturer = device?.Manufacturer ?? "";
        var model = device?.ModelNumber;
        if (IsUnknown(model))
            model = device?.Model;
        var hasIdentity = !IsUnknown(manufacturer) && !IsUnknown(model);
        checks.Add(new RecoveryPreflightCheck(
            "device-identity",
            "هوية الشركة والموديل المقروءة من الهاتف",
            hasIdentity ? RecoveryCheckState.Passed : RecoveryCheckState.Unknown,
            hasIdentity ? $"Manufacturer={manufacturer}; Model={model}" : "الشركة أو الموديل غير متاحين من بيانات النظام الحالية.",
            hasIdentity ? "راجع رقم الطراز المطبوع على الجهاز عند الحاجة." : "اقرأ بيانات الجهاز مجددًا ولا تخمّن الموديل."));

        var firmwarePresent = firmware is not null;
        checks.Add(new RecoveryPreflightCheck(
            "firmware-selected",
            "اختيار ملف Firmware من المكتبة المحلية",
            firmwarePresent ? RecoveryCheckState.Passed : RecoveryCheckState.Failed,
            firmwarePresent ? $"{firmware!.Manufacturer} {firmware.Model}; {firmware.FileName}; {firmware.Version}" : "لم يتم اختيار ملف من مكتبة Firmware.",
            firmwarePresent ? "استكمل فحوص التطابق والبصمة." : "استورد الملف الذي تريد فحصه إلى Firmware Center أولًا."));

        var exactManufacturer = firmwarePresent && hasIdentity &&
            firmware!.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase);
        var exactModel = firmwarePresent && hasIdentity &&
            firmware!.Model.Equals(model, StringComparison.OrdinalIgnoreCase);
        checks.Add(new RecoveryPreflightCheck(
            "manufacturer-match",
            "تطابق الشركة في بيانات المكتبة مع الجهاز",
            exactManufacturer ? RecoveryCheckState.Passed : RecoveryCheckState.Failed,
            exactManufacturer
                ? $"تطابق الاسم المدخل: {manufacturer}."
                : $"Manufacturer الجهاز: {manufacturer}; Manufacturer الملف: {firmware?.Manufacturer ?? "غير محدد"}.",
            exactManufacturer ? "استمر في الفحوص التالية." : "تحقق من الشركة من مصدر رسمي؛ لا يكفي تغيير النص يدويًا لإثبات المصدر."));
        checks.Add(new RecoveryPreflightCheck(
            "model-match",
            "تطابق الموديل الدقيق",
            exactModel ? RecoveryCheckState.Passed : RecoveryCheckState.Failed,
            exactModel
                ? $"تطابق الاسم المدخل: {model}."
                : $"Model الجهاز: {model ?? "غير متاح"}; Model الملف: {firmware?.Model ?? "غير محدد"}.",
            exactModel ? "يلزم مع ذلك سجل توافق موثق قبل أي استعادة." : "لا تستخدم الملف حتى يتم إثبات تطابق رقم الطراز واللوحة من مصدر موثوق."));

        var hashVerified = integrity is
        {
            FileExists: true,
            SizeMatches: true,
            Sha256Matches: true
        } && firmware is not null && integrity.ItemId == firmware.Id;
        checks.Add(new RecoveryPreflightCheck(
            "local-file-integrity",
            "سلامة الملف المحلي وحجم الملف وSHA-256",
            hashVerified ? RecoveryCheckState.Passed : RecoveryCheckState.Failed,
            integrity is null
                ? "لم تتم إعادة قراءة الملف وحساب بصمته."
                : $"{integrity.Message} SHA-256 match={integrity.Sha256Matches}; size match={integrity.SizeMatches}; exists={integrity.FileExists}.",
            hashVerified ? "هذه النتيجة لا تثبت هوية الناشر؛ استمر في مراجعة المصدر والتوافق." : "أعد استيراد الملف أو تحقق من فقده أو تغيّره."));

        checks.Add(new RecoveryPreflightCheck(
            "publisher-authenticity",
            "التحقق من المصدر الرسمي وتوقيع الناشر",
            RecoveryCheckState.Blocked,
            "لا توجد خدمة موثوقة حاليًا للتحقق من توقيع ناشر Firmware أو جذر الثقة للمصدر.",
            "لا تستخدم الملف للتفليش حتى تتوفر سلسلة ثقة وتوقيع موثقان."));
        checks.Add(new RecoveryPreflightCheck(
            "device-compatibility",
            "توافق ملف Firmware مع الطراز واللوحة والمنطقة",
            RecoveryCheckState.Blocked,
            "لا توجد قاعدة توافق رسمية موقعة لهذا الملف والجهاز؛ تطابق النصوص غير كافٍ.",
            "يلزم سجل رسمي موثوق يطابق model/product/board/region/build/bootloader."));
        checks.Add(new RecoveryPreflightCheck(
            "anti-rollback",
            "قيود الرجوع إلى إصدار أقدم ومتطلبات Bootloader",
            RecoveryCheckState.Blocked,
            "قيمة Anti-Rollback وسياسة OEM لم يتم التحقق منهما من مصدر موثوق.",
            "لا تحاول الرجوع إلى إصدار أقدم قبل التحقق الرسمي من سياسة الجهاز."));
        checks.Add(new RecoveryPreflightCheck(
            "verified-backup",
            "وجود نسخة احتياطية حديثة ومتحقق منها لهذا الجهاز",
            RecoveryCheckState.Unknown,
            "لم يتم ربط سجل النسخ الاحتياطي بهوية الجهاز وباختبار استعادة قابل للتحقق في هذا الفحص.",
            "أنشئ نسخة احتياطية وتحقق من إمكانية قراءة ملفاتها قبل أي عملية قد تفقد البيانات."));

        return new SafeRecoveryPreflightReport(
            hasIdentity ? $"{manufacturer} {model}" : "Unknown device",
            firmwarePresent ? $"{firmware!.Manufacturer} {firmware.Model} — {firmware.FileName}" : "No firmware selected",
            DateTimeOffset.UtcNow,
            CanProceed: false,
            checks,
            limitations);
    }

    private static bool IsUnknown(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           UnknownMarkers.Any(marker => value.Trim().Equals(marker, StringComparison.OrdinalIgnoreCase));
}
