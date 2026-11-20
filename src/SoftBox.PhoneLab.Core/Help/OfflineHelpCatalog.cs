namespace SoftBox.PhoneLab.Core.Help;

public sealed record OfflineHelpArticle(
    string Id,
    string TitleAr,
    string TitleEn,
    string Category,
    string[] Keywords,
    string BodyAr,
    string RelatedModuleId);

/// <summary>
/// Curated offline help for the real current SoftBox tool scope.
/// Each article documents actual behavior and limitations, not unimplemented buttons.
/// </summary>
public static class OfflineHelpCatalog
{
    public static IReadOnlyList<OfflineHelpArticle> Articles { get; } =
    [
        new("adb-authorize", "توصيل الهاتف وتفويض ADB", "Connect a phone and authorize ADB", "الاتصال",
            ["USB", "ADB", "Android", "authorization", "تصحيح", "تفويض"],
            """
            1) استخدم كابل USB يدعم نقل البيانات، وليس الشحن فقط.
            2) فعّل خيارات المطوّر ثم USB debugging من إعدادات الهاتف.
            3) صِل الهاتف وافتح قفل الشاشة.
            4) وافق على نافذة Allow USB debugging، ويمكن اختيار السماح دائمًا لهذا الكمبيوتر الشخصي الموثوق.
            5) اضغط تحديث الأجهزة في SoftBox، وتأكد أن حالة ADB هي device وليست unauthorized أو offline.
            إذا ظهر الهاتف في مستكشف Windows فقط فهذا قد يكون MTP ولا يعني أن ADB مصرح به.
            لا توافق على تصحيح USB على كمبيوتر لا تثق به. لا يقرأ SoftBox كلمات مرور قفل الشاشة.
            """, "device-center"),
        new("usb-pnp", "فحص أجهزة USB وتعريفات Windows", "Windows USB and driver inventory", "الاتصال",
            ["USB", "VID", "PID", "SetupAPI", "PnP", "driver", "Qualcomm", "MediaTek"],
            """
            افتح USB Doctor ثم نفّذ فحص أجهزة USB المتصلة. يعرض Windows PnP الاسم والشركة وVID/PID والخدمة ورمز المشكلة المتاح.
            بعض VID/PID الشائعة تُعرض كمؤشر وضع فقط: Qualcomm 05C6:9008، MediaTek 0E8D:0003، Samsung 04E8:6860، Google ADB/Fastboot.
            المعرف لا يثبت موديل الهاتف أو توافق اللوحة أو وجود programmer/DA مناسب. هذا الفحص قراءة فقط ولا يثبت أو يزيل أو يعيد تشغيل تعريف.
            لا تستخدم تعريفات مجهولة ولا تعطّل حماية Windows لتجاوز مشكلة تعريف.
            """, "usb-doctor"),
        new("android-files", "إدارة ملفات Android", "Android shared-storage file manager", "Android",
            ["file", "manager", "push", "pull", "sdcard", "DCIM", "ملفات", "نقل"],
            """
            اختر هاتفًا حاصلًا على تصريح ADB ثم افتح Android Service Center > مدير الملفات.
            استخدم مسارات مساحة التخزين المشتركة مثل /sdcard/DCIM أو /sdcard/Pictures.
            الاستعراض يستخدم أوامر قراءة. رفع ملف أو إنشاء مجلد يغيّر بيانات المستخدم، ولذلك يجب اختيار الوجهة الصحيحة ومراجعة التأكيد.
            التنزيل يستخدم ADB pull والحفظ المحلي. لا يمكن للأداة تجاوز Scoped Storage أو الوصول إلى /data وبيانات التطبيقات الخاصة.
            """, "android-service"),
        new("android-apps", "إدارة التطبيقات وتثبيت APK", "Installed applications and APK install", "Android",
            ["APK", "package", "install", "update", "dumpsys", "تطبيق", "تثبيت"],
            """
            اقرأ التطبيقات الخارجية من مدير التطبيقات، واختر حزمة لعرض dumpsys package.
            لتثبيت أو تحديث APK، اختر ملفًا محليًا؛ يفحص SoftBox Manifest ويعرض اسم الحزمة والإصدار وSHA-256، ثم يطلب موافقتك قبل ADB install -r.
            سيظل Android يطبق قواعد التوقيع والتوافق والصلاحيات. فشل اختلاف التوقيع لا يتم تجاوزه. لا توجد إزالة تطبيقات أو تجاوز أقفال.
            """, "android-service"),
        new("apk-lab", "مختبر APK وتحليل الملف", "APK Lab inspection and comparison", "التطبيقات",
            ["APK", "manifest", "AXML", "SHA256", "permissions", "signature", "مقارنة"],
            """
            اختر ملف APK محليًا لتحليل Manifest النصي أو Android Binary XML، اسم الحزمة والإصدار وSDK والصلاحيات وبصمة SHA-256.
            يمكن مقارنة ملفين في بيانات الإصدار والصلاحيات وأسماء مدخلات ZIP. المقارنة حسب المسار، وليست مقارنة محتوى كل ملف داخلي.
            اكتشاف ملفات التوقيع أو APK Signing Block ليس تحققًا تشفيريًا من الشهادة أو هوية الناشر، وليس فحصًا كاملًا للبرمجيات الخبيثة.
            لا يتم تثبيت الملف أو تشغيله أو رفعه.
            """, "apk-lab"),
        new("smart-backup", "النسخ الاحتياطي الذكي", "Smart Backup and verification", "النسخ",
            ["backup", "SHA256", "ZIP", "restore", "نسخ", "استعادة"],
            """
            اختر مجلدًا عامًا متاحًا عبر ADB مثل /sdcard/DCIM أو Documents، وحدد مسار ZIP على الكمبيوتر.
            ينقل SoftBox الملفات المسموح بها إلى منطقة مؤقتة، ينشئ ZIP ويحسب SHA-256 لكل ملف ضمن Manifest.
            استخدم زر التحقق لاحقًا لإعادة حساب الحجم والبصمة لكل ملف واكتشاف التغيير أو الفقد.
            الأرشيف الحالي غير مشفر، ولا يشمل بيانات التطبيقات الخاصة أو ملفات النظام، ولا يوفر استعادة تلقائية إلى الهاتف.
            احفظه في مكان خاص وآمن. لا تعتبر النسخة مكتملة لبيانات الهاتف كلها.
            """, "smart-backup"),
        new("firmware-library", "مكتبة Firmware المحلية", "Offline firmware library", "Firmware",
            ["firmware", "ROM", "OTA", "SHA256", "model", "region", "CSC", "فريموير"],
            """
            اختر ملفًا محليًا لاستيراده إلى المكتبة. أضف الشركة والموديل والمنطقة والإصدار ومعلومة المصدر التي لديك.
            يحسب SoftBox SHA-256 ويعيد حسابه عند التحقق للتأكد أن النسخة المحلية لم تتغير منذ الاستيراد.
            تطابق البصمة مع سجل الاستيراد لا يثبت أن المصدر رسمي أو أن الملف مطابق للهاتف أو المنطقة أو قيود الرجوع.
            التنزيل التلقائي والتحقق من توقيع الناشر ومحرك توافق التفليش ليست متاحة بعد.
            لا تفلّش ملفًا بناءً على الاسم أو حجم الملف وحده.
            """, "firmware-center"),
        new("network-diagnostics", "تشخيص الشبكة", "Network Diagnostics", "الاتصال",
            ["DNS", "Ping", "ICMP", "Wi-Fi", "network", "زمن الاستجابة"],
            """
            يعرض Network Diagnostics واجهات Windows المحلية وعناوين IPv4/IPv6 والبوابات وخوادم DNS.
            أدخل اسم نطاق أو IP لطلب حل DNS ثم إرسال ICMP echo واحد إلى عنوان واحد.
            قد تحجب الجدران النارية ICMP مع استمرار DNS؛ لذلك فشل Ping لا يثبت انقطاع الإنترنت.
            هذه بيانات الكمبيوتر وليست معلومات Wi-Fi الخاصة بالهاتف. لم يتم تنفيذ فحص منافذ أو مسح شبكة.
            """, "network-diagnostics"),
        new("workflow", "أتمتة سير العمل", "Read-only Workflow Automation", "الأتمتة",
            ["workflow", "automation", "JSON", "batch", "steps", "قالب"],
            """
            أنشئ قالبًا من خطوات القراءة فقط المتاحة مثل properties وbattery وstorage وuptime وthird-party-apps وservices.
            احفظ القالب محليًا بصيغة JSON، ثم اختر هاتفًا مصرحًا به وشغّل الخطوات بالتتابع.
            يتوقف المحرك عند فشل خطوة، ويمكن إلغاء التشغيل. لا يقبل أوامر Shell حرة أو حذف ملفات أو تفليش أو تجاوز حماية.
            """, "workflow-automation"),
        new("usb-hints", "مؤشرات Qualcomm وMediaTek", "Qualcomm and MediaTek USB hints", "الشركات والمعالجات",
            ["EDL", "9008", "Preloader", "BROM", "Qualcomm", "MediaTek"],
            """
            يعرض USB Doctor بعض VID/PID المعروفة كمؤشرات أولية لوضع الاتصال.
            VID/PID 05C6:9008 مؤشر Qualcomm EDL شائع، و0E8D:0003 مؤشر من عائلة MediaTek Preloader/BROM.
            هذه المؤشرات لا تحدد طراز الهاتف أو إصدار المعالج أو حالة Secure Boot أو توافق programmer/DA.
            لا يوفر SoftBox في هذا النطاق عمليات EDL/BROM منخفضة المستوى أو تفليشًا أو تجاوزًا للحماية.
            """, "qualcomm-tools"),
        new("support-bundle", "حزمة الدعم وتنقيح السجلات", "Support bundle and log redaction", "الدعم",
            ["support", "logs", "token", "password", "redaction", "سجلات"],
            """
            افتح Support & Logs واختر فقط الملفات التي تريد إضافتها بصيغ TXT أو LOG أو JSON.
            ينقّح SoftBox الحقول الشائعة مثل Password وToken وAuthorization وSerial، ويضيف معلومات النظام والإصدار ثم ينشئ ZIP محليًا.
            التنقيح آلي وليس ضمانًا كاملًا؛ راجع الملفات الأصلية والحزمة الناتجة قبل مشاركتها.
            لا يتم رفع الحزمة تلقائيًا ولا إرسالها إلى خادم.
            """, "support-logs"),
        new("repair-history", "سجل الصيانة حسب الجهاز", "Per-device repair history", "إدارة الصيانة",
            ["history", "repair", "device", "serial", "تاريخ", "سجل"],
            """
            تُسجل أحداث اكتشاف الجهاز وفقد/منح تصريح ADB وقراءة الخصائص ومزامنة Agent وتصدير التقرير.
            يستخدم سجل الجهاز بصمة SHA-256 لمعرف ADB ويعرض Serial مقنعًا بدل الرقم الخام.
            البيانات محفوظة محليًا في حساب Windows الحالي وغير مشفرة في هذه النسخة؛ احمِ حساب Windows وملفات المستخدم.
            """, "repair-history")
    ];

    public static IReadOnlyList<OfflineHelpArticle> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Articles;

        var terms = query.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return Articles
            .Select(article => new
            {
                Article = article,
                Score = terms.Sum(term =>
                    (article.TitleAr.Contains(term, StringComparison.OrdinalIgnoreCase) ? 5 : 0) +
                    (article.TitleEn.Contains(term, StringComparison.OrdinalIgnoreCase) ? 4 : 0) +
                    (article.Keywords.Any(keyword => keyword.Contains(term, StringComparison.OrdinalIgnoreCase)) ? 3 : 0) +
                    (article.BodyAr.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Article.TitleAr, StringComparer.Ordinal)
            .Select(item => item.Article)
            .ToArray();
    }

    public static OfflineHelpArticle? Find(string id)
        => Articles.FirstOrDefault(article => article.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
