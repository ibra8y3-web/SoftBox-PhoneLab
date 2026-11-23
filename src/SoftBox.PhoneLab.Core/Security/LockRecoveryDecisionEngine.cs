namespace SoftBox.PhoneLab.Core.Security;

public enum LockRecoveryRoute
{
    PreviousCredential,
    AccountRecovery,
    OfficialEraseAndRestore,
    ManufacturerService,
    OwnershipReview
}

public sealed record LockRecoveryContext(
    string Platform,
    string Manufacturer,
    string OperatingSystemVersion,
    string OneUiVersion,
    bool PreviousCredentialKnown,
    bool LockChangedWithin72Hours,
    bool AccountAccessAvailable,
    bool FindMyOrFindHubEnabled,
    bool BackupAvailable,
    bool ProofOfOwnershipAvailable);

public sealed record LockRecoveryAdvice(
    LockRecoveryRoute Route,
    string Title,
    string Explanation,
    string Steps,
    string OfficialUrl,
    bool DataLossPossible,
    bool RequiresPreviousCredential);

/// <summary>
/// Evidence-led, non-bypass recovery guidance for an owner who forgot a screen lock.
/// The engine never attempts to inspect, change, guess or remove a credential.
/// </summary>
public static class LockRecoveryDecisionEngine
{
    private const string ApplePasscodeResetUrl = "https://support.apple.com/en-us/105039";
    private const string AppleEraseUrl = "https://support.apple.com/en-us/105090";
    private const string SamsungPreviousLockUrl = "https://www.samsung.com/ae/support/mobile-devices/unlock-your-samsung-galaxy-with-a-previous-screen-lock-method/";
    private const string GoogleCantUnlockUrl = "https://support.google.com/android/answer/7663172?hl=en";
    private const string GoogleAccountRecoveryUrl = "https://accounts.google.com/signin/recovery";
    private const string GoogleFindHubUrl = "https://support.google.com/accounts/answer/6160491";
    private const string GoogleDeviceProtectionUrl = "https://support.google.com/android/answer/9459346";

    public static LockRecoveryAdvice Evaluate(LockRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var platform = Normalize(context.Platform);
        var manufacturer = Normalize(context.Manufacturer);

        if (!context.ProofOfOwnershipAvailable)
        {
            return new LockRecoveryAdvice(
                LockRecoveryRoute.OwnershipReview,
                "أثبت ملكية الجهاز أولًا",
                "لا يبدأ SoftBox أي إجراء استرداد قبل وجود إثبات ملكية أو تفويض موثق من صاحب الجهاز.",
                "جهّز فاتورة الشراء أو مستند الملكية أو تفويض الصيانة. لا تجمع رقم PIN أو كلمة مرور الحساب داخل SoftBox.",
                ManufacturerSupport(manufacturer),
                false,
                false);
        }

        if (platform is "ios" or "ipados")
        {
            if (context.PreviousCredentialKnown && context.LockChangedWithin72Hours &&
                IsAtLeastMajor(context.OperatingSystemVersion, 17))
            {
                return new LockRecoveryAdvice(
                    LockRecoveryRoute.PreviousCredential,
                    "جرّب Passcode Reset من Apple",
                    "في iOS/iPadOS 17 أو أحدث، تسمح Apple باستخدام رمز القفل السابق مرة واحدة خلال 72 ساعة من تغييره، إذا كان الرمز السابق معروفًا ولم تنتهِ صلاحيته.",
                    "من شاشة القفل اختر Forgot Passcode? ثم Enter Previous Passcode، وأدخل الرمز السابق بنفسك على iPhone. SoftBox لا يستقبل الرمز ولا يرسله.",
                    ApplePasscodeResetUrl,
                    false,
                    true);
            }

            if (context.AccountAccessAvailable && context.FindMyOrFindHubEnabled)
            {
                return new LockRecoveryAdvice(
                    LockRecoveryRoute.OfficialEraseAndRestore,
                    "استعادة iPhone عبر مسار Apple الرسمي",
                    "إذا لم يعد Passcode Reset متاحًا، يتيح Apple مسار إعادة تعيين رسميًا. إعادة التعيين تمسح بيانات الجهاز وقد تتطلب Apple Account بعد ذلك.",
                    "تحقق أولًا من النسخة الاحتياطية وإمكانية تسجيل الدخول إلى Apple Account. لا تبدأ المسح إذا لم تكن بيانات الحساب متاحة.",
                    AppleEraseUrl,
                    true,
                    false);
            }

            return new LockRecoveryAdvice(
                LockRecoveryRoute.AccountRecovery,
                "استعد Apple Account قبل أي إعادة تعيين",
                "لا يوجد دليل على توفر Passcode Reset أو متطلبات المسح الرسمي. حافظ على البيانات وتحقق من حساب Apple أولًا.",
                "استعد Apple Account من موقع Apple، ثم راجع خيارات الدعم الرسمية. لا توجد طريقة معتمدة لفتح البيانات المشفرة دون رمز صالح.",
                "https://iforgot.apple.com/",
                false,
                false);
        }

        if (platform == "android" && manufacturer == "samsung" &&
            context.PreviousCredentialKnown && context.LockChangedWithin72Hours &&
            IsAtLeastVersion(context.OneUiVersion, 6, 1))
        {
            return new LockRecoveryAdvice(
                LockRecoveryRoute.PreviousCredential,
                "استخدم Previous Screen Lock من Samsung",
                "تدعم أجهزة Galaxy المؤهلة مع One UI 6.1 أو أحدث استخدام PIN/كلمة المرور/النمط السابق خلال 72 ساعة من تغييره، ما دام لم يُحذف الرمز السابق.",
                "بعد محاولات القفل التي تعرض خيار Forgot PIN/Password/Pattern، اختره ثم أدخل الرمز السابق على الهاتف. SoftBox لا يستقبل الرمز.",
                SamsungPreviousLockUrl,
                false,
                true);
        }

        if (platform == "android" && !context.AccountAccessAvailable)
        {
            return new LockRecoveryAdvice(
                LockRecoveryRoute.AccountRecovery,
                "استعد حساب Google قبل إعادة ضبط Android",
                "إعادة ضبط جهاز محمي قد تطلب حساب Google سبق إضافته ومزامنته على الجهاز. عدم امتلاك الحساب قد يمنع إكمال الإعداد بعد المسح.",
                "استعد حساب Google أولًا، وتأكد من وجود نسخة احتياطية. لا تبدأ Factory Reset قبل التأكد من بيانات الحساب.",
                GoogleAccountRecoveryUrl,
                false,
                false);
        }

        if (platform == "android" && context.AccountAccessAvailable && context.FindMyOrFindHubEnabled)
        {
            return new LockRecoveryAdvice(
                LockRecoveryRoute.OfficialEraseAndRestore,
                "مسار Google الرسمي لإعادة ضبط Android",
                "توضح Google أن خيار عدم القدرة على فتح Android قد يتطلب مسح الجهاز. هذا يحذف البيانات وقد تبقى حماية الجهاز التي تطلب حسابًا سبق مزامنته.",
                "راجع دليل Google، وتأكد من النسخة الاحتياطية وكلمة مرور حساب Google السابق قبل أي مسح. استخدم Find Hub أو تعليمات الشركة الرسمية فقط.",
                GoogleCantUnlockUrl,
                true,
                false);
        }

        if (platform == "android")
        {
            return new LockRecoveryAdvice(
                LockRecoveryRoute.ManufacturerService,
                "مركز الشركة المعتمد هو المسار الآمن",
                "لا يوجد مسار استرداد ذاتي موثوق تم إثبات توفره لهذا الطراز. لا يمكن لـADB أو تعريف USB إزالة تشفير شاشة القفل.",
                "تواصل مع مركز الصيانة المعتمد ومعك إثبات الملكية. قد يتطلب الحل الرسمي إعادة ضبط تمحو البيانات؛ اطلب تأكيدًا مكتوبًا قبل الإجراء.",
                ManufacturerSupport(manufacturer),
                true,
                false);
        }

        return new LockRecoveryAdvice(
            LockRecoveryRoute.ManufacturerService,
            "استخدم استرداد الشركة المصنّعة",
            "لا يوجد بروتوكول عام وآمن لفتح قفل أنظمة الهاتف المختلفة. تختلف إجراءات الاسترداد حسب النظام والطراز وإصدار البرنامج.",
            "تواصل مع الشركة المصنّعة أو مزود النظام مع إثبات الملكية، ولا تستخدم أدوات تجاوز أو ملفات غير موثوقة.",
            ManufacturerSupport(manufacturer),
            false,
            false);
    }

    private static bool IsAtLeastMajor(string value, int required)
        => Version.TryParse(NormalizeVersion(value), out var version) && version.Major >= required;

    private static bool IsAtLeastVersion(string value, int major, int minor)
    {
        if (!Version.TryParse(NormalizeVersion(value), out var version)) return false;
        return version.Major > major || (version.Major == major && version.Minor >= minor);
    }

    private static string NormalizeVersion(string value)
    {
        var digits = new string((value ?? string.Empty).TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return digits.Trim('.');
    }

    private static string Normalize(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string ManufacturerSupport(string manufacturer) => manufacturer switch
    {
        "samsung" => "https://www.samsung.com/eg/support/",
        "huawei" or "honor" => "https://consumer.huawei.com/en/support/",
        "xiaomi" or "redmi" or "poco" => "https://www.mi.com/global/support/",
        "oppo" => "https://support.oppo.com/",
        "realme" => "https://www.realme.com/global/support",
        "vivo" => "https://www.vivo.com/en/support",
        "oneplus" => "https://service.oneplus.com/global",
        "tecno" => "https://www.tecno-mobile.com/support/",
        "infinix" => "https://battery.infinixmobility.com/care/support",
        "nothing" => "https://support.nothing.tech/hc/en-us",
        _ => GoogleDeviceProtectionUrl
    };
}
