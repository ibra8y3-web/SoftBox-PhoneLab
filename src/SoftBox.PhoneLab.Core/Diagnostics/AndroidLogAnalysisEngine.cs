using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Diagnostics;

public enum AndroidLogEventCategory
{
    ApplicationCrash,
    ApplicationNotResponding,
    MemoryPressure,
    PermissionAndSecurity,
    SelinuxPolicy,
    ThermalAndPower,
    UsbAndConnectivity,
    StorageAndFilesystem,
    AndroidSystem,
    Network
}

public sealed record AndroidLogEvent(
    AndroidLogEventCategory Category,
    string Severity,
    string Tag,
    string Summary,
    string LastSeenTimestamp,
    int Occurrences);

public sealed record AndroidLogAnalysisResult(
    DateTimeOffset AnalyzedAtUtc,
    int TotalLinesRead,
    int LinesAnalyzed,
    int RelevantLines,
    int EventGroups,
    int CriticalEventGroups,
    bool InputTruncated,
    IReadOnlyList<AndroidLogEvent> Events,
    IReadOnlyList<string> Limitations);

/// <summary>
/// Local, bounded Android logcat correlation. It emits classified summaries and aggregated event
/// counts rather than storing or exporting raw log lines. A log match is evidence for review,
/// not proof of a physical hardware failure.
/// </summary>
public static class AndroidLogAnalysisEngine
{
    public const int MaximumInputCharacters = 8 * 1024 * 1024;
    public const int MaximumAnalyzedLines = 10_000;
    public const int MaximumEventGroups = 100;
    private static readonly Regex ThreadTimePattern = new(
        @"^\s*(?<time>\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d+)\s+(?<pid>\d+)\s+(?<tid>\d+)\s+(?<priority>[VDIWEF])\s+(?<tag>[^:]{1,96}):\s*(?<message>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Rule[] Rules =
    [
        new(AndroidLogEventCategory.ApplicationNotResponding, "Critical",
            "رُصد حدث ANR أو توقف استجابة تطبيق.",
            ["ANR in ", "am_anr", "Application Not Responding"]),
        new(AndroidLogEventCategory.ApplicationCrash, "Critical",
            "رُصد انهيار تطبيق أو إشارة قاتلة أو سجل tombstone.",
            ["FATAL EXCEPTION", "Fatal signal", "am_crash", "crash_dump", "tombstone"]),
        new(AndroidLogEventCategory.MemoryPressure, "Critical",
            "رُصد استثناء نفاد ذاكرة أو فشل تخصيص ذاكرة.",
            ["OutOfMemoryError", "Failed to allocate", "cannot allocate memory"]),
        new(AndroidLogEventCategory.SelinuxPolicy, "Warning",
            "رُصد رفض سياسة SELinux؛ قد يكون متوقعًا أو مرتبطًا بقيود النظام.",
            ["avc: denied", "SELinux denial"]),
        new(AndroidLogEventCategory.PermissionAndSecurity, "Warning",
            "رُصد رفض صلاحية أو SecurityException.",
            ["Permission Denial", "SecurityException", "not allowed to access"]),
        new(AndroidLogEventCategory.ThermalAndPower, "Warning",
            "رُصد حدث حرارة أو إدارة طاقة؛ يلزم ربطه بقياس فعلي.",
            ["thermal", "overheat", "ThermalService", "shutdown due to"]),
        new(AndroidLogEventCategory.StorageAndFilesystem, "Warning",
            "رُصد خطأ تخزين أو نظام ملفات.",
            ["No space left on device", "ENOSPC", "I/O error", "fs_mgr", "f2fs", "ext4"]),
        new(AndroidLogEventCategory.MemoryPressure, "Warning",
            "رُصد ضغط ذاكرة أو إنهاء عمليات بواسطة مدير الذاكرة.",
            ["lmkd", "lowmemorykiller", "low memory", "Killing .* adj"]),
        new(AndroidLogEventCategory.UsbAndConnectivity, "Warning",
            "رُصد حدث USB أو فصل اتصال؛ السجل لا يحدد وحده سلامة المنفذ.",
            ["UsbDeviceManager", "USB disconnect", "usb.*disconnect", "USB device"]),
        new(AndroidLogEventCategory.Network, "Information",
            "رُصد حدث شبكة أو اتصال؛ يلزم اختبار اتصال مستقل.",
            ["ConnectivityService", "NetworkMonitor", "netd", "DNS resolution"]),
        new(AndroidLogEventCategory.AndroidSystem, "Warning",
            "رُصد حدث Watchdog أو تعطل مكوّن نظام Android.",
            ["Watchdog", "SystemServer.*fatal", "system_server.*died"])
    ];

    public static AndroidLogAnalysisResult Analyze(string? logcatOutput)
    {
        var raw = logcatOutput ?? "";
        var inputTruncated = raw.Length > MaximumInputCharacters;
        if (inputTruncated)
            raw = raw[^MaximumInputCharacters..];

        var allLines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var truncatedByLineCount = allLines.Length > MaximumAnalyzedLines;
        var lines = truncatedByLineCount
            ? allLines[^MaximumAnalyzedLines..]
            : allLines;

        var events = new Dictionary<string, EventAccumulator>(StringComparer.OrdinalIgnoreCase);
        var relevantLines = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parsed = ParseLine(line);
            var rule = Rules.FirstOrDefault(candidate => candidate.Patterns.Any(pattern =>
                line.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                (pattern.Contains(".*", StringComparison.Ordinal) &&
                 Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))));

            if (rule is null)
                continue;

            relevantLines++;
            var severity = PromoteSeverity(rule.Severity, parsed.Priority);
            var tag = string.IsNullOrWhiteSpace(parsed.Tag) ? "AndroidLog" : parsed.Tag;
            var key = $"{rule.Category}|{severity}|{tag}|{rule.Summary}";

            if (!events.TryGetValue(key, out var accumulator))
            {
                accumulator = new EventAccumulator(
                    rule.Category,
                    severity,
                    tag,
                    rule.Summary,
                    parsed.Timestamp);
                events.Add(key, accumulator);
            }

            accumulator.Occurrences++;
            if (!string.IsNullOrWhiteSpace(parsed.Timestamp))
                accumulator.LastSeenTimestamp = parsed.Timestamp;
        }

        var ordered = events.Values
            .OrderByDescending(item => SeverityRank(item.Severity))
            .ThenByDescending(item => item.Occurrences)
            .ThenBy(item => item.Category)
            .Take(MaximumEventGroups)
            .Select(item => item.ToRecord())
            .ToArray();

        var limitations = new List<string>
        {
            "التحليل محلي على الجهاز المضيف؛ لا يتم رفع Logcat أو إرسال محتواه إلى أي خدمة.",
            "يعرض المحرك أنماطًا نصية مجمعة، ولا يثبت سبب العطل أو سلامة/تلف قطعة إلكترونية.",
            "فشل أو غياب حدث من Logcat لا يثبت أن الوظيفة سليمة؛ بعض إصدارات Android والشركات تحجب أجزاء من السجل.",
            "لا يتم حفظ السطور الخام في نتيجة التحليل. احتفظ بالسجل الأصلي فقط عند الحاجة وبموافقة صاحب الجهاز."
        };
        if (inputTruncated || truncatedByLineCount)
            limitations.Add($"تم تقليص الإدخال إلى آخر {lines.Length:N0} سطر أو {MaximumInputCharacters:N0} حرف؛ قد توجد أحداث أقدم خارج نطاق التحليل.");
        if (ordered.Length == 0)
            limitations.Add("لم يتم العثور على الأنماط المحددة في الجزء المقروء؛ هذا ليس تقريرًا بأن الجهاز خالٍ من الأعطال.");

        return new AndroidLogAnalysisResult(
            DateTimeOffset.UtcNow,
            allLines.Length,
            lines.Length,
            relevantLines,
            ordered.Length,
            ordered.Count(item => item.Severity == "Critical"),
            inputTruncated || truncatedByLineCount,
            ordered,
            limitations);
    }

    private static ParsedLogLine ParseLine(string line)
    {
        var match = ThreadTimePattern.Match(line);
        return match.Success
            ? new ParsedLogLine(
                match.Groups["time"].Value,
                match.Groups["priority"].Value,
                match.Groups["tag"].Value.Trim())
            : new ParsedLogLine("", "", "");
    }

    private static string PromoteSeverity(string declaredSeverity, string priority)
    {
        if (priority.Equals("F", StringComparison.OrdinalIgnoreCase) ||
            priority.Equals("E", StringComparison.OrdinalIgnoreCase))
            return "Critical";
        if (priority.Equals("W", StringComparison.OrdinalIgnoreCase) &&
            declaredSeverity == "Information")
            return "Warning";
        return declaredSeverity;
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "Critical" => 3,
        "Warning" => 2,
        _ => 1
    };

    private sealed class EventAccumulator(
        AndroidLogEventCategory category,
        string severity,
        string tag,
        string summary,
        string timestamp)
    {
        public AndroidLogEventCategory Category { get; } = category;
        public string Severity { get; } = severity;
        public string Tag { get; } = tag;
        public string Summary { get; } = summary;
        public int Occurrences { get; set; }
        public string LastSeenTimestamp { get; set; } = timestamp;

        public AndroidLogEvent ToRecord()
            => new(Category, Severity, Tag, Summary, LastSeenTimestamp, Occurrences);
    }

    private sealed record ParsedLogLine(string Timestamp, string Priority, string Tag);

    private sealed record Rule(
        AndroidLogEventCategory Category,
        string Severity,
        string Summary,
        IReadOnlyList<string> Patterns);
}
