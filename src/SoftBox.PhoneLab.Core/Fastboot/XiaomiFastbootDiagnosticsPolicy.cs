namespace SoftBox.PhoneLab.Core.Fastboot;

public sealed record XiaomiFastbootDiagnosticsReport(
    bool IsXiaomiDevice,
    bool SelectedSerialMatches,
    string DeviceLabel,
    string? Product,
    string? Variant,
    string? HardwareRevision,
    string? BootloaderVersion,
    string? CurrentSlot,
    string LockState,
    string SecureBootState,
    IReadOnlyDictionary<string, string> RollbackVariables,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> Warnings,
    bool FlashAllowed);

/// <summary>
/// Xiaomi/Redmi/POCO-specific read-only interpretation of a selected Fastboot session.
/// It deliberately does not provide Xiaomi authorization, flash, unlock, erase or relock commands.
/// </summary>
public static class XiaomiFastbootDiagnosticsPolicy
{
    public static bool IsXiaomiManufacturer(string? value)
    {
        var manufacturer = (value ?? "").Trim();
        return manufacturer.Contains("xiaomi", StringComparison.OrdinalIgnoreCase) ||
               manufacturer.Equals("redmi", StringComparison.OrdinalIgnoreCase) ||
               manufacturer.Equals("poco", StringComparison.OrdinalIgnoreCase);
    }

    public static XiaomiFastbootDiagnosticsReport Evaluate(
        string? manufacturer,
        string expectedSerial,
        FastbootDevice? detectedDevice,
        FastbootStateReport? state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSerial);
        var isXiaomi = IsXiaomiManufacturer(manufacturer);
        var serialMatches = detectedDevice is not null &&
            detectedDevice.Serial.Equals(expectedSerial, StringComparison.OrdinalIgnoreCase);
        var findings = new List<string>();
        var warnings = new List<string>();

        if (!isXiaomi)
            findings.Add("الشركة التي أكدها Android ليست Xiaomi/Redmi/POCO؛ تم رفض تفسير ملف Xiaomi.");
        else
            findings.Add("هوية الشركة تأكدت من خصائص Android قبل الانتقال إلى Fastboot.");

        if (detectedDevice is null)
            findings.Add("لم يتم العثور على جلسة Fastboot للجهاز المحدد.");
        else if (!serialMatches)
            findings.Add("Serial المكتشف في Fastboot لا يطابق الهاتف الذي بدأ منه الفني العملية.");
        else
            findings.Add("Serial في Fastboot يطابق الهاتف الذي اختاره الفني.");

        if (state is not null && serialMatches && isXiaomi)
        {
            findings.Add(string.IsNullOrWhiteSpace(state.Product)
                ? "Fastboot لم يرجع قيمة product."
                : "Product: " + state.Product);
            findings.Add(state.RollbackVariables.Count > 0
                ? $"تم رصد {state.RollbackVariables.Count} متغير Anti-Rollback خام."
                : "لا توجد متغيرات Anti-Rollback معروفة في getvar all؛ هذا لا يعني غياب الحماية.");
            warnings.AddRange(state.Warnings);
        }

        if (!isXiaomi || !serialMatches || state is null)
            warnings.Add("لم يتم اعتماد بيانات هذا الاتصال كتقرير Xiaomi صالح.");

        warnings.Add("النتيجة قراءة getvar فقط؛ لا تثبت صلاحية فتح Bootloader أو توافق Firmware.");
        warnings.Add("أي عمليات Unlock/Flash/Erase/Format/Relock غير متاحة من هذا التقرير.");

        return new XiaomiFastbootDiagnosticsReport(
            isXiaomi,
            serialMatches,
            detectedDevice?.Details ?? "",
            state?.Product,
            state?.Variant,
            state?.HardwareRevision,
            state?.BootloaderVersion,
            state?.CurrentSlot,
            state?.LockState.ToString() ?? "Unknown",
            state?.SecureBootState.ToString() ?? "Unknown",
            state?.RollbackVariables ?? new Dictionary<string, string>(),
            findings,
            warnings,
            FlashAllowed: false);
    }
}
