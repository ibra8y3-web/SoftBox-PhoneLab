using System;
using System.Collections.Generic;
using System.Linq;

namespace SoftBox.PhoneLab.Core.Recovery;

public enum OfficialRecoveryCaseKind
{
    GoogleFrp,
    XiaomiMiAccount,
    SamsungFrp,
    ScreenLock,
    MdmRelease,
    CarrierNetworkUnlock
}

public sealed record OfficialRecoveryResource(string Title, string Url, string Description);

public sealed record OfficialRecoveryWorkflow(
    OfficialRecoveryCaseKind Kind,
    string Id,
    string TitleAr,
    string SummaryAr,
    IReadOnlyList<string> Steps,
    IReadOnlyList<OfficialRecoveryResource> Resources);

/// <summary>
/// Six OEM/carrier/administrator-owned recovery tracks. These are official hand-offs,
/// not lock-bypass methods.
/// </summary>
public static class OfficialRecoveryWorkflowCatalog
{
    private static readonly IReadOnlyList<OfficialRecoveryWorkflow> Workflows =
    [
        new(
            OfficialRecoveryCaseKind.GoogleFrp,
            "google-frp-recovery",
            "استرداد حماية Google FRP",
            "استعادة حساب Google الذي كان مرتبطًا بالجهاز قبل إعادة ضبط المصنع، أو تقديم طلب دعم رسمي مع إثبات الملكية.",
            [
                "تأكد من أنك تستخدم حساب Google الذي كان مضافًا إلى الهاتف قبل إعادة الضبط.",
                "استعد الحساب من بوابة Google الرسمية عند نسيان كلمة المرور أو تعذر تسجيل الدخول.",
                "أكمل التحقق على شاشة إعداد Android نفسها؛ لا توجد واجهة عامة تتيح لبرنامج كمبيوتر إزالة FRP.",
                "عند تعذر استعادة الحساب، تواصل مع الشركة أو مركز الخدمة المعتمد مع إثبات الشراء."
            ],
            [
                new("استرداد حساب Google", "https://accounts.google.com/signin/recovery", "بوابة Google الرسمية لاسترداد الحساب."),
                new("شرح حماية الجهاز وFRP", "https://support.google.com/android/answer/9459346?hl=en", "شرح Google الرسمي لحماية الجهاز بعد إعادة الضبط.")
            ]),
        new(
            OfficialRecoveryCaseKind.XiaomiMiAccount,
            "xiaomi-mi-account-recovery",
            "استرداد حساب Xiaomi / Mi",
            "استعادة حساب Xiaomi أو تقديم طلب مراجعة قفل الجهاز من خلال قنوات Xiaomi الرسمية.",
            [
                "استخدم استرداد كلمة مرور Xiaomi للحساب المرتبط بالجهاز.",
                "عند ظهور قفل جهاز أو Activation Lock، أرسل طلب مراجعة من بوابة Xiaomi الرسمية.",
                "جهّز إثبات الشراء وبيانات الموديل التي تطلبها Xiaomi مباشرة في بوابتها الآمنة.",
                "انتظر قرار Xiaomi؛ SoftBox لا يتصل بخوادم الحساب ولا يغيّر حالة التنشيط."
            ],
            [
                new("استرداد حساب Xiaomi", "https://account.xiaomi.com/helpcenter/faq/en_US/02.faqs/03.password/faq-1", "إعادة تعيين كلمة مرور الحساب عبر Xiaomi."),
                new("طلب مراجعة جهاز مقفل", "https://account.xiaomi.com/helpcenter/faq/en_US/01.guide/11.appeal/faq-6", "نموذج المراجعة الرسمي للأجهزة المقفلة.")
            ]),
        new(
            OfficialRecoveryCaseKind.SamsungFrp,
            "samsung-frp-recovery",
            "استرداد Samsung / Google FRP",
            "استعادة حساب Google السابق أو حساب Samsung، ثم متابعة الدعم الرسمي عند استمرار قفل التنشيط.",
            [
                "تحقق من حساب Google السابق المرتبط بالجهاز؛ قد تظل حماية Google مطلوبة بعد إعادة الضبط.",
                "استعد حساب Samsung من بوابة الحساب الرسمية عند الحاجة.",
                "إذا تعذر إثبات الحساب، راجع مركز خدمة Samsung مع مستند ملكية الجهاز.",
                "لا تعتبر التفليش أو إعادة الضبط طريقة مضمونة لإزالة حماية الحساب."
            ],
            [
                new("استرداد حساب Google", "https://accounts.google.com/signin/recovery", "بوابة استرداد حساب Google."),
                new("حساب Samsung", "https://account.samsung.com/", "تسجيل الدخول واسترداد حساب Samsung."),
                new("مراكز خدمة Samsung", "https://www.samsung.com/eg/support/service-center/", "البحث عن مركز خدمة معتمد في مصر.")
            ]),
        new(
            OfficialRecoveryCaseKind.ScreenLock,
            "screen-lock-official-recovery",
            "استرداد PIN / Pattern / Screen Lock",
            "تحديد مسار الاسترداد الرسمي مع توضيح أثر خيارات إعادة الضبط على البيانات وحماية FRP.",
            [
                "جرّب وسائل الاسترداد الرسمية المرتبطة بحساب الشركة المصنّعة وإصدار Android/One UI.",
                "لا توجد طريقة عامة مدعومة لاستخراج PIN أو Pattern من الهاتف المقفل.",
                "قبل أي إعادة ضبط، وضّح للمالك أنها قد تمسح بيانات المستخدم ولا تضمن إزالة FRP أو Activation Lock.",
                "عند الحاجة إلى خدمة فنية، استخدم مركز الشركة المعتمد وإثبات الملكية."
            ],
            [
                new("إرشادات Google لقفل Android", "https://support.google.com/android/answer/7663172?hl=en", "خيارات Google الرسمية عند نسيان قفل الشاشة."),
                new("مراكز خدمة Samsung", "https://www.samsung.com/eg/support/service-center/", "دعم Samsung الرسمي ومراكز الخدمة.")
            ]),
        new(
            OfficialRecoveryCaseKind.MdmRelease,
            "mdm-authorized-release",
            "إزالة إدارة MDM بتفويض المؤسسة",
            "تجهيز طلب فك إدارة مؤسسية يقدمه مسؤول Device Owner أو Profile Owner أو مزود EMM المخوّل.",
            [
                "حدد المؤسسة المالكة لسياسة الإدارة أو مزود EMM المسؤول.",
                "قدّم مرجع الجهاز من خلال قناة المؤسسة الداخلية؛ لا تضع رموز دخول أو بيانات سرية في ملف SoftBox.",
                "يجب على مسؤول المؤسسة المخوّل تنفيذ Unenroll أو Release من وحدة الإدارة الرسمية.",
                "أعد فحص الجهاز بعد تأكيد المسؤول اكتمال الإزالة؛ غياب مؤشرات ADB وحده لا يثبت انتهاء الإدارة."
            ],
            [
                new("إرشادات Android Enterprise", "https://support.google.com/work/android/answer/14549362?hl=en", "إرشادات Google للإدارة المؤسسية وFRP المؤسسي.")
            ]),
        new(
            OfficialRecoveryCaseKind.CarrierNetworkUnlock,
            "carrier-authorized-network-unlock",
            "طلب فتح SIM / Network من المشغّل",
            "إعداد قائمة تحقق لمخاطبة شركة الاتصالات الأصلية؛ أهلية IMEI والموافقة ورمز الفتح تصدر من المشغّل فقط.",
            [
                "تواصل مع شركة الاتصالات الأصلية التي وضعت قفل الشبكة، ومن خلال صاحب الحساب أو المفوض عنه.",
                "اطلب فحص أهلية الجهاز والعقد وأي التزامات مالية من قناة المشغّل الآمنة.",
                "أدخل IMEI فقط في بوابة المشغّل الرسمية أو لدى موظف مخوّل؛ SoftBox لا يلتقطه أو يخزنه.",
                "نفّذ تعليمات الفتح التي يصدرها المشغّل فقط، ثم اختبر شريحة شبكة أخرى."
            ],
            [])
    ];

    public static IReadOnlyList<OfficialRecoveryWorkflow> All => Workflows;

    public static OfficialRecoveryWorkflow Get(OfficialRecoveryCaseKind kind)
        => Workflows.Single(workflow => workflow.Kind == kind);
}
