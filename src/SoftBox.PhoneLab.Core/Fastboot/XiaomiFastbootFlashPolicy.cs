using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SoftBox.PhoneLab.Core.Fastboot;

public sealed record XiaomiFastbootFlashDecision(
    bool CanFlash,
    string Partition,
    string Evidence,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Fail-closed decision for a single authorized Xiaomi Fastboot image write.
/// Bootloader unlock is intentionally not exposed; Xiaomi account eligibility belongs to Mi Unlock.
/// </summary>
public static class XiaomiFastbootFlashPolicy
{
    private static readonly string[] AllowedPartitions =
    [
        "boot", "init_boot", "vendor_boot", "dtbo", "recovery"
    ];

    public static IReadOnlyList<string> SupportedPartitions => AllowedPartitions;

    public static XiaomiFastbootFlashDecision Evaluate(
        string? manufacturer,
        string expectedSerial,
        string? detectedSerial,
        string? deviceProduct,
        string? packageProduct,
        BootloaderLockState lockState,
        string? partition,
        string? imagePath,
        bool operatorConfirmedOfficialSource,
        bool operatorConfirmedBackup)
    {
        var blockers = new List<string>();
        var normalizedPartition = (partition ?? "").Trim().ToLowerInvariant();
        var normalizedDeviceProduct = (deviceProduct ?? "").Trim();
        var normalizedPackageProduct = (packageProduct ?? "").Trim();

        if (!XiaomiFastbootDiagnosticsPolicy.IsXiaomiManufacturer(manufacturer))
            blockers.Add("خصائص الجهاز لا تؤكد أن الشركة Xiaomi/Redmi/POCO.");
        if (!AdbSafe(expectedSerial) || !AdbSafe(detectedSerial) ||
            !string.Equals(expectedSerial, detectedSerial, StringComparison.Ordinal))
            blockers.Add("Serial المكتشف في Fastboot لا يطابق الهاتف الذي بدأ منه الفني العملية.");
        if (normalizedDeviceProduct.Length == 0 || normalizedPackageProduct.Length == 0 ||
            !normalizedDeviceProduct.Equals(normalizedPackageProduct, StringComparison.Ordinal))
            blockers.Add("Product الحزمة لا يطابق Product المقروء من Fastboot تطابقًا كاملًا.");
        if (lockState != BootloaderLockState.Unlocked)
            blockers.Add(lockState == BootloaderLockState.Locked
                ? "Bootloader مقفل. استخدم إجراء Xiaomi الرسمي وحقق أهلية الحساب؛ لا يمكن التفليش عبر هذه الواجهة."
                : "حالة Bootloader ليست Unlocked مؤكدة؛ تم منع الكتابة.");
        if (!AllowedPartitions.Contains(normalizedPartition, StringComparer.Ordinal))
            blockers.Add("القسم غير موجود في قائمة الأقسام المحدودة التي يدعمها SoftBox.");
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            blockers.Add("ملف صورة Firmware غير موجود.");
        else
        {
            var info = new FileInfo(imagePath);
            if (info.Length is <= 0 or > 2L * 1024 * 1024 * 1024 ||
                !Path.GetExtension(imagePath).Equals(".img", StringComparison.OrdinalIgnoreCase))
                blockers.Add("الصورة يجب أن تكون IMG غير فارغة ولا تتجاوز 2 GiB.");
        }
        if (!operatorConfirmedOfficialSource)
            blockers.Add("لم يؤكد الفني أن الصورة مأخوذة من مصدر OEM رسمي ومطابقة لهذا Product.");
        if (!operatorConfirmedBackup)
            blockers.Add("لم يؤكد الفني وجود نسخة احتياطية صالحة ووعيه بمخاطر فشل الإقلاع.");

        return new XiaomiFastbootFlashDecision(
            blockers.Count == 0,
            normalizedPartition,
            "قرار محدود لصورة IMG واحدة: هوية Xiaomi + نفس Serial + Product متطابق + Bootloader مفتوح + تأكيد المصدر والنسخة الاحتياطية.",
            blockers);
    }

    private static bool AdbSafe(string? serial)
        => !string.IsNullOrWhiteSpace(serial) &&
           serial.Length <= 128 &&
           serial.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');
}
