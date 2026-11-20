using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record FirmwareInspection(
    string FileName, long SizeBytes, string Sha256, bool IsZip,
    bool HasAndroidMetadata, bool HasPayload, bool HasUpdaterScript,
    IReadOnlyList<string> MetadataLines, string Assessment);

public sealed record BootLoopAssessment(string Verdict, IReadOnlyList<string> Evidence, string Limitation);

/// <summary>Read-only Android evidence collection and firmware archive inspection. Never flashes or modifies a device.</summary>
public sealed class AndroidForensicsService(AdbClient adb)
{
    public async Task<string> CaptureAndroidLogsAsync(string serial, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial)) throw new ArgumentException("Invalid ADB serial.", nameof(serial));
        var sections = new List<string>
        {
            "SOFTBOX PHONELAB — READ-ONLY ANDROID EVIDENCE",
            $"Captured UTC: {DateTimeOffset.UtcNow:O}",
            $"Device serial: {serial}",
            "Privacy: log output may contain notifications, account names, paths, app data and tokens. Review before sharing.",
            ""
        };

        async Task Add(string title, params string[] args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var output = await adb.RunForDeviceAsync(serial, args);
                sections.Add($"===== {title} =====");
                sections.Add(output);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or OperationCanceledException)
            {
                if (ex is OperationCanceledException) throw;
                sections.Add($"===== {title} =====");
                sections.Add("Unavailable: " + ex.Message);
            }
            sections.Add("");
        }

        await Add("LOGCAT — LAST 1500 LINES", "logcat", "-d", "-v", "threadtime", "-t", "1500");
        await Add("DEVICE PROPERTIES", "shell", "getprop");
        await Add("BATTERY", "shell", "dumpsys", "battery");
        await Add("THERMAL SERVICE", "shell", "dumpsys", "thermalservice");
        await Add("BOOT UPTIME", "shell", "cat", "/proc/uptime");
        var text = string.Join(Environment.NewLine, sections);
        await File.WriteAllTextAsync(destinationPath, text, new UTF8Encoding(false), cancellationToken);
        return text;
    }

    public static BootLoopAssessment AnalyzeBootLoop(string logText)
    {
        ArgumentNullException.ThrowIfNull(logText);
        var evidence = new List<string>();
        var rules = new (string Pattern, string Label)[]
        {
            (@"boot.?loop|bootloop", "سطر صريح يشير إلى boot loop"),
            (@"watchdog.*(reboot|timeout)|system_server.*watchdog", "مؤشر Watchdog أو مهلة في خدمة النظام"),
            (@"FATAL EXCEPTION|Fatal signal \d+", "انهيار عملية أو إشارة قاتلة"),
            (@"reboot_reason|sys\.boot\.reason", "سجل يحتوي سبب إقلاع/إعادة تشغيل")
        };
        foreach (var rule in rules)
        {
            var count = Regex.Matches(logText, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
            if (count > 0) evidence.Add($"{rule.Label}: {count} تطابق");
        }
        if (evidence.Count == 0)
            return new BootLoopAssessment("لا توجد إشارات كافية في السجل المقدم", [],
                "قد تكون المشكلة خارج نافذة السجل أو في مرحلة ما قبل Android. عدم العثور على مؤشرات لا ينفي العطل.");
        return new BootLoopAssessment("مؤشرات تستحق مراجعة الفني — ليست تشخيصًا مؤكدًا", evidence,
            "الأنماط النصية وحدها لا تثبت سبب إعادة التشغيل. يلزم ربط التوقيت وسبب الإقلاع وقياسات الطاقة وسياق الجهاز.");
    }

    public static async Task<FirmwareInspection> InspectFirmwareArchiveAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("ملف Firmware غير موجود.", path);
        var info = new FileInfo(path);
        await using var file = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant();
        var metadata = new List<string>();
        var isZip = false;
        var hasAndroidMetadata = false;
        var hasPayload = false;
        var hasUpdater = false;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            isZip = true;
            var metadataEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals("META-INF/com/android/metadata", StringComparison.OrdinalIgnoreCase));
            if (metadataEntry is not null)
            {
                hasAndroidMetadata = true;
                using var reader = new StreamReader(metadataEntry.Open());
                while (metadata.Count < 80 && await reader.ReadLineAsync(cancellationToken) is { } line)
                    metadata.Add(line);
            }
            hasPayload = archive.Entries.Any(e =>
                e.FullName.Equals("payload.bin", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith("/payload.bin", StringComparison.OrdinalIgnoreCase));
            hasUpdater = archive.Entries.Any(e =>
                e.FullName.EndsWith("update-binary", StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException) { }

        var assessment = !isZip
            ? "الملف ليس أرشيف ZIP معروفًا؛ تم حساب SHA-256 فقط."
            : hasAndroidMetadata || hasPayload || hasUpdater
                ? "تم التعرف على بنية أو مكونات شائعة لحزمة تحديث Android. هذا لا يثبت الأصالة أو التوافق أو سلامة المصدر."
                : "أرشيف ZIP صالح، لكن لم تُكتشف علامات Android OTA المعتادة. افحص نوع الحزمة ومصدرها يدويًا.";
        return new FirmwareInspection(info.Name, info.Length, hash, isZip, hasAndroidMetadata, hasPayload, hasUpdater, metadata, assessment);
    }
}
