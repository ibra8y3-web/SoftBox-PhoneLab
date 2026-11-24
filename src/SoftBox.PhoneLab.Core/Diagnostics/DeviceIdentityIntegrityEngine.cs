using System.Security.Cryptography;
using System.Text;

namespace SoftBox.PhoneLab.Core.Diagnostics;

public enum DeviceIdentityIntegrityStatus
{
    InsufficientEvidence,
    ConsistentEvidence,
    ReviewRequired,
    ConflictDetected
}

public sealed record DeviceIdentityObservation(
    string Platform,
    string Mode,
    string IdentifierKind,
    string IdentifierValue,
    string Manufacturer,
    string Model,
    string Source,
    string Confidence);

public sealed record DeviceIdentityIntegrityFinding(
    string Platform,
    string Mode,
    string IdentifierKind,
    string MaskedIdentifier,
    string Manufacturer,
    string Model,
    IReadOnlyList<string> Sources,
    string Confidence,
    string IdentifierFingerprint,
    DeviceIdentityIntegrityStatus Status,
    IReadOnlyList<string> Warnings);

public sealed record DeviceIdentityIntegrityReport(
    DateTimeOffset AnalyzedAtUtc,
    DeviceIdentityIntegrityStatus OverallStatus,
    IReadOnlyList<DeviceIdentityIntegrityFinding> Findings,
    IReadOnlyList<string> GlobalWarnings);

/// <summary>
/// Read-only evidence correlation for device identifiers. It never generates, changes,
/// patches, spoofs or writes an identifier to a handset. Different transport identifiers
/// are not assumed to be equal and USB VID/PID is never treated as a serial number.
/// </summary>
public static class DeviceIdentityIntegrityEngine
{
    private const int MaximumObservations = 256;

    public static DeviceIdentityIntegrityReport Analyze(
        IEnumerable<DeviceIdentityObservation> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var input = source.Take(MaximumObservations + 1).ToArray();
        if (input.Length > MaximumObservations)
            throw new ArgumentOutOfRangeException(nameof(source),
                $"No more than {MaximumObservations} identity observations can be analyzed at once.");

        var valid = input.Where(IsValid).ToArray();
        var invalidCount = input.Length - valid.Length;
        var findings = new List<DeviceIdentityIntegrityFinding>();

        var groups = valid.GroupBy(item => new IdentityKey(
            Normalize(item.Platform),
            Normalize(item.IdentifierKind),
            Normalize(item.IdentifierValue)));

        foreach (var group in groups)
        {
            var observations = group.ToArray();
            var manufacturers = DistinctKnown(observations.Select(item => item.Manufacturer));
            var models = DistinctKnown(observations.Select(item => item.Model));
            var sources = observations.Select(item => Limit(item.Source, 96))
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var warnings = new List<string>();
            var distinctSources = observations.Select(item => item.Source.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var isNonUniqueHardwareHint =
                group.Key.Kind.Contains("VID/PID", StringComparison.OrdinalIgnoreCase) ||
                group.Key.Kind.Contains("HARDWARE ID", StringComparison.OrdinalIgnoreCase);
            var status = distinctSources > 1 && !isNonUniqueHardwareHint
                ? DeviceIdentityIntegrityStatus.ConsistentEvidence
                : DeviceIdentityIntegrityStatus.InsufficientEvidence;

            if (manufacturers.Length > 1 || models.Length > 1)
            {
                status = DeviceIdentityIntegrityStatus.ConflictDetected;
                warnings.Add("نفس المعرّف الدقيق ظهر مع أسماء شركة أو موديلات مختلفة بين مصادر القراءة؛ لا يتم حسم الهوية تلقائيًا.");
            }
            else if (isNonUniqueHardwareHint)
            {
                status = DeviceIdentityIntegrityStatus.ReviewRequired;
                warnings.Add("VID/PID أو Hardware ID قد يتكرر بين أجهزة متعددة؛ لا يصلح كرقم تسلسلي ولا كدليل ربط لهاتف بعينه.");
            }
            else if (distinctSources > 1)
            {
                warnings.Add("مصادر مستقلة قرأت نفس المعرّف الدقيق من نفس النوع. هذا يعزز اتساق القراءة فقط ولا يثبت أصالة اللوحة أو القطع.");
            }
            else
            {
                warnings.Add("مصدر مستقل واحد فقط قدم هذا المعرّف؛ لا يوجد تأييد مستقل كافٍ.");
            }

            if (observations.Any(item => IsWeakConfidence(item.Confidence)))
            {
                if (status != DeviceIdentityIntegrityStatus.ConflictDetected)
                    status = DeviceIdentityIntegrityStatus.ReviewRequired;
                warnings.Add("بعض الأدلة مجرد تلميح USB أو اسم تعريف، وليست قراءة معرّف موثوقة من نظام الجهاز.");
            }

            var first = observations[0];
            findings.Add(new DeviceIdentityIntegrityFinding(
                first.Platform,
                first.Mode,
                first.IdentifierKind,
                MaskIdentifier(first.IdentifierValue),
                manufacturers.FirstOrDefault() ?? "غير متاح",
                models.FirstOrDefault() ?? "غير متاح",
                sources,
                observations.Select(item => item.Confidence)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .First(),
                CreateIdentifierFingerprint(first.IdentifierValue),
                status,
                warnings));
        }

        var globalWarnings = new List<string>
        {
            "لا تتم مطابقة هاتفين عبر اختلاف المعرّفات أو قرب وقت الاتصال؛ لا يوجد ربط بين وسائط USB المختلفة دون دليل صريح.",
            "معرّف النقل أو الرقم التسلسلي لا يثبت وحده أن الجهاز أصلي بالكامل أو أن اللوحة والقطع لم تُستبدل.",
            "لا توجد أي عملية كتابة للهوية أو IMEI أو Serial أو تعديل NVRAM/EFS/مناطق الخدمة."
        };

        if (invalidCount > 0)
            globalWarnings.Add($"تم استبعاد {invalidCount} سجل/سجلات غير صالح أو ناقص لتجنب استنتاج هوية من بيانات غير مكتملة.");

        var overall = findings.Any(item => item.Status == DeviceIdentityIntegrityStatus.ConflictDetected)
            ? DeviceIdentityIntegrityStatus.ConflictDetected
            : findings.Any(item => item.Status == DeviceIdentityIntegrityStatus.ReviewRequired)
                ? DeviceIdentityIntegrityStatus.ReviewRequired
                : findings.Any(item => item.Status == DeviceIdentityIntegrityStatus.ConsistentEvidence)
                    ? DeviceIdentityIntegrityStatus.ConsistentEvidence
                    : DeviceIdentityIntegrityStatus.InsufficientEvidence;

        return new DeviceIdentityIntegrityReport(
            DateTimeOffset.UtcNow,
            overall,
            findings.OrderBy(item => item.Platform, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.IdentifierKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.MaskedIdentifier, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            globalWarnings);
    }

    public static bool IsValid(DeviceIdentityObservation? observation)
    {
        if (observation is null ||
            !IsSafeLabel(observation.Platform, 48) ||
            !IsSafeLabel(observation.Mode, 80) ||
            !IsSafeLabel(observation.IdentifierKind, 80) ||
            !IsSafeIdentifier(observation.IdentifierValue) ||
            !IsSafeLabel(observation.Source, 96) ||
            !IsSafeLabel(observation.Confidence, 96))
            return false;

        return IsOptionalLabel(observation.Manufacturer, 96) &&
               IsOptionalLabel(observation.Model, 128);
    }

    public static bool IsSafeIdentifierForUi(string? value) => IsSafeIdentifier(value);

    /// <summary>Stable one-way key for matching a UI row back to its exact evidence group.</summary>
    public static string CreateIdentifierFingerprint(string? value)
    {
        if (!IsSafeIdentifier(value))
            return string.Empty;
        var normalized = value!.Trim().ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static string MaskIdentifier(string? value)
    {
        if (!IsSafeIdentifier(value))
            return "غير متاح";

        var visible = new string(value!.Where(char.IsLetterOrDigit).ToArray());
        if (visible.Length == 0)
            return "غير متاح";

        return "••••" + (visible.Length <= 4 ? visible : visible[^4..]);
    }

    private static bool IsSafeIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= 256 &&
           value.All(character =>
               char.IsAsciiLetterOrDigit(character) ||
               character is '-' or '_' or '.' or ':' or '/');

    private static bool IsSafeLabel(string? value, int maximum)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= maximum &&
           !value.Any(char.IsControl);

    private static bool IsOptionalLabel(string? value, int maximum)
        => string.IsNullOrWhiteSpace(value) || IsSafeLabel(value, maximum);

    private static string[] DistinctKnown(IEnumerable<string> values)
        => values.Select(value => (value ?? string.Empty).Trim())
            .Where(value => value.Length > 0 &&
                !value.Equals("غير متاح", StringComparison.OrdinalIgnoreCase) &&
                !value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsWeakConfidence(string confidence)
        => confidence.Contains("hint", StringComparison.OrdinalIgnoreCase) ||
           confidence.Contains("تلميح", StringComparison.OrdinalIgnoreCase) ||
           confidence.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
           confidence.Equals("غير مؤكد", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string Limit(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= maximum ? trimmed : trimmed[..maximum];
    }

    private readonly record struct IdentityKey(string Platform, string Kind, string Value);
}
