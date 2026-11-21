using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SoftBox.PhoneLab.Core.Firmware;

public sealed record SamsungOdinAutomationEvidence(
    string ExpectedExecutablePath,
    string ActualExecutablePath,
    string WindowTitle,
    int StartButtonCount,
    bool StartButtonEnabled,
    bool HasConnectedComPort,
    IReadOnlyList<string> ExpectedPackageNames,
    IReadOnlyList<string> VisibleUiText,
    bool OperatorConfirmedCompatibility,
    bool OperatorConfirmedBackup)
{
    public bool StartButtonSupportsInvokePattern { get; init; }
    public bool OperatorConfirmedComPortMatchesSelectedDevice { get; init; }
}

public sealed record SamsungOdinAutomationDecision(
    bool CanInvokeStart,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> VerifiedEvidence);

/// <summary>
/// Fail-closed gate for accessibility-based Odin automation. It requires an exact executable path,
/// an Odin window, one enabled Start button, an attached COM port, visible selected package names,
/// and explicit technician acknowledgements. It does not authenticate firmware or guarantee flash success.
/// </summary>
public static class SamsungOdinAutomationPolicy
{
    public static SamsungOdinAutomationDecision Evaluate(SamsungOdinAutomationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var blockers = new List<string>();
        var verified = new List<string>();

        var expected = NormalizePath(evidence.ExpectedExecutablePath);
        var actual = NormalizePath(evidence.ActualExecutablePath);
        if (expected is null || actual is null ||
            !expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
            blockers.Add("مسار عملية Odin الفعلية لا يطابق ملف Odin.exe الذي اختاره الفني.");
        else
            verified.Add("مسار ملف Odin يطابق العملية المفتوحة.");

        if (string.IsNullOrWhiteSpace(evidence.WindowTitle) ||
            !evidence.WindowTitle.Contains("Odin", StringComparison.OrdinalIgnoreCase))
            blockers.Add("تعذر تأكيد أن النافذة المحددة هي نافذة Odin.");
        else
            verified.Add("عنوان النافذة يحدد Odin.");

        if (evidence.StartButtonCount != 1)
            blockers.Add("لم يتم العثور على زر Start واحد فقط دون التباس.");
        else if (!evidence.StartButtonEnabled)
            blockers.Add("زر Start موجود لكنه غير مفعّل.");
        else if (!evidence.StartButtonSupportsInvokePattern)
            blockers.Add("زر Start لا يدعم InvokePattern؛ لا يتم استخدام النقر بالإحداثيات كبديل.");
        else
            verified.Add("زر Start واحد ظاهر ومفعّل ويدعم InvokePattern.");

        if (!evidence.HasConnectedComPort)
            blockers.Add("لم يظهر منفذ COM متصل داخل شجرة عناصر Odin.");
        else
            verified.Add("ظهر منفذ COM في واجهة Odin.");

        var expectedPackages = evidence.ExpectedPackageNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => Path.GetFileName(name) ?? string.Empty)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (expectedPackages.Length == 0)
            blockers.Add("لا توجد أسماء حزم Firmware فُحصت لربطها بواجهة Odin.");

        var visible = string.Join("\n", evidence.VisibleUiText ?? Array.Empty<string>());
        foreach (var package in expectedPackages)
        {
            if (!visible.Contains(package, StringComparison.OrdinalIgnoreCase))
                blockers.Add("لم يظهر اسم الحزمة المحددة داخل واجهة Odin: " + package);
            else
                verified.Add("اسم الحزمة ظاهر داخل Odin: " + package);
        }

        if (!evidence.OperatorConfirmedCompatibility)
            blockers.Add("لم يؤكد الفني مطابقة Model / CSC / Binary / Anti-Rollback.");
        else
            verified.Add("الفني أكد توافق الموديل والمنطقة وإصدار Bootloader.");

        if (!evidence.OperatorConfirmedBackup)
            blockers.Add("لم يؤكد الفني وجود نسخة احتياطية صالحة.");
        else
            verified.Add("الفني أكد وجود نسخة احتياطية.");

        if (!evidence.OperatorConfirmedComPortMatchesSelectedDevice)
            blockers.Add("لم يؤكد الفني أن منفذ COM الظاهر يخص نفس الهاتف المحدد في SoftBox.");
        else
            verified.Add("الفني أكد تطابق الهاتف المتصل على COM مع الهاتف المحدد في SoftBox.");

        return new SamsungOdinAutomationDecision(blockers.Count == 0, blockers, verified);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try { return Path.GetFullPath(path.Trim()); }
        catch { return null; }
    }
}
