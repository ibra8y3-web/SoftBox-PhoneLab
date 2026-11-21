namespace SoftBox.PhoneLab.Core.Firmware;

public enum FirmwareCompatibilityState
{
    Passed,
    Failed,
    Unknown
}

public sealed record FirmwareCompatibilityCheck(
    string Name,
    FirmwareCompatibilityState State,
    string Evidence,
    string RequiredAction);

public sealed record FirmwareCompatibilityReport(
    string DeviceLabel,
    string FirmwareLabel,
    DateTimeOffset EvaluatedAtUtc,
    bool CanProceed,
    IReadOnlyList<FirmwareCompatibilityCheck> Checks,
    IReadOnlyList<string> Limitations);

/// <summary>
/// A fail-closed compatibility gate. Exact manufacturer/model mismatches fail; missing board,
/// region, bootloader, publisher-signature or anti-rollback evidence remains Unknown.
/// This policy never authorizes flashing by itself.
/// </summary>
public static class FirmwareCompatibilityPolicy
{
    public static FirmwareCompatibilityReport Evaluate(
        string? deviceManufacturer,
        string? deviceModel,
        string? deviceProduct,
        string? deviceBoard,
        string? deviceRegion,
        int? deviceRollbackIndex,
        string? firmwareManufacturer,
        string? firmwareModel,
        string? firmwareProduct,
        string? firmwareBoard,
        string? firmwareRegion,
        int? firmwareRollbackIndex,
        bool fileIntegrityVerified,
        bool publisherSignatureVerified)
    {
        var checks = new List<FirmwareCompatibilityCheck>
        {
            CompareExact("الشركة المصنعة", deviceManufacturer, firmwareManufacturer),
            CompareModel(deviceModel, deviceProduct, firmwareModel),
            CompareOptionalExact("Product", deviceProduct, firmwareProduct),
            CompareOptionalExact("Board / Hardware SKU", deviceBoard, firmwareBoard),
            CompareOptionalExact("المنطقة / CSC", deviceRegion, firmwareRegion),
            CompareRollback(deviceRollbackIndex, firmwareRollbackIndex),
            new("سلامة الملف المحلي",
                fileIntegrityVerified ? FirmwareCompatibilityState.Passed : FirmwareCompatibilityState.Failed,
                fileIntegrityVerified ? "تطابقت بصمة الملف وحجمه مع سجل المكتبة." : "لم يتم تأكيد سلامة الملف المحلي.",
                fileIntegrityVerified ? "لا يلزم إجراء." : "أعد استيراد الملف من مصدر موثوق ثم أعد حساب SHA-256."),
            new("توقيع الناشر",
                publisherSignatureVerified ? FirmwareCompatibilityState.Passed : FirmwareCompatibilityState.Unknown,
                publisherSignatureVerified ? "تم التحقق من توقيع ناشر موثوق." : "لا يوجد توقيع ناشر موثوق مثبت لهذا الملف.",
                "تحقق من توقيع الشركة عبر جذر ثقة مستقل.")
        };

        var canProceed = checks.All(check => check.State == FirmwareCompatibilityState.Passed);
        var limitations = new List<string>
        {
            "هذه بوابة توافق معلوماتية فقط وليست أمر Flash أو Unlock أو Erase.",
            "يجب التحقق من نسخة Bootloader وسياسة Anti-Rollback ومصدر الملف عبر بيانات OEM موثوقة.",
            "حتى مع نجاح كل الحقول، يجب تنفيذ أي استعادة عبر أداة الشركة الرسمية واتباع تحذيرات فقدان البيانات."
        };
        return new FirmwareCompatibilityReport(
            $"{NormalizeLabel(deviceManufacturer)} {NormalizeLabel(deviceModel)}".Trim(),
            $"{NormalizeLabel(firmwareManufacturer)} {NormalizeLabel(firmwareModel)}".Trim(),
            DateTimeOffset.UtcNow,
            canProceed,
            checks,
            limitations);
    }

    private static FirmwareCompatibilityCheck CompareExact(string name, string? device, string? firmware)
    {
        if (IsMissing(device) || IsMissing(firmware))
            return Unknown(name, "بيانات الشركة غير مكتملة.");
        var normalizedDevice = Normalize(device!);
        var normalizedFirmware = Normalize(firmware!);
        return normalizedDevice.Equals(normalizedFirmware, StringComparison.Ordinal)
            ? Passed(name, $"تطابق اسم الشركة: {device}.")
            : Failed(name, $"الشركة على الجهاز ({device}) تختلف عن بيانات الحزمة ({firmware}).");
    }

    private static FirmwareCompatibilityCheck CompareModel(string? deviceModel, string? deviceProduct, string? firmwareModel)
    {
        if (IsMissing(firmwareModel) || (IsMissing(deviceModel) && IsMissing(deviceProduct)))
            return Unknown("الموديل", "تعذر تحديد موديل دقيق من بيانات الجهاز أو الحزمة.");
        var accepted = new[] { deviceModel, deviceProduct }
            .Where(value => !IsMissing(value))
            .Select(value => Normalize(value!))
            .ToHashSet(StringComparer.Ordinal);
        var packageModel = Normalize(firmwareModel!);
        if (!accepted.Contains(packageModel))
            return Failed("الموديل", $"موديل الحزمة ({firmwareModel}) لا يطابق Model/Product المقروءين من الهاتف.");
        return Passed("الموديل", "تطابق موديل Firmware مع Model أو Product المقروءين من الهاتف.");
    }

    private static FirmwareCompatibilityCheck CompareOptionalExact(string name, string? device, string? firmware)
    {
        if (IsMissing(device) || IsMissing(firmware))
            return Unknown(name, "لا توجد قيمتان موثوقتان للمقارنة.");
        var normalizedDevice = Normalize(device!);
        var normalizedFirmware = Normalize(firmware!);
        return normalizedDevice.Equals(normalizedFirmware, StringComparison.Ordinal)
            ? Passed(name, "تطابق القيمة المقروءة مع بيانات الحزمة.")
            : Failed(name, $"القيمة على الهاتف ({device}) لا تطابق قيمة الحزمة ({firmware}).");
    }

    private static FirmwareCompatibilityCheck CompareRollback(int? device, int? firmware)
    {
        if (device is null || firmware is null)
            return Unknown("Anti-Rollback", "قيمة Anti-Rollback الموثوقة غير متاحة للجهاز أو الحزمة.");
        return firmware >= device
            ? Passed("Anti-Rollback", $"مؤشر الحزمة {firmware} ليس أقل من مؤشر الجهاز {device}.")
            : Failed("Anti-Rollback", $"مؤشر الحزمة {firmware} أقل من مؤشر الجهاز {device}؛ يُمنع الرجوع.");
    }

    private static FirmwareCompatibilityCheck Unknown(string name, string evidence)
        => new(name, FirmwareCompatibilityState.Unknown, evidence, "احصل على قيمة موثوقة من أداة الشركة أو وثائقها الرسمية.");
    private static FirmwareCompatibilityCheck Passed(string name, string evidence)
        => new(name, FirmwareCompatibilityState.Passed, evidence, "لا يلزم إجراء.");
    private static FirmwareCompatibilityCheck Failed(string name, string evidence)
        => new(name, FirmwareCompatibilityState.Failed, evidence, "لا تستخدم هذه الحزمة لهذا الجهاز.");

    private static bool IsMissing(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Equals("غير متاح", StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string value)
        => string.Concat(value.Trim().Where(char.IsLetterOrDigit)).ToLowerInvariant();
    private static string NormalizeLabel(string? value) => IsMissing(value) ? "غير محدد" : value!.Trim();
}
