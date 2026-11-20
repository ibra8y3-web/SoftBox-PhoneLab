using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public enum DeviceCapabilityStatus
{
    Verified,
    Likely,
    NeedsAuthorization,
    Unknown
}

public sealed record DeviceCapabilityAssessment(
    string CapabilityId,
    string CapabilityName,
    DeviceCapabilityStatus Status,
    string StatusLabel,
    string Evidence,
    string Limitation);

public sealed record DeviceCapabilityMatrixSnapshot(
    string DeviceName,
    string Manufacturer,
    string ModelNumber,
    string DeviceCode,
    string Soc,
    string AndroidVersion,
    string ApiLevel,
    DateTimeOffset EvaluatedAtUtc,
    IReadOnlyList<DeviceCapabilityAssessment> Capabilities);

/// <summary>
/// Evaluates only evidence observed by the current SoftBox inspection and curated identity/PCB catalogues.
/// It intentionally does not infer Fastboot, EDL, BROM, bootloader state or firmware compatibility from ADB.
/// </summary>
public static class DeviceCapabilityMatrixService
{
    public static DeviceCapabilityMatrixSnapshot Evaluate(
        string deviceName,
        bool deviceSelected,
        bool adbAuthorized,
        DeviceSnapshot? snapshot,
        DeviceProfileMatch? profileMatch,
        BoardMap? boardMap,
        DateTimeOffset? evaluatedAtUtc = null)
    {
        deviceName = string.IsNullOrWhiteSpace(deviceName) ? "لا يوجد جهاز محدد" : deviceName.Trim();
        var currentSnapshot = deviceSelected && adbAuthorized ? snapshot : null;
        var match = currentSnapshot is null ? DeviceProfileMatch.Unknown : profileMatch ?? DeviceProfileMatch.Unknown;
        var capabilities = new List<DeviceCapabilityAssessment>
        {
            Assessment(
                "adb-transport",
                "اتصال ADB لهذا الجهاز",
                !deviceSelected ? DeviceCapabilityStatus.Unknown
                    : adbAuthorized ? DeviceCapabilityStatus.Verified
                    : DeviceCapabilityStatus.NeedsAuthorization,
                !deviceSelected ? "لم يتم اختيار جهاز"
                    : adbAuthorized ? "ADB مصرح به"
                    : "يحتاج موافقة ADB",
                !deviceSelected ? "لا يوجد جهاز محدد في قائمة الاتصال."
                    : adbAuthorized ? "أعاد اكتشاف الجهاز حالة device المصرح بها من ADB."
                    : "الجهاز ظاهر لكن حالة ADB لا تسمح بقراءة بياناته.",
                "تصريح ADB لا يعني أن Fastboot أو أوضاع خدمة USB الأخرى متاحة."),

            EvaluateIdentity(match, deviceSelected, adbAuthorized),
            EvaluateAndroidProperties(currentSnapshot, deviceSelected, adbAuthorized),
            EvaluateProcessor(currentSnapshot, deviceSelected, adbAuthorized),
            EvaluateBattery(currentSnapshot, deviceSelected, adbAuthorized),
            EvaluateBoardMap(currentSnapshot, boardMap, deviceSelected, adbAuthorized),

            Unknown(
                "fastboot-transport",
                "وضع Fastboot",
                "لم يتم إثبات ظهور الجهاز عبر fastboot devices في هذا التقييم.",
                "ADB وFastboot وضعان منفصلان؛ يلزم فحص النقل الفعلي قبل اعتبار الجهاز مدعومًا."),
            Unknown(
                "qualcomm-edl",
                "Qualcomm EDL / 9008",
                "لا توجد قراءة USB/PnP حالية تثبت وضع EDL أو تطابق معرفاته مع هذا الجهاز.",
                "يتطلب إثبات VID/PID والموديل ومبرمج Firehose موثوقًا ومطابقًا؛ لا يتم بدء أي تفليش."),
            Unknown(
                "mediatek-brom",
                "MediaTek BROM / Preloader",
                "لا توجد قراءة USB/PnP حالية تثبت وضع BROM أو Preloader لهذا الجهاز.",
                "يتطلب إثبات VID/PID وموديل SoC وDownload Agent موثوقًا ومطابقًا؛ لا يتم تجاوز الحماية."),
            Unknown(
                "bootloader-state",
                "حالة قفل Bootloader وAVB",
                "خصائص ADB وحدها لا تثبت حالة القفل أو سلسلة Verified Boot.",
                "يلزم استعلام مستقل موثوق من وضع Bootloader وتفسير خاص بالشركة والموديل."),
            Unknown(
                "firmware-compatibility",
                "توافق حزمة Firmware",
                "لم تتم مطابقة حزمة محددة مع الموديل والمنطقة وإصدار Bootloader وقيود الرجوع.",
                "وجود اسم الموديل أو بصمة SHA-256 لا يثبت مصدر الحزمة أو توقيعها أو صلاحية تثبيتها.")
        };

        return new DeviceCapabilityMatrixSnapshot(
            deviceName,
            currentSnapshot?.Manufacturer ?? "غير معروف",
            currentSnapshot?.ModelNumber ?? "غير معروف",
            currentSnapshot?.DeviceCode ?? "غير معروف",
            currentSnapshot?.Soc ?? "غير معروف",
            currentSnapshot?.AndroidVersion ?? "غير معروف",
            currentSnapshot?.ApiLevel ?? "غير معروف",
            (evaluatedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            capabilities);
    }

    private static DeviceCapabilityAssessment EvaluateIdentity(
        DeviceProfileMatch match,
        bool deviceSelected,
        bool adbAuthorized)
    {
        if (!deviceSelected)
            return Unknown("model-identity", "هوية الموديل", "لا يوجد جهاز محدد.", "اختر جهازًا ثم نفّذ فحص ADB.");
        if (!adbAuthorized)
            return NeedsAuthorization("model-identity", "هوية الموديل", "يلزم تصريح ADB لقراءة هوية الجهاز.", "لم تُستخدم بيانات فحص قديمة كأنها بيانات الجهاز الحالي.");
        if (match.Profile is null || match.Score <= 0)
            return Unknown("model-identity", "هوية الموديل", "لم توجد مطابقة في قاعدة ملفات تعريف الأجهزة.", "عدم وجود تطابق لا يعني أن الجهاز غير مدعوم.");
        if (match.Profile.IdentityVerified && match.Score >= 110)
            return Assessment(
                "model-identity",
                "هوية الموديل",
                DeviceCapabilityStatus.Verified,
                "مطابقة موثقة",
                $"تطابق كود أو رقم موديل بقوة {match.Score}. مصدر الهوية: {NonEmpty(match.Profile.IdentitySource)}.",
                "إثبات الهوية لا يثبت دعم كل بروتوكولات الصيانة أو توافق Firmware.");
        return Assessment(
            "model-identity",
            "هوية الموديل",
            DeviceCapabilityStatus.Likely,
            "مطابقة مرجحة",
            $"مطابقة بقاعدة بيانات بدرجة {match.Score} ({match.Confidence}). مصدر البيانات: {NonEmpty(match.Profile.IdentitySource)}.",
            "المصدر أو التطابق غير كافٍ لإثبات هوية اللوحة أو نسخة العتاد.");
    }

    private static DeviceCapabilityAssessment EvaluateAndroidProperties(
        DeviceSnapshot? snapshot,
        bool deviceSelected,
        bool adbAuthorized)
    {
        if (!deviceSelected)
            return Unknown("android-properties", "قراءة خصائص Android", "لم يتم اختيار جهاز.", "اختر جهازًا متصلًا.");
        if (!adbAuthorized)
            return NeedsAuthorization("android-properties", "قراءة خصائص Android", "الجهاز يحتاج تصريح USB debugging.", "لا تتم قراءة بيانات الجهاز قبل الموافقة.");
        if (snapshot is null)
            return Unknown("android-properties", "قراءة خصائص Android", "لم يُنفذ فحص الخصائص بعد.", "نفّذ فحص الجهاز لقراءة System Properties.");

        var observed = new[]
        {
            ("Android", snapshot.AndroidVersion),
            ("API", snapshot.ApiLevel),
            ("Build ID", snapshot.BuildId),
            ("Security Patch", snapshot.SecurityPatch)
        }.Where(pair => IsObserved(pair.Item2)).Select(pair => pair.Item1 + ": " + pair.Item2).ToArray();

        return observed.Length > 0
            ? Assessment(
                "android-properties",
                "قراءة خصائص Android",
                DeviceCapabilityStatus.Verified,
                "قراءة مباشرة",
                string.Join(" • ", observed),
                "القراءات تصف ما أبلغ به النظام وقت الفحص ولا تثبت سلامة العتاد أو اعتماد الشركة المصنعة.")
            : Unknown("android-properties", "قراءة خصائص Android", "لم تتوفر خصائص Android قابلة للتفسير.", "قد يمنع النظام أو إصدار الجهاز بعض القراءات.");
    }

    private static DeviceCapabilityAssessment EvaluateProcessor(
        DeviceSnapshot? snapshot,
        bool deviceSelected,
        bool adbAuthorized)
    {
        if (!deviceSelected)
            return Unknown("processor-abi", "معمارية المعالج وABI", "لا يوجد جهاز محدد.", "اختر جهازًا.");
        if (!adbAuthorized)
            return NeedsAuthorization("processor-abi", "معمارية المعالج وABI", "يلزم تصريح ADB.", "لا تستخدم بيانات قديمة.");
        if (snapshot is null)
            return Unknown("processor-abi", "معمارية المعالج وABI", "لم يُنفذ فحص الجهاز بعد.", "نفّذ فحص ADB.");
        if (!IsObserved(snapshot.CpuAbi) && !IsObserved(snapshot.Soc))
            return Unknown("processor-abi", "معمارية المعالج وABI", "لم يعلن النظام عن ABI أو SoC قابل للقراءة.", "لا يتم استنتاج شركة الشرائح من اسم الموديل فقط.");

        return Assessment(
            "processor-abi",
            "معمارية المعالج وABI",
            DeviceCapabilityStatus.Verified,
            "قيمة مرصودة",
            $"SoC: {NonEmpty(snapshot.Soc)} • ABI: {NonEmpty(snapshot.CpuAbi)} • الأنوية: {(snapshot.CpuCores > 0 ? snapshot.CpuCores.ToString(System.Globalization.CultureInfo.InvariantCulture) : "غير متاح")}",
            "هذه معلومات تعريفية مرصودة فقط، ولا تثبت توافق أدوات التفليش أو توفر مبرمج خدمة.");
    }

    private static DeviceCapabilityAssessment EvaluateBattery(
        DeviceSnapshot? snapshot,
        bool deviceSelected,
        bool adbAuthorized)
    {
        if (!deviceSelected)
            return Unknown("battery-telemetry", "قراءات البطارية", "لا يوجد جهاز محدد.", "اختر جهازًا.");
        if (!adbAuthorized)
            return NeedsAuthorization("battery-telemetry", "قراءات البطارية", "يلزم تصريح ADB.", "لا تتم قراءة بيانات البطارية قبل الموافقة.");
        if (snapshot is null)
            return Unknown("battery-telemetry", "قراءات البطارية", "لم يُنفذ الفحص بعد.", "نفّذ فحص الجهاز.");
        if (snapshot.BatteryPercent is null &&
            snapshot.BatteryVoltageMillivolts is null &&
            snapshot.BatteryTemperatureCelsius is null &&
            !IsObserved(snapshot.BatteryHealth) &&
            !IsObserved(snapshot.BatteryStatus))
            return Unknown("battery-telemetry", "قراءات البطارية", "لم تُرجع واجهة Android قراءات بطارية صالحة.", "غياب القراءة لا يعني وجود عطل في البطارية.");

        var evidence = new List<string>();
        if (snapshot.BatteryPercent is int percent) evidence.Add($"الشحن {percent}%");
        if (snapshot.BatteryVoltageMillivolts is int voltage) evidence.Add($"الجهد {voltage} mV");
        if (snapshot.BatteryTemperatureCelsius is double temperature) evidence.Add($"الحرارة {temperature:0.0}°C");
        if (IsObserved(snapshot.BatteryHealth)) evidence.Add("الصحة " + snapshot.BatteryHealth);
        if (IsObserved(snapshot.BatteryStatus)) evidence.Add("الحالة " + snapshot.BatteryStatus);
        return Assessment(
            "battery-telemetry",
            "قراءات البطارية",
            DeviceCapabilityStatus.Verified,
            "قراءات مباشرة",
            string.Join(" • ", evidence),
            "قراءات النظام ليست اختبار سعة أو مقاومة داخلية ولا تثبت سلامة البطارية.");
    }

    private static DeviceCapabilityAssessment EvaluateBoardMap(
        DeviceSnapshot? snapshot,
        BoardMap? map,
        bool deviceSelected,
        bool adbAuthorized)
    {
        if (!deviceSelected)
            return Unknown("exact-pcb-map", "خريطة PCB مطابقة", "لم يتم اختيار جهاز.", "يلزم جهاز محدد وبيانات موديل.");
        if (!adbAuthorized || snapshot is null)
            return NeedsAuthorization("exact-pcb-map", "خريطة PCB مطابقة", "يلزم فحص موديل الجهاز المصرح به.", "لن تُعرض الخريطة العامة كأنها خريطة دقيقة.");
        if (map is not null && BoardMapCatalog.IsVerifiedForDevice(map, snapshot))
            return Assessment(
                "exact-pcb-map",
                "خريطة PCB مطابقة",
                DeviceCapabilityStatus.Verified,
                "خريطة موثقة مطابقة",
                $"{map.DisplayName} • {map.AccuracyLevel} • {map.SourceName} • راجعها {map.VerifiedBy}",
                "التطابق مبني على رقم الموديل/كود الجهاز وبيانات المصدر والمراجعة؛ راجع نسخة اللوحة الفعلية قبل الإصلاح.");
        return Unknown(
            "exact-pcb-map",
            "خريطة PCB مطابقة",
            map?.AccuracyNotice ?? "لا توجد خريطة PCB موثقة مطابقة لهذا الجهاز.",
            "المخطط العام أو خريطة موديل قريب لا يُعدان دليل توافق.");
    }

    private static DeviceCapabilityAssessment NeedsAuthorization(
        string id, string name, string evidence, string limitation)
        => Assessment(id, name, DeviceCapabilityStatus.NeedsAuthorization, "يحتاج تصريحًا", evidence, limitation);

    private static DeviceCapabilityAssessment Unknown(
        string id, string name, string evidence, string limitation)
        => Assessment(id, name, DeviceCapabilityStatus.Unknown, "غير معروف", evidence, limitation);

    private static DeviceCapabilityAssessment Assessment(
        string id,
        string name,
        DeviceCapabilityStatus status,
        string label,
        string evidence,
        string limitation)
        => new(id, name, status, label, evidence, limitation);

    private static bool IsObserved(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.Equals("غير متاح", StringComparison.OrdinalIgnoreCase) &&
           !value.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
           !value.Equals("unavailable", StringComparison.OrdinalIgnoreCase);

    private static string NonEmpty(string? value)
        => IsObserved(value) ? value! : "غير متاح";
}
