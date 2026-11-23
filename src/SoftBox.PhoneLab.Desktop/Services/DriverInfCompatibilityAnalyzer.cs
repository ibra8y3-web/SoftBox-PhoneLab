using System.Runtime.InteropServices;
using System.Text;

namespace SoftBox.PhoneLab.Desktop.Services;

public enum DriverInfMatchStatus
{
    Compatible,
    NoHardwareIdentity,
    HardwareIdNotFound,
    ArchitectureMismatch,
    WindowsVersionMismatch,
    UnsupportedInf
}

public sealed record WindowsDriverTarget(
    Version Version,
    string Architecture,
    string DisplayName)
{
    public static WindowsDriverTarget Current
    {
        get
        {
            var architecture = RuntimeInformation.OSArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.X64 => "amd64",
                System.Runtime.InteropServices.Architecture.X86 => "x86",
                System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
                _ => "unknown"
            };
            return new WindowsDriverTarget(
                Environment.OSVersion.Version,
                architecture,
                Environment.OSVersion.VersionString + " • " + RuntimeInformation.OSArchitecture);
        }
    }
}

public sealed record DriverInfCompatibilityReport(
    string InfPath,
    DriverInfMatchStatus Status,
    string Provider,
    string DriverVersion,
    string WindowsTarget,
    string Architecture,
    IReadOnlyList<string> TargetHardwareIds,
    IReadOnlyList<string> MatchedHardwareIds,
    IReadOnlyList<string> MatchingModelSections,
    string Explanation,
    string Limitation);

/// <summary>
/// Conservative offline INF model matcher. It reads [Manufacturer] decorations and model rows,
/// then compares exact Windows PnP hardware IDs against sections selected for the current
/// Windows architecture/build. It neither installs nor stages a driver.
/// </summary>
public static class DriverInfCompatibilityAnalyzer
{
    private const int MaximumInfBytes = 16 * 1024 * 1024;
    private const int MaximumLines = 250_000;
    private const int MaximumHardwareIds = 128;

    public static async Task<DriverInfCompatibilityReport> AnalyzeFileAsync(
        string infPath,
        IReadOnlyCollection<string> targetHardwareIds,
        WindowsDriverTarget? target = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(infPath);
        var fullPath = Path.GetFullPath(infPath);
        if (!Path.GetExtension(fullPath).Equals(".inf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("اختر ملف تعريف INF بعد فك حزمة الشركة المصنّعة.", nameof(infPath));

        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("ملف INF غير موجود.", fullPath);
        if (info.Length <= 0 || info.Length > MaximumInfBytes)
            throw new InvalidDataException("ملف INF فارغ أو أكبر من الحد الآمن 16 MiB.");

        await using var stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        if (content.Length > MaximumInfBytes)
            throw new InvalidDataException("محتوى INF تجاوز الحد الآمن بعد فك الترميز.");

        return Analyze(content, targetHardwareIds, target ?? WindowsDriverTarget.Current, fullPath);
    }

    public static DriverInfCompatibilityReport Analyze(
        string infContent,
        IReadOnlyCollection<string> targetHardwareIds,
        WindowsDriverTarget target,
        string infPath = "(memory)")
    {
        ArgumentNullException.ThrowIfNull(infContent);
        ArgumentNullException.ThrowIfNull(targetHardwareIds);
        ArgumentNullException.ThrowIfNull(target);

        var deviceIds = targetHardwareIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(NormalizeHardwareId)
            .Where(id => id.Length is > 0 and <= 512)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumHardwareIds)
            .ToArray();

        var provider = string.Empty;
        var driverVersion = string.Empty;
        var sections = ParseSections(infContent);
        if (sections.TryGetValue("Version", out var versionLines))
        {
            provider = ReadKey(versionLines, "Provider");
            driverVersion = ReadKey(versionLines, "DriverVer");
        }

        if (deviceIds.Length == 0)
        {
            return Report(
                infPath, DriverInfMatchStatus.NoHardwareIdentity, provider, driverVersion, target,
                deviceIds, [], [],
                "لا توجد Hardware IDs مقروءة من جهاز Windows المحدد؛ لا يمكن إثبات توافق INF مع هذا الهاتف.",
                "اسم الشركة أو الموديل أو VID/PID وحده ليس بديلًا عن قائمة Hardware IDs التي يعلنها Windows.");
        }

        if (sections.Count == 0 || !sections.TryGetValue("Manufacturer", out var manufacturerLines))
        {
            return Report(
                infPath, DriverInfMatchStatus.UnsupportedInf, provider, driverVersion, target,
                deviceIds, [], [],
                "ملف INF لا يحتوي على بنية أقسام قابلة للتحقق أو لا يحتوي قسم [Manufacturer].",
                "لم يتم إجراء أي استنتاج من اسم الملف؛ هذه نتيجة غير حاسمة وليست حكمًا بأن التعريف غير متوافق.");
        }

        var modelSections = ResolveModelSections(manufacturerLines, sections);
        if (modelSections.Count == 0)
        {
            return Report(
                infPath, DriverInfMatchStatus.UnsupportedInf, provider, driverVersion, target,
                deviceIds, [], [],
                "تعذر استخراج أقسام Models المشار إليها من [Manufacturer].",
                "قد يستخدم INF تنسيقًا أو توجيهات لا يغطيها هذا المحلل المحافظ؛ استخدم Windows/الشركة المصنّعة للتحقق.");
        }

        var matches = new List<ModelMatch>();
        foreach (var section in modelSections)
        {
            if (!sections.TryGetValue(section.SectionName, out var rows))
                continue;

            foreach (var row in rows)
            {
                foreach (var id in ReadModelHardwareIds(row))
                {
                    if (deviceIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                        matches.Add(new ModelMatch(id, section.SectionName, section.Decoration));
                }
            }
        }

        if (matches.Count == 0)
        {
            return Report(
                infPath, DriverInfMatchStatus.HardwareIdNotFound, provider, driverVersion, target,
                deviceIds, [], modelSections.Select(item => item.SectionName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                "لم يُعثر على أي Hardware ID من الجهاز داخل أقسام الطراز المشار إليها في ملف INF.",
                "عدم وجود تطابق في هذا INF لا يثبت أن الشركة لا تملك تعريفًا آخر؛ افحص الحزمة الصحيحة للطراز والمنطقة.");
        }

        var architectureMatches = matches
            .Where(match => IsArchitectureCompatible(match.Decoration, target.Architecture))
            .ToArray();
        if (architectureMatches.Length == 0)
        {
            return Report(
                infPath, DriverInfMatchStatus.ArchitectureMismatch, provider, driverVersion, target,
                deviceIds, matches.Select(item => item.HardwareId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                matches.Select(item => item.SectionName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                "Hardware ID موجود في INF، لكن أقسام Models المطابقة مخصصة لمعمارية Windows مختلفة.",
                "لم يتم تثبيت التعريف. استخدم حزمة x64/ARM64/x86 التي تطابق Windows الفعلي.");
        }

        var versionMatches = architectureMatches
            .Where(match => IsWindowsVersionCompatible(match.Decoration, target.Version))
            .ToArray();
        if (versionMatches.Length == 0)
        {
            return Report(
                infPath, DriverInfMatchStatus.WindowsVersionMismatch, provider, driverVersion, target,
                deviceIds, architectureMatches.Select(item => item.HardwareId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                architectureMatches.Select(item => item.SectionName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                "Hardware ID ومعمارية Windows متطابقان، لكن أقسام INF المتاحة تشترط إصدارًا/Build أحدث من إصدار Windows الحالي.",
                "قد توجد أقسام أخرى داخل حزمة مختلفة؛ لم يتم إجراء أي تثبيت أو تجاوز لقيود نظام التشغيل.");
        }

        return Report(
            infPath, DriverInfMatchStatus.Compatible, provider, driverVersion, target,
            deviceIds, versionMatches.Select(item => item.HardwareId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            versionMatches.Select(item => item.SectionName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            "وُجد تطابق Hardware ID مطابق لمعمارية Windows وإصدارها في أقسام Models المشار إليها من [Manufacturer].",
            "هذه مطابقة INF ثابتة فقط؛ لا تثبت توقيع CAT أو عضوية INF داخل CAT أو صلاحية الحزمة للتثبيت. تحقق من توقيع CAT وعضوية الملفات ومن تعليمات OEM قبل أي تثبيت.");
    }

    private static Dictionary<string, List<string>> ParseSections(string content)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentSection = null;
        var pending = new StringBuilder();
        var lineCount = 0;

        foreach (var rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal)
                                       .Replace('\r', '\n')
                                       .Split('\n'))
        {
            if (++lineCount > MaximumLines)
                throw new InvalidDataException("ملف INF يحتوي على عدد أسطر أكبر من الحد الآمن.");

            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;

            if (pending.Length > 0)
                pending.Append(' ');
            pending.Append(line);

            if (pending.ToString().EndsWith("\\", StringComparison.Ordinal))
            {
                pending.Length--;
                continue;
            }

            var completed = pending.ToString().Trim();
            pending.Clear();

            if (completed.Length > 2 && completed[0] == '[' && completed[^1] == ']')
            {
                currentSection = completed[1..^1].Trim();
                if (!sections.ContainsKey(currentSection))
                    sections[currentSection] = [];
                continue;
            }

            if (currentSection is not null)
                sections[currentSection].Add(completed);
        }

        if (pending.Length > 0 && currentSection is not null)
            sections[currentSection].Add(pending.ToString().Trim());
        return sections;
    }

    private static string StripComment(string value)
    {
        var inQuotes = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '"')
                inQuotes = !inQuotes;
            else if (value[index] == ';' && !inQuotes)
                return value[..index];
        }
        return value;
    }

    private static string ReadKey(IEnumerable<string> lines, string key)
    {
        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            if (line[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return line[(separator + 1)..].Trim().Trim('"');
        }
        return string.Empty;
    }

    private static List<ResolvedModelSection> ResolveModelSections(
        IEnumerable<string> manufacturerLines,
        IReadOnlyDictionary<string, List<string>> sections)
    {
        var result = new List<ResolvedModelSection>();
        foreach (var line in manufacturerLines)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var values = SplitCsv(line[(separator + 1)..]);
            if (values.Count == 0)
                continue;

            var baseSection = values[0];
            var decorations = values.Skip(1).Where(value => value.Length > 0).ToArray();
            if (decorations.Length == 0)
            {
                if (sections.ContainsKey(baseSection))
                    result.Add(new ResolvedModelSection(baseSection, string.Empty));
                continue;
            }

            foreach (var decoration in decorations)
            {
                var sectionName = baseSection + "." + decoration;
                if (sections.ContainsKey(sectionName))
                    result.Add(new ResolvedModelSection(sectionName, decoration));
            }
        }
        return result
            .DistinctBy(item => item.SectionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ReadModelHardwareIds(string line)
    {
        var separator = line.IndexOf('=');
        if (separator <= 0)
            return [];

        var values = SplitCsv(line[(separator + 1)..]);
        if (values.Count <= 1)
            return [];

        return values.Skip(1)
            .Select(NormalizeHardwareId)
            .Where(value => value.Contains('\\') && value.Length <= 512)
            .ToArray();
    }

    private static List<string> SplitCsv(string value)
    {
        var items = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in value)
        {
            if (character == '"')
                inQuotes = !inQuotes;
            if (character == ',' && !inQuotes)
            {
                items.Add(current.ToString().Trim().Trim('"'));
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }
        if (current.Length > 0)
            items.Add(current.ToString().Trim().Trim('"'));
        return items;
    }

    private static string NormalizeHardwareId(string value)
        => (value ?? string.Empty).Trim().Trim('"').ToUpperInvariant();

    private static bool IsArchitectureCompatible(string decoration, string architecture)
    {
        if (string.IsNullOrWhiteSpace(decoration))
            return true; // Undecorated model section is architecture-neutral in the INF.
        var architectureToken = decoration.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (!architectureToken.StartsWith("NT", StringComparison.OrdinalIgnoreCase))
            return false;

        var declared = architectureToken[2..].ToLowerInvariant();
        if (declared.Length == 0)
            return true;
        return declared switch
        {
            "amd64" => architecture.Equals("amd64", StringComparison.OrdinalIgnoreCase),
            "x86" => architecture.Equals("x86", StringComparison.OrdinalIgnoreCase),
            "arm64" or "arm64ec" => architecture.Equals("arm64", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsWindowsVersionCompatible(string decoration, Version targetVersion)
    {
        var parts = decoration.Split('.', StringSplitOptions.None);
        if (parts.Length <= 1)
            return true;

        var numeric = parts.Skip(1)
            .Where(part => part.Length > 0 && part.All(char.IsDigit))
            .Select(part => int.TryParse(part, out var value) ? value : -1)
            .ToArray();
        if (numeric.Length < 2)
            return true;

        var minMajor = numeric[0];
        var minMinor = numeric[1];
        var minBuild = numeric.Length >= 3 ? numeric[2] : 0;
        if (minMajor < 0 || minMinor < 0 || minBuild < 0)
            return false;

        if (targetVersion.Major != minMajor)
            return targetVersion.Major > minMajor;
        if (targetVersion.Minor != minMinor)
            return targetVersion.Minor > minMinor;
        return targetVersion.Build >= minBuild;
    }

    private static DriverInfCompatibilityReport Report(
        string infPath,
        DriverInfMatchStatus status,
        string provider,
        string driverVersion,
        WindowsDriverTarget target,
        IReadOnlyList<string> targetIds,
        IReadOnlyList<string> matchedIds,
        IReadOnlyList<string> sections,
        string explanation,
        string limitation)
        => new(
            infPath,
            status,
            provider,
            driverVersion,
            target.DisplayName,
            target.Architecture,
            targetIds,
            matchedIds,
            sections,
            explanation,
            limitation);

    private sealed record ResolvedModelSection(string SectionName, string Decoration);
    private sealed record ModelMatch(string HardwareId, string SectionName, string Decoration);
}
