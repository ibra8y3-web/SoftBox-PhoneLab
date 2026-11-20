using System.Globalization;

namespace SoftBox.PhoneLab.Core.Fastboot;

public enum BootloaderLockState
{
    Locked,
    Unlocked,
    Unknown,
    Conflicting
}

public enum VerifiedBootState
{
    Enabled,
    Disabled,
    Unknown,
    Conflicting
}

public sealed record FastbootStateReport(
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> ConflictingVariables,
    string? Product,
    string? Variant,
    string? HardwareRevision,
    string? BootloaderVersion,
    string? BasebandVersion,
    string? CurrentSlot,
    int? SlotCount,
    BootloaderLockState LockState,
    VerifiedBootState SecureBootState,
    IReadOnlyDictionary<string, string> RollbackVariables,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Parses read-only "fastboot getvar all" output into structured facts.
/// It never infers unlock eligibility or flashing compatibility and preserves rollback values as raw strings.
/// </summary>
public static class FastbootVariableParser
{
    private static readonly HashSet<string> LockKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "unlocked",
        "device-unlocked",
        "bootloader-unlocked",
        "device-state"
    };

    private static readonly HashSet<string> SecureKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "secure",
        "secure-boot",
        "verified-boot"
    };

    public static FastbootStateReport Parse(string? output)
    {
        var observed = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (output ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryReadVariable(line, out var key, out var value))
                continue;

            if (!observed.TryGetValue(key, out var values))
            {
                values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                observed.Add(key, values);
            }

            values.Add(value);
        }

        var conflicts = observed
            .Where(pair => pair.Value.Count > 1)
            .Select(pair => pair.Key)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var variables = observed
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Single(),
                StringComparer.OrdinalIgnoreCase);

        var lockState = ResolveLockState(observed, conflicts);
        var secureState = ResolveSecureState(observed, conflicts);

        var product = Get(variables, "product");
        var variant = Get(variables, "variant");
        var hardwareRevision = First(variables, "hw-revision", "hardware-revision", "board-revision");
        var bootloaderVersion = First(variables, "version-bootloader", "bootloader-version", "version-bootloader");
        var basebandVersion = First(variables, "version-baseband", "baseband-version");
        var currentSlot = NormalizeSlot(Get(variables, "current-slot"));
        var slotCount = ParseSlotCount(Get(variables, "slot-count"));

        var rollback = variables
            .Where(pair => IsRollbackVariable(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        var warnings = new List<string>();
        if (lockState == BootloaderLockState.Unlocked)
            warnings.Add("أبلغ Fastboot أن Bootloader مفتوح. هذه قراءة حالة فقط، ولا تثبت أهلية إعادة القفل أو توافق أي Firmware.");
        else if (lockState == BootloaderLockState.Conflicting)
            warnings.Add("توجد متغيرات متعارضة حول حالة قفل Bootloader. لم يتم اختيار قيمة منها.");
        else if (lockState == BootloaderLockState.Unknown)
            warnings.Add("لم تتوفر قيمة صريحة وموثوقة لحالة قفل Bootloader.");

        if (secureState == VerifiedBootState.Disabled)
            warnings.Add("أبلغ Fastboot أن متغير Secure/Verified Boot معطل. لم يتم إجراء تحقق تشفيري من سلسلة AVB.");
        else if (secureState == VerifiedBootState.Conflicting)
            warnings.Add("توجد قيم متعارضة لمتغيرات Secure Boot.");
        else if (secureState == VerifiedBootState.Unknown)
            warnings.Add("لم تتوفر قيمة صريحة لحالة Secure Boot؛ لا يتم استنتاجها من حالة القفل.");

        if (rollback.Count > 0)
            warnings.Add("تم عرض متغيرات Anti-Rollback كما أبلغ بها الجهاز فقط. لم يتم تفسيرها أو مقارنتها بملف Firmware.");
        else
            warnings.Add("لم يتم العثور على متغير Anti-Rollback معروف في المخرجات؛ غياب المتغير لا يعني عدم وجود حماية رجوع.");

        if (conflicts.Length > 0)
            warnings.Add("تم إخفاء القيم المتعارضة من جدول المتغيرات لتجنب عرض نتيجة حاسمة غير مدعومة.");

        if (currentSlot is null && (Get(variables, "slot-count") is not null || variables.Keys.Any(key => key.StartsWith("slot-suffix", StringComparison.OrdinalIgnoreCase))))
            warnings.Add("تعذر تأكيد Current Slot من القيم المقروءة.");

        return new FastbootStateReport(
            variables,
            conflicts,
            product,
            variant,
            hardwareRevision,
            bootloaderVersion,
            basebandVersion,
            currentSlot,
            slotCount,
            lockState,
            secureState,
            rollback,
            warnings);
    }

    private static bool TryReadVariable(string line, out string key, out string value)
    {
        key = "";
        value = "";

        var text = (line ?? "").Trim();
        if (text.Length is 0 or > 16_384)
            return false;

        if (text.StartsWith("(bootloader)", StringComparison.OrdinalIgnoreCase))
            text = text["(bootloader)".Length..].Trim();

        if (text.StartsWith("getvar:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("finished", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("total time", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("waiting for", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("info", StringComparison.OrdinalIgnoreCase))
            return false;

        var separator = text.IndexOf(':');
        if (separator <= 0 || separator >= text.Length - 1)
            return false;

        var rawKey = text[..separator].Trim();
        var rawValue = text[(separator + 1)..].Trim();
        if (rawKey.Length is 0 or > 160 ||
            rawKey.Any(char.IsWhiteSpace) ||
            rawValue.Length is 0 or > 4096)
            return false;

        var normalizedKey = NormalizeKey(rawKey);
        if (normalizedKey.Length == 0 ||
            normalizedKey.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            normalizedKey.Equals("getvar", StringComparison.OrdinalIgnoreCase))
            return false;

        if (rawValue.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
            rawValue.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            rawValue.Contains("unknown variable", StringComparison.OrdinalIgnoreCase))
            return false;

        if (rawValue.StartsWith("(bootloader)", StringComparison.OrdinalIgnoreCase))
            rawValue = rawValue["(bootloader)".Length..].Trim();
        if (rawValue.Length == 0 || rawValue.Any(char.IsControl))
            return false;

        key = normalizedKey;
        value = rawValue;
        return true;
    }

    private static BootloaderLockState ResolveLockState(
        IReadOnlyDictionary<string, HashSet<string>> observed,
        IReadOnlyCollection<string> conflicts)
    {
        var states = new HashSet<BootloaderLockState>();
        foreach (var key in LockKeys)
        {
            if (conflicts.Contains(key, StringComparer.OrdinalIgnoreCase))
                return BootloaderLockState.Conflicting;
            if (!observed.TryGetValue(key, out var values))
                continue;

            foreach (var value in values)
            {
                var state = ParseLockValue(value);
                if (state.HasValue)
                    states.Add(state.Value);
            }
        }

        if (states.Count > 1)
            return BootloaderLockState.Conflicting;
        return states.Count == 1 ? states.Single() : BootloaderLockState.Unknown;
    }

    private static VerifiedBootState ResolveSecureState(
        IReadOnlyDictionary<string, HashSet<string>> observed,
        IReadOnlyCollection<string> conflicts)
    {
        var states = new HashSet<VerifiedBootState>();
        foreach (var key in SecureKeys)
        {
            if (conflicts.Contains(key, StringComparer.OrdinalIgnoreCase))
                return VerifiedBootState.Conflicting;
            if (!observed.TryGetValue(key, out var values))
                continue;

            foreach (var value in values)
            {
                var state = ParseBooleanValue(value);
                if (state.HasValue)
                    states.Add(state.Value ? VerifiedBootState.Enabled : VerifiedBootState.Disabled);
            }
        }

        if (states.Count > 1)
            return VerifiedBootState.Conflicting;
        return states.Count == 1 ? states.Single() : VerifiedBootState.Unknown;
    }

    private static BootloaderLockState? ParseLockValue(string value)
    {
        var normalized = NormalizeValue(value);
        if (normalized is "yes" or "true" or "1" or "unlocked" or "orange")
            return BootloaderLockState.Unlocked;
        if (normalized is "no" or "false" or "0" or "locked")
            return BootloaderLockState.Locked;
        return null;
    }

    private static bool? ParseBooleanValue(string value)
    {
        var normalized = NormalizeValue(value);
        if (normalized is "yes" or "true" or "1" or "enabled" or "on")
            return true;
        if (normalized is "no" or "false" or "0" or "disabled" or "off")
            return false;
        return null;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;

    private static string? First(
        IReadOnlyDictionary<string, string> values,
        params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value))
                return value;
        return null;
    }

    private static string? NormalizeSlot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = NormalizeValue(value);
        return normalized is "a" or "b" or "c" or "d" ? normalized : null;
    }

    private static int? ParseSlotCount(string? value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) &&
           count is >= 1 and <= 8
            ? count
            : null;

    private static bool IsRollbackVariable(string key)
        => key.Equals("anti", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("anti-rollback", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("antirollback", StringComparison.OrdinalIgnoreCase) ||
           key.StartsWith("rollback-index", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("rollback", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeKey(string value)
        => new(value.Trim().ToLowerInvariant()
            .Select(character => character == '_' ? '-' : character)
            .ToArray());

    private static string NormalizeValue(string value)
        => value.Trim().Trim('"', '\'').ToLowerInvariant();
}
