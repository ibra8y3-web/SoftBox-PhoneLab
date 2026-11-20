using System.Text.RegularExpressions;
using SoftBox.PhoneLab.Core.Firmware;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Core.Compatibility;

public enum DeviceIdentityState
{
    Exact,
    Probable,
    Ambiguous,
    Conflicting,
    Unknown
}

public enum DeviceCapabilityOperation
{
    ReadAndroidDiagnostics,
    RunAgentHardwareTests,
    MirrorScreen,
    ReadFastbootVariables,
    ShowVerifiedBoardMap,
    ReviewFirmwareCandidate,
    FlashFirmware,
    UnlockBootloader,
    EraseDeviceData,
    QualcommEmergencyDownload,
    MediaTekBromRecovery,
    SamsungDownloadMode,
    XiaomiFastbootFlash
}

public enum CapabilityDecisionState
{
    Supported,
    NeedsSetup,
    ReviewRequired,
    Blocked,
    Unknown
}

public sealed record DeviceIdentityResolution(
    DeviceProfile? Profile,
    DeviceIdentityState State,
    int Score,
    bool ExactModelNumberMatched,
    bool ManufacturerMatched,
    bool VerifiedIdentitySource,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Conflicts)
{
    public bool IsExact => State == DeviceIdentityState.Exact && Profile is not null;
    public string DisplayName => Profile?.MarketingName ?? "هوية الجهاز غير محسومة";

    public DeviceProfileMatch ToLegacyMatch()
        => new(
            Profile,
            Profile is null ? 0 : Score,
            State switch
            {
                DeviceIdentityState.Exact => "مطابقة دقيقة",
                DeviceIdentityState.Probable => "مطابقة مرجحة",
                DeviceIdentityState.Ambiguous => "مطابقة ملتبسة",
                DeviceIdentityState.Conflicting => "بيانات متعارضة",
                _ => "غير معروف"
            },
            Reasons.Concat(Conflicts.Select(conflict => "تعارض: " + conflict)).ToArray());
}

public sealed record DeviceCapabilityContext(
    DeviceSnapshot? Device,
    bool AdbAuthorized,
    bool AgentConnected,
    bool FastbootDetected,
    bool VerifiedBoardMapAvailable,
    FirmwareLibraryItem? Firmware = null,
    FirmwareIntegrityResult? FirmwareIntegrity = null);

public sealed record DeviceCapabilityDecision(
    DeviceCapabilityOperation Operation,
    string DisplayName,
    CapabilityDecisionState State,
    bool CanExecute,
    string Evidence,
    string RequiredAction);

public sealed record DeviceCapabilityReport(
    DateTimeOffset EvaluatedAtUtc,
    DeviceIdentityResolution Identity,
    IReadOnlyList<DeviceCapabilityDecision> Decisions,
    IReadOnlyList<string> SafetyRules)
{
    public bool HasBlockedOperations => Decisions.Any(item => item.State == CapabilityDecisionState.Blocked);
    public bool HasUnknownOperations => Decisions.Any(item => item.State == CapabilityDecisionState.Unknown);
}

/// <summary>
/// Shared, fail-closed device identity and capability policy engine.
///
/// Identity resolution is evidence-based and deliberately separate from operation support.
/// A model match never grants OEM flashing, unlocking, EDL/BROM or destructive capabilities.
/// Unknown or ambiguous hardware information is never upgraded to an assumed capability.
/// </summary>
public static class DeviceCapabilityEngine
{
    private static readonly string[] UnknownValues =
    [
        "", "غير متاح", "unknown", "n/a", "not available", "none", "null"
    ];

    public static DeviceIdentityResolution ResolveIdentity(
        DeviceSnapshot? device,
        IEnumerable<DeviceProfile>? profiles)
    {
        if (device is null || profiles is null)
            return UnknownIdentity();

        var candidates = profiles
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Id))
            .Select(profile => ScoreProfile(device, profile))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
            return UnknownIdentity();

        var best = candidates[0];
        var tied = candidates
            .Where(candidate => candidate.Score == best.Score)
            .Select(candidate => candidate.Profile.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (tied.Length > 1)
        {
            return new DeviceIdentityResolution(
                null,
                DeviceIdentityState.Ambiguous,
                best.Score,
                false,
                false,
                false,
                best.Reasons,
                ["أكثر من ملف جهاز حصل على درجة المطابقة نفسها: " + string.Join(", ", tied)]);
        }

        if (best.Conflicts.Count > 0)
        {
            return new DeviceIdentityResolution(
                null,
                DeviceIdentityState.Conflicting,
                best.Score,
                best.ExactModelNumberMatched,
                best.ManufacturerMatched,
                false,
                best.Reasons,
                best.Conflicts);
        }

        var verifiedSource = best.Profile.IdentityVerified && IsVerifiedIdentitySource(best.Profile);
        var exact = best.ExactModelNumberMatched &&
                    best.ManufacturerMatched &&
                    best.Profile.IdentityVerified &&
                    verifiedSource;

        var state = exact
            ? DeviceIdentityState.Exact
            : best.Score >= 70
                ? DeviceIdentityState.Probable
                : DeviceIdentityState.Unknown;

        return new DeviceIdentityResolution(
            best.Profile,
            state,
            best.Score,
            best.ExactModelNumberMatched,
            best.ManufacturerMatched,
            verifiedSource,
            best.Reasons,
            []);
    }

    public static DeviceCapabilityReport Evaluate(
        DeviceCapabilityContext context,
        IEnumerable<DeviceProfile>? profiles = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var identity = ResolveIdentity(context.Device, profiles);
        var device = context.Device;
        var safeSerial = device is not null && IsSafeSerial(device.Serial);
        var adbReady = safeSerial && context.AdbAuthorized;
        var firmwareState = EvaluateFirmwareCandidate(context, identity);

        var decisions = new List<DeviceCapabilityDecision>
        {
            Decision(
                DeviceCapabilityOperation.ReadAndroidDiagnostics,
                "قراءات Android عبر ADB",
                adbReady ? CapabilityDecisionState.Supported :
                    safeSerial ? CapabilityDecisionState.Blocked : CapabilityDecisionState.Unknown,
                adbReady,
                adbReady
                    ? "Serial صالح والجهاز مصرح به حاليًا عبر ADB. الدعم هنا لأوامر القراءة فقط."
                    : "لم يتم إثبات اتصال ADB مصرح به للجهاز المحدد.",
                adbReady ? "يمكن تنفيذ قراءات ADB المحددة للقراءة فقط." : "وصل الجهاز ووافق على USB debugging ثم أعد الاكتشاف."),

            Decision(
                DeviceCapabilityOperation.RunAgentHardwareTests,
                "اختبارات العتاد عبر PhoneLab Agent",
                adbReady && context.AgentConnected ? CapabilityDecisionState.Supported :
                    adbReady ? CapabilityDecisionState.NeedsSetup : CapabilityDecisionState.Blocked,
                adbReady && context.AgentConnected,
                context.AgentConnected && adbReady
                    ? "Agent متصل والجهاز مصرح به عبر ADB."
                    : "لم يتم إثبات اتصال Agent صالح مع تصريح ADB الحالي.",
                context.AgentConnected && adbReady
                    ? "يمكن عرض الاختبارات الفعلية المتاحة في Agent."
                    : "ثبت Agent من مسار SoftBox الرسمي، وافتحه على الهاتف ووافق على الاتصال."),

            Decision(
                DeviceCapabilityOperation.MirrorScreen,
                "عرض شاشة الهاتف عبر scrcpy",
                adbReady ? CapabilityDecisionState.Supported : CapabilityDecisionState.Blocked,
                adbReady,
                adbReady
                    ? "يتوفر اتصال ADB مصرح به؛ يلزم أن تظل أداة scrcpy والتعريف متاحين وقت التشغيل."
                    : "لا يوجد تصريح ADB صالح لهذا الجهاز.",
                adbReady ? "افتح جلسة العرض مع الجهاز المحدد." : "أعد توصيل الهاتف ووافق على USB debugging."),

            Decision(
                DeviceCapabilityOperation.ReadFastbootVariables,
                "قراءة متغيرات Fastboot",
                context.FastbootDetected && safeSerial
                    ? CapabilityDecisionState.Supported
                    : CapabilityDecisionState.Unknown,
                context.FastbootDetected && safeSerial,
                context.FastbootDetected && safeSerial
                    ? "تم اكتشاف Serial في وضع Fastboot؛ المسموح به هنا قراءة getvar فقط."
                    : "لم يتم تأكيد أن الجهاز المحدد موجود الآن في قائمة Fastboot.",
                context.FastbootDetected ? "أعد اكتشاف Fastboot قبل القراءة." : "أدخل الهاتف إلى وضع Bootloader/Fastboot بالطريقة الرسمية الخاصة به."),

            Decision(
                DeviceCapabilityOperation.ShowVerifiedBoardMap,
                "عرض خريطة PCB المطابقة",
                context.VerifiedBoardMapAvailable && identity.IsExact
                    ? CapabilityDecisionState.Supported
                    : CapabilityDecisionState.Unknown,
                context.VerifiedBoardMapAvailable && identity.IsExact,
                context.VerifiedBoardMapAvailable && identity.IsExact
                    ? "توجد خريطة PCB موثقة مطابقة لهوية الجهاز الدقيقة."
                    : "لا توجد خريطة PCB موثقة مثبتة على هوية دقيقة لهذا الجهاز؛ المخطط العام ليس خريطة صيانة مطابقة.",
                "استورد خريطة موثقة بمصدر ومراجعة ورقم موديل/كود جهاز مطابقين."),

            firmwareState,

            Decision(
                DeviceCapabilityOperation.FlashFirmware,
                "تفليش Firmware",
                CapabilityDecisionState.Blocked,
                false,
                "محرك SoftBox لا يملك حاليًا بروتوكول تفليش موثقًا وآمنًا يطابق ملف Firmware وهوية اللوحة وقيود Bootloader/Anti-Rollback.",
                "لا يمكن تنفيذ التفليش من هذا المحرك. يلزم بروتوكول OEM رسمي، وملف موقع متوافق، وقاعدة Anti-Rollback موثوقة واختبارات جهاز فعلية."),

            Decision(
                DeviceCapabilityOperation.UnlockBootloader,
                "فتح Bootloader",
                CapabilityDecisionState.Blocked,
                false,
                "لا توجد موافقة OEM أو سياسة طراز موثقة أو تحقق رسمي من أهلية فتح Bootloader.",
                "استخدم أداة الشركة المصنعة الرسمية وإجراءاتها الخاصة بالموديل. لن يرسل SoftBox أمر فتح تخمينيًا."),

            Decision(
                DeviceCapabilityOperation.EraseDeviceData,
                "مسح بيانات الجهاز",
                CapabilityDecisionState.Blocked,
                false,
                "محرك التوافق لا يسمح بتحويل صلاحية التشخيص إلى عملية مسح أو إعادة ضبط.",
                "استخدم إجراءً رسميًا مستقلًا مع نسخة احتياطية وموافقة صريحة، ولا تستخدم مسارات التشخيص."),

            Decision(
                DeviceCapabilityOperation.QualcommEmergencyDownload,
                "استعادة Qualcomm EDL",
                CapabilityDecisionState.Blocked,
                false,
                "لا توجد سلسلة ثقة لملفات Firehose أو تحقق توقيع/توافق للمنصة واللوحة.",
                "يلزم ملف Programmer رسمي مطابق وتحقق توقيع ومصدر OEM موثوق وإجراءات خدمة معتمدة."),

            Decision(
                DeviceCapabilityOperation.MediaTekBromRecovery,
                "استعادة MediaTek BROM/DA",
                CapabilityDecisionState.Blocked,
                false,
                "لا يوجد Download Agent موثق التوقيع ومتوافق مع Hardware/SLA لهذا الجهاز.",
                "يلزم DA رسمي ومطابق والتحقق من سياسة Secure Boot/Service Authentication."),

            Decision(
                DeviceCapabilityOperation.SamsungDownloadMode,
                "عمليات Samsung Download Mode",
                CapabilityDecisionState.Blocked,
                false,
                "لا توجد وحدة خدمة Samsung موثقة ومختبرة لعمليات Download Mode أو التفليش.",
                "لا تستخدم أوامر Odin/Download Mode غير مدعومة من هذا المحرك."),

            Decision(
                DeviceCapabilityOperation.XiaomiFastbootFlash,
                "عمليات Xiaomi Fastboot",
                CapabilityDecisionState.Blocked,
                false,
                "لا توجد قاعدة Xiaomi رسمية موثقة تربط الموديل وحالة Bootloader والمنطقة وسياسة الحساب بالعملية.",
                "يجب استخدام آلية Xiaomi الرسمية والتحقق من حالة Bootloader وقيود الطراز قبل أي خدمة.")
        };

        return new DeviceCapabilityReport(
            DateTimeOffset.UtcNow,
            identity,
            decisions,
            [
                "مطابقة الموديل لا تعني أن جميع وظائف الشركة أو المعالج مدعومة.",
                "صلاحية ADB ووجود Agent ووجود Fastboot حالات اتصال منفصلة ولا يتم استنتاج إحداها من الأخرى.",
                "مطابقة النصوص أو بصمة SHA-256 المحلية لا تثبت توقيع الناشر أو توافق Firmware.",
                "كل عملية تفليش أو فتح Bootloader أو مسح أو EDL/BROM/OEM غير مدعومة تظل محجوبة حتى يضاف بروتوكول رسمي موثق واختبارات مستقلة.",
                "لا يحوّل هذا المحرك بيانات التعريف أو التليمترية إلى حكم نجاح كهربائي أو سلامة مادية."
            ]);
    }

    private static DeviceCapabilityDecision EvaluateFirmwareCandidate(
        DeviceCapabilityContext context,
        DeviceIdentityResolution identity)
    {
        var firmware = context.Firmware;
        if (firmware is null)
            return Decision(
                DeviceCapabilityOperation.ReviewFirmwareCandidate,
                "تقييم ملف Firmware مختار",
                CapabilityDecisionState.Unknown,
                false,
                "لم يتم اختيار ملف Firmware من المكتبة.",
                "اختر ملفًا ثم أعد التحقق من الحجم والبصمة والموديل والمنطقة والمصدر.");

        var device = context.Device;
        if (device is null || !identity.IsExact)
            return Decision(
                DeviceCapabilityOperation.ReviewFirmwareCandidate,
                "تقييم ملف Firmware مختار",
                CapabilityDecisionState.Blocked,
                false,
                "هوية الهاتف ليست مطابقة دقيقة وموثقة؛ لا يمكن اعتبار بيانات الموديل اليدوية دليل توافق.",
                "اقرأ رقم الموديل الحقيقي وDevice Code، ثم حل التعارضات من ملف هوية موثوق.");

        var integrityMatches = context.FirmwareIntegrity is
        {
            FileExists: true,
            SizeMatches: true,
            Sha256Matches: true
        } integrity && integrity.ItemId == firmware.Id;

        if (!integrityMatches)
            return Decision(
                DeviceCapabilityOperation.ReviewFirmwareCandidate,
                "تقييم ملف Firmware مختار",
                CapabilityDecisionState.Blocked,
                false,
                "لم ينجح التحقق الحالي من وجود الملف وحجمه وSHA-256 وربطه بسجل المكتبة نفسه.",
                "أعد قراءة الملف وحساب البصمة قبل أي تقييم آخر.");

        var manufacturerMatches = Normalize(device.Manufacturer) == Normalize(firmware.Manufacturer);
        var firmwareModel = Normalize(firmware.Model);
        var modelMatches = identity.Profile is not null &&
            (identity.Profile.ModelNumbers.Any(value => Normalize(value) == firmwareModel) ||
             Normalize(device.ModelNumber) == firmwareModel);

        if (!manufacturerMatches || !modelMatches)
            return Decision(
                DeviceCapabilityOperation.ReviewFirmwareCandidate,
                "تقييم ملف Firmware مختار",
                CapabilityDecisionState.Blocked,
                false,
                $"تعارض بيانات الملف والجهاز: الشركة مطابقة={manufacturerMatches}; الموديل الدقيق مطابق={modelMatches}.",
                "لا تستخدم الملف. يجب أن يطابق رقم الموديل الحقيقي، وليس الاسم التجاري المشابه فقط.");

        return Decision(
            DeviceCapabilityOperation.ReviewFirmwareCandidate,
            "تقييم ملف Firmware مختار",
            CapabilityDecisionState.ReviewRequired,
            false,
            "تطابق رقم الموديل والشركة وسلامة الملف المحلي فقط. لم يتم إثبات مصدر الناشر أو توقيعه أو توافق المنطقة/اللوحة/Bootloader أو سياسة Anti-Rollback.",
            "أكمل التحقق الرسمي من المصدر وتوقيع الناشر ونسخة اللوحة والمنطقة وإصدار Bootloader وAnti-Rollback. لا يعني هذا القرار السماح بالتفليش.");
    }

    private static Candidate ScoreProfile(DeviceSnapshot device, DeviceProfile profile)
    {
        var score = 0;
        var reasons = new List<string>();
        var conflicts = new List<string>();

        var modelNumber = HasValue(device.ModelNumber) ? device.ModelNumber : "";
        var modelMatched = modelNumber.Length > 0 &&
            profile.ModelNumbers.Any(value => EqualsNormalized(value, modelNumber));
        if (modelMatched)
        {
            score += 140;
            reasons.Add("مطابقة رقم الموديل exact");
        }

        var deviceCodeMatched = HasValue(device.DeviceCode) &&
            profile.DeviceCodes.Any(value => EqualsNormalized(value, device.DeviceCode));
        if (deviceCodeMatched)
        {
            score += 110;
            reasons.Add("مطابقة Device Code");
        }

        var productMatched = HasValue(device.Product) &&
            profile.ProductNames.Any(value => EqualsNormalized(value, device.Product));
        if (productMatched)
        {
            score += 75;
            reasons.Add("مطابقة Product ID");
        }

        var boardMatched = HasValue(device.Board) &&
            profile.BoardIds.Any(value => EqualsNormalized(value, device.Board));
        if (boardMatched)
        {
            score += 45;
            reasons.Add("مطابقة Board ID");
        }

        var socMatched = HasValue(device.Soc) &&
            profile.SocAliases.Any(value =>
                Normalize(device.Soc).Contains(Normalize(value), StringComparison.Ordinal) ||
                Normalize(value).Contains(Normalize(device.Soc), StringComparison.Ordinal));
        if (socMatched)
        {
            score += 15;
            reasons.Add("توافق اسم SoC");
        }

        var manufacturerMatched = HasValue(device.Manufacturer) &&
            ManufacturerMatches(profile.Manufacturer, device.Manufacturer);
        if (manufacturerMatched)
        {
            score += 20;
            reasons.Add("مطابقة الشركة المصنعة");
        }
        else if (HasValue(device.Manufacturer) && score > 0)
        {
            conflicts.Add($"الشركة المعلنة ({device.Manufacturer}) لا تطابق الشركة المسجلة ({profile.Manufacturer}).");
        }

        if (HasValue(device.ModelNumber) && modelNumber.Length > 0 &&
            profile.ModelNumbers.Length > 0 && !modelMatched && deviceCodeMatched)
        {
            reasons.Add("رقم الموديل لم يتطابق رغم مطابقة Device Code؛ يلزم مراجعة نسخة الجهاز.");
        }

        var aliasMatched = HasValue(device.Model) &&
            profile.ModelAliases.Any(alias => ContainsNormalized(device.Model, alias));
        if (aliasMatched)
        {
            score += 25;
            reasons.Add("مطابقة اسم تسويقي (دليل مساعد فقط)");
        }

        return new Candidate(profile, score, modelMatched, manufacturerMatched, reasons, conflicts);
    }

    private static bool IsVerifiedIdentitySource(DeviceProfile profile)
        => Uri.TryCreate(profile.IdentitySourceUrl, UriKind.Absolute, out var uri) &&
           uri.Scheme == Uri.UriSchemeHttps &&
           !string.IsNullOrWhiteSpace(profile.IdentitySource) &&
           !string.IsNullOrWhiteSpace(profile.IdentitySourceUrl);

    private static bool ManufacturerMatches(string profile, string actual)
    {
        var left = Normalize(profile);
        var right = Normalize(actual);
        if (left.Length == 0 || right.Length == 0)
            return false;

        return left == right ||
               left.Contains(right, StringComparison.Ordinal) ||
               right.Contains(left, StringComparison.Ordinal) ||
               (left.Contains("xiaomi", StringComparison.Ordinal) &&
                (right.Contains("redmi", StringComparison.Ordinal) ||
                 right.Contains("poco", StringComparison.Ordinal))) ||
               (right.Contains("xiaomi", StringComparison.Ordinal) &&
                (left.Contains("redmi", StringComparison.Ordinal) ||
                 left.Contains("poco", StringComparison.Ordinal))) ||
               (left.Contains("motorola", StringComparison.Ordinal) &&
                right.Contains("motorola", StringComparison.Ordinal));
    }

    private static bool ContainsNormalized(string source, string target)
    {
        var normalizedSource = Normalize(source);
        var normalizedTarget = Normalize(target);
        return normalizedTarget.Length > 3 &&
               normalizedSource.Contains(normalizedTarget, StringComparison.Ordinal);
    }

    private static bool EqualsNormalized(string left, string right)
    {
        var normalizedLeft = Normalize(left);
        return normalizedLeft.Length > 0 && normalizedLeft == Normalize(right);
    }

    private static string Normalize(string? value)
        => new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool HasValue(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           !UnknownValues.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool IsSafeSerial(string serial)
        => !string.IsNullOrWhiteSpace(serial) &&
           !serial.StartsWith("-", StringComparison.Ordinal) &&
           Regex.IsMatch(serial, @"^[A-Za-z0-9._:-]{1,128}$", RegexOptions.CultureInvariant);

    private static DeviceCapabilityDecision Decision(
        DeviceCapabilityOperation operation,
        string name,
        CapabilityDecisionState state,
        bool canExecute,
        string evidence,
        string requiredAction)
        => new(operation, name, state, canExecute && state == CapabilityDecisionState.Supported,
            evidence, requiredAction);

    private static DeviceIdentityResolution UnknownIdentity()
        => new(null, DeviceIdentityState.Unknown, 0, false, false, false, [],
            ["لم يتم العثور على ملف تعريف مدعوم أو بيانات هوية كافية."]);

    private sealed record Candidate(
        DeviceProfile Profile,
        int Score,
        bool ExactModelNumberMatched,
        bool ManufacturerMatched,
        IReadOnlyList<string> Reasons,
        IReadOnlyList<string> Conflicts);
}
