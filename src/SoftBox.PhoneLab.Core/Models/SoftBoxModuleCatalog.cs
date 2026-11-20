namespace SoftBox.PhoneLab.Core.Models;

public enum ModuleImplementationStatus
{
    Planned,
    InDevelopment,
    Implemented,
    Tested,
    Verified,
    Unsupported
}

public sealed record SoftBoxModuleFeature(
    string Id,
    string NameAr,
    string NameEn,
    string DetailsAr,
    ModuleImplementationStatus Status,
    string? ActionId = null);

public sealed record SoftBoxModule(
    string Id,
    int Order,
    string GroupAr,
    string NameAr,
    string NameEn,
    string SummaryAr,
    ModuleImplementationStatus Status,
    IReadOnlyList<SoftBoxModuleFeature> Features);

/// <summary>
/// Audited product module inventory. ActionIds are assigned only to real existing commands/windows.
/// This catalog is metadata, not a simulation of unfinished device functionality.
/// </summary>
public static class SoftBoxModuleCatalog
{
    public const int ExpectedModuleCount = 36;

    public static IReadOnlyList<SoftBoxModule> All { get; } =
    [
        new("dashboard", 1, "الرئيسية", "لوحة التحكم", "Dashboard",
            "ملخص حالة SoftBox والأجهزة المتصلة والعمليات المتاحة حاليًا.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("live-usb", "حالة محرك ADB", "ADB engine status", "يعرض توفر أداة ADB الفعلية وحالة اكتشاف الأجهزة.", ModuleImplementationStatus.Implemented),
                new("connected-list", "قائمة الأجهزة المتصلة", "Connected devices", "قائمة أجهزة ADB تتحدث تلقائيًا؛ لوحة مؤشرات العمليات والإحصائيات الشاملة لم تكتمل بعد.", ModuleImplementationStatus.Implemented),
                new("dashboard-reports", "إحصائيات العمليات والتقارير", "Operations analytics", "لم يتم بناء سجل مركزي للعمليات والإشعارات والإحصائيات بعد.", ModuleImplementationStatus.Planned)
            ]),
        new("device-center", 2, "مركز الأجهزة", "مركز الأجهزة", "Device Center",
            "اكتشاف أجهزة Android المصرح بها وإظهار حالة اتصال كل جهاز.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("adb-discovery", "اكتشاف ADB تلقائيًا", "Automatic ADB discovery", "فحص دوري كل 3 ثوانٍ مع التمييز بين الجهاز المصرح به وغير المصرح.", ModuleImplementationStatus.Implemented, "RefreshDevices"),
                new("multi-device-list", "عرض أكثر من جهاز", "Multi-device list", "يمكن عرض عدة أجهزة واختيار جهاز للفحص؛ التنفيذ الجماعي المتوازي لم يبدأ.", ModuleImplementationStatus.Implemented),
                new("wireless-adb", "اتصال ADB لاسلكي", "Wireless ADB", "لا توجد واجهة مستقلة لإدارة الاقتران اللاسلكي حاليًا.", ModuleImplementationStatus.Planned)
            ]),
        new("device-intelligence", 3, "مركز الأجهزة", "ذكاء الأجهزة", "Device Intelligence",
            "تحليل بيانات Android ومطابقتها مع ملفات تعريف الموديلات المتاحة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("properties", "قراءة خصائص الجهاز وSoC والذاكرة والتخزين والبطارية", "Device properties", "تُقرأ القيم التي يسمح بها النظام عبر ADB؛ القراءة لا تثبت سلامة القطع.", ModuleImplementationStatus.Implemented, "InspectDevice"),
                new("profiles", "مطابقة الموديل وقاعدة ملفات التعريف", "Model profile matching", "كتالوج محلي من SoftBox مع مصدر مجتمعي؛ المطابقة المجتمعية احتمالية وليست إثباتًا للوحة.", ModuleImplementationStatus.Implemented, "UpdateDeviceCatalog"),
                new("board-map", "استيراد خريطة PCB موثقة للموديل", "Verified PCB map import", "يتطلب ملف خريطة موثقًا ومتطابقًا مع الموديل ومصدره؛ الخريطة العامة ليست تخطيطًا دقيقًا.", ModuleImplementationStatus.Implemented, "ImportBoardMap"),
                new("board-view", "فتح ملف BoardView في برنامج خارجي مستقل", "Native BoardView viewer", "يفتح الصيغ المدعومة في TRACE Boardviewer المنفصل؛ لا يحول كل الصيغ إلى المجسم ثلاثي الأبعاد.", ModuleImplementationStatus.Implemented, "OpenBoardView"),
                new("compatibility", "مصفوفة القدرات والتوافق لكل موديل", "Per-model capability matrix", "لم تُبنَ بعد قاعدة موثقة تربط كل عملية بموديل وإصدار وبروتوكول.", ModuleImplementationStatus.Planned)
            ]),
        new("android-service", 4, "أدوات Android", "مركز صيانة Android", "Android Service Center",
            "مركز موحد لأدوات ADB وAgent وإدارة ملفات التخزين المشترك وقراءة التطبيقات والأوامر الآمنة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("adb-core", "اكتشاف أجهزة ADB وقراءة معلوماتها", "ADB discovery and inspection", "مدمج في محرك الاكتشاف والفحص الحالي؛ تشغيل الفحص العام من قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("agent-bridge", "تثبيت PhoneLab Agent وجلب نتائج الاختبارات", "PhoneLab Agent bridge", "اتصال محلي عبر ADB forward، ويتطلب موافقة المستخدم على الهاتف؛ واجهة الاختبارات موجودة في قسم تشخيص العتاد.", ModuleImplementationStatus.Implemented),
                new("agent-update", "تحديث PhoneLab Agent", "PhoneLab Agent update", "يفحص ويحدّث مكوّن Agent ضمن دعم التطبيق الحالي.", ModuleImplementationStatus.Implemented, "UpdateAgent"),
                new("logs", "قراءة سجلات Android المسموح بها", "Android logs", "يجمع حزمة أدلة محدودة؛ السجلات قد تحتوي بيانات شخصية ويجب مراجعتها قبل المشاركة. أداة جمعها في قسم الدعم والسجلات.", ModuleImplementationStatus.Implemented),
                new("app-manager", "عرض التطبيقات الخارجية وتفاصيل الحزم", "Installed app inspection", "يعرض أسماء الحزم الخارجية وتفاصيل dumpsys دون تثبيت أو إزالة أو تعديل التطبيق؛ افتح مركز أدوات Android.", ModuleImplementationStatus.Implemented, "OpenAndroidDeviceTools"),
                new("file-manager", "مدير ملفات Android عبر ADB", "Android shared-storage file manager", "استعراض ورفع وتنزيل ملفات وإنشاء مجلدات داخل التخزين المشترك المسموح به فقط؛ لا يصل إلى /data أو بيانات التطبيقات الخاصة.", ModuleImplementationStatus.Implemented, "OpenAndroidDeviceTools"),
                new("command-center", "مركز أوامر Android بقائمة قراءة فقط", "Allow-listed Android command center", "أوامر getprop والبطارية والتخزين وuptime والتطبيقات والخدمات من قائمة ثابتة؛ لا يقبل أوامر Shell حرة.", ModuleImplementationStatus.Implemented, "OpenAndroidDeviceTools")
            ]),
        new("apk-lab", 5, "أدوات Android", "مختبر APK", "APK Lab",
            "تحليل محلي للملفات دون تثبيت أو تعديل: بيانات Manifest، الصلاحيات، قابلية قراءة ZIP وبصمة SHA-256.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("apk-manifest", "قراءة AndroidManifest.xml والصلاحيات والإصدار", "Manifest, permissions and version", "تم اختبار XML النصي وAndroid Binary XML واستخراج اسم الحزمة والإصدار وSDK والصلاحيات.", ModuleImplementationStatus.Tested, "OpenApkLab"),
                new("apk-archive", "قراءة مدخلات أرشيف ZIP وفحص قابلية فكها", "ZIP entry readability", "تم اختبار قراءة المدخلات ورفض ملف ZIP غير صالح؛ لا يُعد ذلك تحققًا من CRC أو مصدر الملف.", ModuleImplementationStatus.Tested),
                new("apk-hash", "حساب SHA-256 للملف الكامل", "Full-file SHA-256", "تم اختبار مطابقة SHA-256 المحسوب مع بايتات الملف الفعلية؛ لا يثبت ذلك موثوقية الناشر.", ModuleImplementationStatus.Tested),
                new("apk-v1-signature-files", "اكتشاف ملفات توقيع APK v1", "Detect v1 signing files", "اختبار رصد ملفات META-INF الخاصة بتوقيع v1؛ لا يتحقق من صحة التوقيع التشفيرية.", ModuleImplementationStatus.Tested),
                new("apk-signing-block", "رصد بنية APK Signing Block", "Detect APK Signing Block", "الكود يرصد البنية وموضعها في الأرشيف، لكن هذا المسار لم يُغطَّ بعد باختبار مستقل ولا يتحقق من التوقيع.", ModuleImplementationStatus.Implemented),
                new("apk-signature-verification", "التحقق التشفيري من الشهادة وسلسلة الثقة", "Cryptographic signature verification", "غير منفذ؛ وجود ملفات توقيع لا يثبت صحتها أو موثوقية الناشر.", ModuleImplementationStatus.Planned),
                new("apk-version-diff", "مقارنة إصدارين وتحليل فروق الصلاحيات والملفات", "APK version and permission comparison", "يقارن اسم الحزمة وبيانات الإصدار وSHA-256 والصلاحيات وأسماء مدخلات الأرشيف بين ملفين محليين؛ لا يقارن محتوى كل ملف ولا يثبت صحة التوقيع.", ModuleImplementationStatus.Tested)
            ]),
        new("samsung-tools", 6, "الشركات والمعالجات", "أدوات Samsung", "Samsung Tools",
            "الموجود حاليًا تعريف Samsung USB الاختياري وأدوات Android العامة؛ لا يوجد بعد محرك صيانة Samsung خاص.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("samsung-driver", "مثبت تعريف Samsung USB الرسمي الاختياري", "Samsung USB driver installer", "مضمن في حزمة Windows مع تحقق مثبت التعريف؛ مكان تشغيله الأساسي قسم طبيب USB، ولا يعني دعم Download Mode أو التفليش.", ModuleImplementationStatus.Implemented),
                new("samsung-adb", "قراءة معلومات Samsung عبر ADB العام", "Generic Samsung ADB inspection", "استخدم محرك ذكاء الأجهزة للفحص العام؛ لم تُبنَ عمليات خاصة بالموديلات.", ModuleImplementationStatus.Implemented),
                new("samsung-service", "أدوات Download Mode وعمليات Samsung الخاصة", "Samsung service operations", "غير منفذة؛ لا يوجد دعم تفليش أو تجاوز حماية.", ModuleImplementationStatus.Planned)
            ]),
        new("xiaomi-tools", 7, "الشركات والمعالجات", "أدوات Xiaomi وRedmi وPOCO", "Xiaomi Tools",
            "لا يوجد حاليًا محرك Xiaomi مخصص؛ المتاح فقط بروتوكولات Android العامة عند دعم الجهاز.",
            ModuleImplementationStatus.Planned,
            [
                new("xiaomi-identity", "تحديد موديلات Xiaomi ومناطقها", "Xiaomi model and region identity", "تحتاج قاعدة موديلات موثقة ومصفوفة توافق.", ModuleImplementationStatus.Planned),
                new("xiaomi-fastboot", "عمليات Xiaomi Fastboot الرسمية المتوافقة", "Supported Xiaomi Fastboot", "لم يتم تنفيذ إجراءات الشركة الخاصة.", ModuleImplementationStatus.Planned)
            ]),
        new("android-brands", 8, "الشركات والمعالجات", "شركات Android الأخرى", "Android Brands",
            "قاعدة التعريف العامة قد تتعرف على شركات متعددة؛ وحدات الصيانة الخاصة بكل شركة لم تُبنَ بعد.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("brand-recognition", "قراءة الشركة والموديل من خصائص Android", "Generic brand recognition", "تُقرأ بيانات الشركة والموديل المتاحة من الجهاز عبر محرك ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("brand-catalog", "مطابقة ملفات تعريف المجتمع", "Community device identities", "مصدر مجتمعي يساعد في التعرف ولا يثبت إصدار اللوحة أو التوافق؛ تحديث الكتالوج موجود في قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("brand-specific", "وحدات صيانة خاصة بكل شركة", "Brand-specific service modules", "Pixel وMotorola وOPPO وvivo وغيرها لا تملك بعد وحدات مستقلة مكتملة.", ModuleImplementationStatus.Planned)
            ]),
        new("qualcomm-tools", 9, "الشركات والمعالجات", "أدوات Qualcomm", "Qualcomm Tools",
            "قراءة تلميح SoC من Android ورصد مؤشر USB الشائع EDL/9008 عبر جرد Windows PnP؛ لا يوجد بعد بروتوكول EDL أو استعادة منخفضة المستوى.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("qualcomm-soc", "قراءة تلميح المعالج من النظام", "SoC identification hint", "قراءة تعريفية فقط من بيانات Android، ولا تكفي لتحديد منصة EDL أو اختيار programmer؛ محرك الهوية في قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("qualcomm-edl", "رصد مؤشر USB Qualcomm EDL / 9008", "Qualcomm EDL USB mode hint", "يرصد VID/PID الشائع 05C6:9008 إذا ظهر في Windows PnP؛ لا يفتح قناة EDL ولا يتحقق من programmer أو توافق الجهاز.", ModuleImplementationStatus.Implemented, "OpenUsbDoctor"),
                new("qualcomm-recovery", "استعادة Qualcomm الرسمية المتوافقة", "Supported Qualcomm recovery", "غير منفذة؛ يلزم مصدر رسمي وملف مطابق للموديل وسلسلة ثقة موثقة.", ModuleImplementationStatus.Planned)
            ]),
        new("mediatek-tools", 10, "الشركات والمعالجات", "أدوات MediaTek", "MediaTek Tools",
            "قراءة تلميح SoC من Android ورصد مؤشر USB الشائع MediaTek Preloader/BROM عبر Windows PnP؛ لا توجد أدوات DA أو استعادة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("mediatek-soc", "قراءة تلميح المعالج من Android", "SoC identification hint", "قد يظهر تعريف المعالج ضمن خصائص الجهاز؛ لا يثبت وضع Preloader. الفحص العام من قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("mediatek-preloader", "رصد مؤشر USB MediaTek Preloader/BROM", "MediaTek Preloader/BROM USB hint", "يرصد VID/PID الشائع 0E8D:0003 عند ظهوره في Windows PnP؛ لا يقرأ بروتوكول BROM ولا يختار DA ولا ينفذ تفليشًا.", ModuleImplementationStatus.Implemented, "OpenUsbDoctor"),
                new("mediatek-recovery", "الاستعادة الرسمية المتوافقة", "Supported MediaTek recovery", "غير منفذة؛ يلزم ملف DA موثوق ومتوافق ومصدر رسمي للجهاز المحدد.", ModuleImplementationStatus.Planned)
            ]),
        new("firmware-center", 11, "Firmware والاستعادة", "مركز Firmware", "Firmware Center",
            "المتاح فحص أرشيف Firmware محليًا وحساب SHA-256؛ التنزيل والتوافق والتنفيذ غير موجودة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("firmware-inspection", "فحص أرشيف محلي وحساب SHA-256", "Local archive inspection and SHA-256", "يقرأ بنية ZIP وعناصر OTA الشائعة فقط؛ لا يثبت المصدر أو التوافق أو سلامة التوقيع.", ModuleImplementationStatus.Implemented, "InspectFirmware"),
                new("firmware-library", "مكتبة ملفات مصنفة حسب الشركة والموديل والمنطقة", "Firmware library", "لم يتم بناء قاعدة بيانات ومكتبة تنزيل بعد.", ModuleImplementationStatus.Planned),
                new("firmware-download", "تنزيل واستكمال وإدارة التنزيلات", "Firmware downloads", "لم يتم بناء مدير التنزيلات بعد.", ModuleImplementationStatus.Planned),
                new("firmware-compatibility", "محرك توافق موثق قبل الاستخدام", "Firmware compatibility engine", "لم يتم تنفيذ محرك يفحص الموديل والمنطقة وقيود الرجوع.", ModuleImplementationStatus.Planned)
            ]),
        new("usb-doctor", 12, "الاتصال والتشخيص", "طبيب USB", "USB Doctor",
            "يجمع فحص ADB/Fastboot مع جرد Windows PnP لأجهزة USB وVID/PID ورموز مشكلات الجهاز؛ لا يثبت أو يزيل تعريفات.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("usb-transports", "فحص أوضاع ADB وFastboot المتاحة", "ADB and Fastboot transport inspection", "استعلامات قراءة فقط؛ لا تكتشف كل أوضاع الشركات مثل EDL وDownload وPreloader.", ModuleImplementationStatus.Implemented, "UsbTransport"),
                new("usb-pnp-inventory", "جرد أجهزة Windows USB وقراءة VID/PID والحالة", "Windows USB PnP inventory", "يستخدم SetupAPI وConfiguration Manager لقراءة الأجهزة الحالية ورموز المشكلة والخدمة المرتبطة دون تعديل الجهاز.", ModuleImplementationStatus.Implemented, "OpenUsbDoctor"),
                new("usb-mode-hints", "تمييز مؤشرات Qualcomm وMediaTek وSamsung وGoogle", "Known USB mode hints", "كتالوج محدود لمعرفات VID/PID الشائعة؛ لا يثبت الموديل أو التوافق ولا ينفذ بروتوكول الخدمة.", ModuleImplementationStatus.Implemented, "OpenUsbDoctor"),
                new("usb-setup", "إرشادات وتجهيز اتصال USB", "USB connection setup", "يفتح تعليمات إعداد أدوات USB وADB ولا يثبت تعريفات مجهولة.", ModuleImplementationStatus.Implemented, "SetupUsb"),
                new("connection-help", "مساعدة توصيل الهاتف وحل اتصال ADB", "USB connection help", "تعليمات الاتصال الحالية في البرنامج.", ModuleImplementationStatus.Implemented, "ConnectionHelp"),
                new("samsung-driver", "تثبيت تعريف Samsung الرسمي الاختياري", "Samsung official driver", "مضمن في حزمة Windows؛ لا يعني دعم Download Mode أو عمليات صيانة خاصة.", ModuleImplementationStatus.Implemented, "InstallSamsungDriver"),
                new("windows-driver-health", "فحص حالة PnP ومشكلات تعريف USB", "Windows PnP device health", "يعرض حالة الجهاز ورمز المشكلة المبلغ عنه من Windows؛ لا يتحقق من توقيع التعريف أو يغيره.", ModuleImplementationStatus.Implemented, "OpenUsbDoctor")
            ]),
        new("smart-backup", 13, "الاتصال والتشخيص", "النسخ الاحتياطي الذكي", "Smart Backup",
            "نسخ مجلدات التخزين المشترك عبر ADB إلى ZIP محلي مع SHA-256 لكل ملف والتحقق من النسخة؛ التشفير والجدولة والاستعادة إلى الهاتف غير مكتملة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("workshop-backup", "نسخ واستعادة قاعدة بيانات مركز الصيانة", "Workshop database backup", "نسخ JSON محلي واستعادته؛ النسخ الحالي غير مشفر. فتح تبويب النسخ يتم من هذا القسم.", ModuleImplementationStatus.Implemented, "OpenWorkshopBackup"),
                new("phone-backup", "نسخ مجلدات التخزين المشترك من Android", "Android shared-storage backup", "ينقل المجلد المحدد عبر ADB إلى ZIP محلي ويحسب SHA-256 لكل ملف؛ لا ينسخ بيانات التطبيقات الخاصة أو النظام.", ModuleImplementationStatus.Implemented, "OpenPhoneBackup"),
                new("backup-verify", "التحقق من النسخة ومطابقة بصمات الملفات", "Backup archive verification", "يفحص Manifest ويعيد حساب SHA-256 ومقارنة حجم كل ملف داخل ZIP؛ مغطى باختبارات آلية.", ModuleImplementationStatus.Tested, "OpenPhoneBackup"),
                new("backup-encryption", "تشفير النسخ بكلمة مرور وجدولة النسخ", "Encrypted scheduled backups", "لم يتم تنفيذ التشفير أو المجدول؛ الأرشيفات الحالية غير مشفرة ويجب حفظها بمكان آمن.", ModuleImplementationStatus.Planned)
            ]),
        new("phone-link", 14, "الاتصال والتشخيص", "استوديو الهاتف", "Phone Link Studio",
            "عرض شاشة Android عبر scrcpy مدمج في مساحة العمل؛ تسجيل الشاشة وتعدد الجلسات غير مكتمل.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("screen-mirror", "عرض والتحكم في شاشة هاتف مصرح به", "Authorized screen mirroring and control", "يستخدم scrcpy الرسمي؛ يحتاج اتصال ADB مصرحًا به.", ModuleImplementationStatus.Implemented, "MirrorScreen"),
                new("screen-record", "تسجيل الشاشة وإدارة التسجيلات", "Screen recording", "لا توجد واجهة تسجيل مدمجة بعد.", ModuleImplementationStatus.Planned),
                new("multi-session", "إدارة عدة جلسات عرض مستقلة", "Multi-device screen sessions", "الاكتشاف المتعدد موجود، لكن إدارة عدة عروض مستقلة غير منفذة.", ModuleImplementationStatus.Planned)
            ]),
        new("hardware-diagnostics", 15, "الاتصال والتشخيص", "تشخيص العتاد", "Hardware Diagnostics",
            "توجد اختبارات Agent وCamera2 وقراءات النظام والقياس الخارجي؛ بعض الفحوص يدوية أو تتطلب معدات.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("agent-tests", "اختبارات الشاشة واللمس والصوت والاهتزاز والحساسات", "Agent hardware screening tests", "اختبارات تتطلب موافقة المستخدم على الهاتف؛ النتائج لا تعادل قياسات معملية معايرة.", ModuleImplementationStatus.Implemented, "OpenPhoneTests"),
                new("camera-tests", "جرد Camera2 والتقاط صور وفحص AF", "Camera2 route tests", "المسارات التي يكشفها مصنع الجهاز فقط؛ لا يقيس جودة العدسة بصريًا. تُدار اختبارات Agent من نفس شاشة تشخيص العتاد.", ModuleImplementationStatus.Implemented),
                new("screen-photo", "تحليل صورة فعلية للشاشة", "Physical screen photo analysis", "يعرض مرشحات بكسلات وتفاوت إضاءة، وليس إثباتًا نهائيًا لعيب الشاشة.", ModuleImplementationStatus.Implemented, "AnalyzeScreen"),
                new("bench-measurements", "قراءات جهاز قياس خارجي متوافق", "External bench measurements", "بروتوكول Serial JSON-Lines للقراءات فقط؛ لا يصدر أوامر طاقة.", ModuleImplementationStatus.Implemented, "MeasurementRig"),
                new("board-level", "قياسات اللوحة والمكونات الكهربائية", "Board-level electrical tests", "تتطلب Fixture ومخططات ومجسات آمنة مناسبة للموديل.", ModuleImplementationStatus.Unsupported)
            ]),
        new("network-diagnostics", 16, "الاتصال والتشخيص", "تشخيص الشبكات", "Network Diagnostics",
            "تشخيص حقيقي لشبكة Windows المحلية وحل DNS وPing لهدف يحدده المستخدم؛ رصد شبكة الهاتف ما زال غير منفذ.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("host-network-interfaces", "قراءة واجهات Windows وعناوين IP والبوابات وDNS", "Windows network interfaces", "يقرأ بيانات واجهات الشبكة المحلية المتاحة من النظام دون إرسال طلبات خارجية.", ModuleImplementationStatus.Implemented),
                new("host-network-probe", "حل DNS واختبار ICMP لهدف يختاره المستخدم", "User-selected DNS and ICMP probe", "يجري حل DNS ثم طلب Ping واحدًا لعنوان واحد؛ قد يحجب الجدار الناري ICMP ولا يعني فشل DNS.", ModuleImplementationStatus.Implemented, "OpenNetworkDiagnostics"),
                new("phone-network", "قراءة معلومات الشبكة المسموح بها من الهاتف وتشخيص ADB اللاسلكي", "Android network observations and ADB Wireless", "لم يُنفذ هذا المسار؛ لا تُعرض بيانات هاتف خاصة أو نتائج غير متاحة.", ModuleImplementationStatus.Planned)
            ]),
        new("smart-repair-ai", 17, "الذكاء والمعرفة", "مساعد الإصلاح الذكي", "Smart Repair AI",
            "لا يوجد مساعد AI حاليًا؛ المتاح فتح أدلة إصلاح أصلية من iFixit دون نسخ محتواها.",
            ModuleImplementationStatus.Planned,
            [
                new("repair-guides", "البحث عن أدلة إصلاح خارجية", "External repair guide search", "يفتح مصادر iFixit الأصلية؛ هذه ليست ميزة ذكاء اصطناعي.", ModuleImplementationStatus.Implemented, "RepairGuides"),
                new("evidence-reasoning", "تحليل الأدلة وترتيب احتمالات العطل", "Evidence-based repair reasoning", "لم يتم بناء محرك استدلال أو مساعد AI بعد.", ModuleImplementationStatus.Planned),
                new("offline-knowledge", "قاعدة معرفة محلية تعمل دون إنترنت", "Offline repair knowledge", "لم يتم إنشاء قاعدة معرفة محلية قابلة للبحث بعد.", ModuleImplementationStatus.Planned)
            ]),
        new("one-click-diagnostic", 18, "الذكاء والمعرفة", "الفحص الشامل بزر واحد", "One-Click Diagnostic",
            "زر الفحص الحالي يقرأ بيانات النظام ويحدّث حالة المكونات؛ لا ينفذ كل اختبارات العتاد تلقائيًا.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("device-inspect", "قراءة بيانات الجهاز وتحديث تقرير المكونات", "Device inspection", "قراءات ADB المتاحة دون تغيير إعدادات الهاتف؛ الفحص العام موجود في قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("automated-suite", "تنسيق اختبارات غير مدمرة في جلسة واحدة", "Coordinated diagnostic suite", "لا يوجد حتى الآن Orchestrator يطلب الموافقات وينسق كل الاختبارات.", ModuleImplementationStatus.Planned)
            ]),
        new("safe-recovery", 19, "Firmware والاستعادة", "الاستعادة الآمنة", "Safe Recovery",
            "يوجد فحص أرشيف فقط؛ لا توجد عمليات تفليش أو استعادة رسمية مفعلة في هذا الإصدار.",
            ModuleImplementationStatus.Planned,
            [
                new("firmware-precheck", "فحص أولي لبنية ملف Firmware وبصمته", "Firmware pre-inspection", "الفحص المحلي متاح في قسم Firmware Center فقط، ولا يثبت التوقيع أو التوافق.", ModuleImplementationStatus.Implemented),
                new("recovery-gate", "بوابة تحقق للتوافق والموافقة والنسخ الاحتياطي", "Recovery safety gate", "لم يتم تنفيذ بوابة مركزية تمنع جميع الوحدات من تنفيذ عملية حساسة.", ModuleImplementationStatus.Planned),
                new("official-recovery", "إجراءات استعادة رسمية حسب الموديل", "Official model-specific recovery", "غير منفذة؛ لا يوجد تفليش أو فتح Bootloader.", ModuleImplementationStatus.Unsupported)
            ]),
        new("workflow-automation", 20, "الأتمتة والإنتاجية", "أتمتة سير العمل", "Workflow Automation",
            "إنشاء وحفظ وتشغيل سلاسل فحص Android من خطوات قراءة فقط محددة مسبقًا مع تتبع النتائج والإلغاء.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("workflow-builder", "إنشاء وحفظ قوالب Workflow بصيغة JSON", "Workflow builder and JSON templates", "محرر خطوات من قائمة سماح، مع حفظ وتحميل ملفات JSON محلية والتحقق من المخطط قبل القبول.", ModuleImplementationStatus.Implemented, "OpenAndroidWorkflow"),
                new("workflow-runner", "تشغيل متتابع مع تقدم وإلغاء وتقرير لكل خطوة", "Sequential workflow runner", "ينفذ خطوات القراءة فقط على الجهاز المحدد، ويتوقف عند الفشل أو الإلغاء ويسجل ناتج كل خطوة؛ لا ينفذ أوامر Shell حرة.", ModuleImplementationStatus.Implemented, "OpenAndroidWorkflow")
            ]),
        new("batch-operations", 21, "الأتمتة والإنتاجية", "العمليات الجماعية", "Batch Operations",
            "تنفيذ أوامر Android غير المدمرة على أجهزة ADB المصرح بها بالتوازي المحدود وعزل نتيجة كل جهاز.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("batch-selection", "اختيار مجموعة أجهزة مصرح بها", "Authorized multi-device selection", "يتيح اختيار عدة أجهزة من قائمة الاكتشاف الفعلية ويربط كل نتيجة بالمعرف المحدد.", ModuleImplementationStatus.Implemented, "OpenAndroidBatchTools"),
                new("batch-runner", "تنفيذ متوازٍ محدود لأوامر قراءة فقط", "Bounded read-only batch runner", "ينفذ أوامر الكتالوج الآمنة بحد أقصى 3 أجهزة متزامنة مع إلغاء مشترك ونتيجة مستقلة لكل جهاز.", ModuleImplementationStatus.Implemented, "OpenAndroidBatchTools")
            ]),
        new("repair-history", 22, "إدارة مركز الصيانة", "سجل الصيانة", "Repair History",
            "سجل أوامر وفعاليات المركز موجود؛ سجل تاريخي موحد لكل جهاز وتشخيصاته غير مكتمل.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("workshop-events", "سجل فعاليات وأوامر مركز الصيانة", "Workshop activity history", "يحفظ محليًا آخر 5000 فعالية مرتبطة بالعملاء والأوامر والمخزون؛ يفتح تبويب العمليات والسجل في الإدارة العالمية.", ModuleImplementationStatus.Implemented, "OpenGlobalWorkshopHistory"),
                new("device-history", "تاريخ الفحوص والأجهزة والتقارير لكل Serial", "Per-device repair history", "لم يتم ربط جميع جلسات التشخيص بسجل جهاز مستمر بعد.", ModuleImplementationStatus.Planned)
            ]),
        new("workshop-manager", 23, "إدارة مركز الصيانة", "إدارة مركز الصيانة", "Workshop Manager",
            "إدارة العملاء وأوامر الإصلاح والحالات وتكاليف العمل والضمان وبيانات المركز متاحة محليًا.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("customers", "إدارة العملاء", "Customer management", "إضافة وتعديل بيانات العملاء مع تخزين محلي.", ModuleImplementationStatus.Implemented),
                new("repair-orders", "إنشاء أوامر الصيانة ومتابعة الحالات", "Repair orders", "أرقام Ticket وحالة الجهاز ونتائج ما قبل/بعد الإصلاح.", ModuleImplementationStatus.Implemented, "OpenWorkshop"),
                new("warranty", "الإعدادات الدولية والضمان وسجل الفني", "International profile and warranty", "العملة واللغة والمنطقة الزمنية ومدة الضمان وسجل المتابعة وتقارير HTML.", ModuleImplementationStatus.Implemented),
                new("quotes-invoices", "إيصالات الاستلام وعروض الأسعار والفواتير", "Receipts, quotes and invoices", "لا توجد دورة موافقات وفواتير مالية مكتملة بعد.", ModuleImplementationStatus.Planned)
            ]),
        new("parts-inventory", 24, "إدارة مركز الصيانة", "مخزون قطع الغيار", "Parts Inventory",
            "إضافة الأصناف وتعديلها وحركات الصرف والإضافة والتنبيه بانخفاض المخزون متاحة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("stock-items", "إدارة الأصناف والكميات وحد إعادة الطلب", "Inventory items", "يحفظ SKU والكمية والتكلفة والموقع محليًا؛ يفتح تبويب المخزون داخل مركز الصيانة.", ModuleImplementationStatus.Implemented, "OpenWorkshopInventory"),
                new("stock-movements", "حركات المخزون وسجلها", "Stock movements", "يمنع الرصيد السالب ويسجل سبب الحركة.", ModuleImplementationStatus.Implemented),
                new("supplier-purchasing", "الموردون والمشتريات وخصم القطع من أمر الإصلاح", "Suppliers and repair consumption", "لم يتم ربط الموردين والمشتريات باستهلاك أمر الصيانة.", ModuleImplementationStatus.Planned)
            ]),
        new("service-reports", 25, "إدارة مركز الصيانة", "تقارير الخدمة والإحصائيات", "Service Reports",
            "تقارير تشخيص JSON وتقارير ورشة HTML/TXT وتصدير CSV موجودة؛ لوحة التحليلات المالية غير مكتملة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("device-json-report", "تقرير تشخيص الجهاز بصيغة JSON", "Device diagnostic JSON report", "يتضمن القراءات والاختبارات وحدود التفسير.", ModuleImplementationStatus.Implemented, "ExportDeviceReport"),
                new("workshop-html", "تقرير صيانة HTML قابل للطباعة أو PDF من المتصفح", "Printable workshop report", "يدعم العربية والإنجليزية وبيانات المركز والضمان؛ يفتح تبويب التقارير في الإدارة العالمية.", ModuleImplementationStatus.Implemented, "OpenGlobalWorkshopReports"),
                new("workshop-csv", "تصدير أوامر الصيانة والمخزون CSV", "Repair and inventory CSV export", "تصدير UTF-8 مع حماية حقول CSV من تفسيرها كصيغ.", ModuleImplementationStatus.Implemented),
                new("analytics", "تحليلات الأعطال والإيرادات وأداء الفنيين", "Service analytics", "لا توجد لوحة تحليلات شاملة مبنية على قاعدة بيانات موحدة بعد.", ModuleImplementationStatus.Planned)
            ]),
        new("plugin-store", 26, "التوسعة والمكونات", "متجر الإضافات", "Plugin Store",
            "لا توجد حاليًا بنية تثبيت أو توقيع أو إدارة صلاحيات للإضافات.",
            ModuleImplementationStatus.Planned,
            [
                new("plugin-discovery", "فهرس وبحث الإضافات", "Plugin catalog", "لم يبدأ التنفيذ.", ModuleImplementationStatus.Planned),
                new("plugin-signing", "التحقق من المصدر والتوقيع والاعتماديات", "Plugin signature verification", "لم يبدأ التنفيذ.", ModuleImplementationStatus.Planned)
            ]),
        new("plugin-sdk", 27, "التوسعة والمكونات", "حزمة تطوير الإضافات", "Plugin SDK",
            "لا يوجد Plugin Contracts أو SDK مستقل أو نموذج إضافة قابل للبناء بعد.",
            ModuleImplementationStatus.Planned,
            [
                new("plugin-contracts", "واجهات Plugin API وManifest وإصدارات", "Plugin contracts and manifest", "لم يبدأ التنفيذ.", ModuleImplementationStatus.Planned),
                new("plugin-sample", "مثال إضافة وتوثيق المطور", "Sample plugin and developer docs", "لم يبدأ التنفيذ.", ModuleImplementationStatus.Planned)
            ]),
        new("offline-center", 28, "النظام والحماية", "مركز العمل دون إنترنت", "Offline Center",
            "الوظائف المحلية الأساسية تعمل دون إنترنت بعد تنزيل الحزمة؛ لا توجد لوحة مركزية لإدارة الحالة المحلية.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("local-adb", "أدوات ADB والفحوص المحلية", "Local ADB and diagnostics", "تعمل بأدوات الحزمة المحلية بعد تثبيتها؛ مدخل الاكتشاف الأساسي موجود في مركز الأجهزة.", ModuleImplementationStatus.Implemented),
                new("local-workshop", "قاعدة ورشة العمل والنسخ المحلي", "Local workshop records", "قاعدة JSON محلية ونسخ يدوي؛ ليست مزامنة متعددة الفروع. نسخة الورشة تُفتح من إدارة مركز الصيانة.", ModuleImplementationStatus.Implemented),
                new("offline-status", "لوحة حالة Offline وإدارة الحزم المحلية", "Offline status dashboard", "لم يتم إنشاء شاشة موحدة لحالة الإنترنت والحزم المخزنة.", ModuleImplementationStatus.Planned)
            ]),
        new("security-privacy", 29, "النظام والحماية", "الأمان والخصوصية", "Device Security & Privacy",
            "هناك ضوابط لأوامر ADB وموافقة Agent وفحص للقراءة فقط؛ تشفير البيانات المحلية والإدارة الأمنية المركزية غير مكتملين.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("adb-arguments", "تمرير وسائط البرامج دون بناء أوامر Shell نصية", "Safe process arguments", "AdbClient يستخدم ArgumentList ويقيد Serial ومعلمات Agent المسموح بها.", ModuleImplementationStatus.Implemented),
                new("agent-consent", "موافقة المستخدم قبل تشغيل جسر Agent", "On-device consent", "جسر Agent لا يبدأ إلا بعد موافقة المستخدم داخل تطبيق الهاتف.", ModuleImplementationStatus.Implemented),
                new("read-only-forensics", "فحص USB وFirmware والسجلات بوضع قراءة فقط", "Read-only forensic operations", "لا ينفذ هذا المسار Flash أو Unlock أو Wipe أو تجاوز أقفال. فحص أوضاع النقل في طبيب USB.", ModuleImplementationStatus.Implemented),
                new("encrypted-data", "تشفير قاعدة البيانات والنسخ وإدارة مفاتيح", "At-rest encryption and key management", "نسخ الورشة غير مشفرة حتى الآن.", ModuleImplementationStatus.Planned)
            ]),
        new("user-technician-management", 30, "النظام والحماية", "المستخدمون والفنيون والصلاحيات", "User & Technician Management",
            "اسم الفني حقل في أوامر الصيانة فقط؛ لا توجد حسابات أو تسجيل دخول أو أدوار وصلاحيات.",
            ModuleImplementationStatus.Planned,
            [
                new("technician-field", "تسجيل اسم الفني على أمر الصيانة", "Technician attribution field", "حقل اسم فني وليس حساب مستخدم موثق؛ يظهر مع أوامر الصيانة في إدارة مركز الصيانة.", ModuleImplementationStatus.Implemented),
                new("accounts", "حسابات ومصادقة وأدوار وصلاحيات", "Accounts and roles", "لم يبدأ التنفيذ.", ModuleImplementationStatus.Planned),
                new("audit-user", "قفل الجلسة وسجل عمليات حسب هوية المستخدم", "Session locking and per-user audit", "لم يبدأ التنفيذ.", ModuleImplementationStatus.Planned)
            ]),
        new("update-center", 31, "النظام والحماية", "مركز التحديثات", "Update Center",
            "تحديث كتالوج الموديلات موجود عبر الإنترنت؛ لا يوجد محدّث ذاتي لتطبيق SoftBox أو حزم موثقة.",
            ModuleImplementationStatus.Planned,
            [
                new("catalog-update", "تحديث كتالوج الموديلات", "Device catalog refresh", "يحدّث بيانات الموديلات ويخزن نسخة محلية؛ مدخل التحديث في قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("application-updater", "تحديث تطبيق SoftBox والتحقق من التوقيع", "Application updater", "لا يوجد تنزيل وتوقيع وتثبيت ذاتي للتطبيق.", ModuleImplementationStatus.Planned),
                new("release-channels", "قنوات Stable/Beta والرجوع الآمن", "Release channels", "لم يبدأ التنفيذ.", ModuleImplementationStatus.Planned)
            ]),
        new("notification-center", 32, "النظام والحماية", "مركز الإشعارات", "Notification Center",
            "رسالة الحالة الحالية متاحة؛ لا يوجد صندوق إشعارات محفوظ وقابل للقراءة والتصفية.",
            ModuleImplementationStatus.Planned,
            [
                new("status-text", "رسالة حالة فورية للعمليات", "Immediate status text", "يعرض StatusMessage نتيجة آخر إجراء، وليس سجل إشعارات.", ModuleImplementationStatus.Implemented),
                new("notification-inbox", "مركز إشعارات محفوظ مع انتقال للقسم", "Persistent notification inbox", "لم يبدأ التنفيذ.", ModuleImplementationStatus.Planned)
            ]),
        new("help-center", 33, "النظام والحماية", "مركز المساعدة", "Help Center",
            "تعليمات الاتصال وAgent وأدلة المشروع متاحة جزئيًا.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("connection-help", "تعليمات USB وADB", "USB and ADB connection help", "نافذة المساعدة العامة للاتصال متاحة في قسم مركز المساعدة.", ModuleImplementationStatus.Implemented),
                new("agent-help", "تعليمات PhoneLab Agent", "PhoneLab Agent help", "إرشادات فتح وتفويض تطبيق الهاتف.", ModuleImplementationStatus.Implemented, "AgentHelp"),
                new("offline-help", "قاعدة مساعدة قابلة للبحث دون إنترنت", "Searchable offline help", "لا يوجد محرك بحث موحد في التوثيق بعد.", ModuleImplementationStatus.Planned)
            ]),
        new("support-logs", 34, "النظام والحماية", "الدعم والسجلات", "Support & Logs",
            "جمع سجلات Android متاح؛ سجلات التطبيق وحزمة دعم منقحة البيانات لم تكتمل.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("android-logs", "جمع سجلات Android وتحليل أولي", "Android logs and boot hints", "يحفظ أدلة محدودة مع تنبيه خصوصية؛ تحليل إعادة التشغيل heuristic وليس تشخيصًا مؤكدًا.", ModuleImplementationStatus.Implemented, "AndroidLogs"),
                new("firmware-log", "تقرير فحص Firmware والبصمة", "Firmware inspection report", "يعرض SHA-256 وبنية الأرشيف دون تفليش؛ أداة الفحص والتقرير في قسم Firmware Center.", ModuleImplementationStatus.Implemented),
                new("app-debug-bundle", "حزمة دعم لسجلات التطبيق بعد تنقيح الأسرار", "Redacted application support bundle", "لا توجد حتى الآن حزمة Debug Package أو سجل Crash منظم.", ModuleImplementationStatus.Planned)
            ]),
        new("settings", 35, "النظام والحماية", "الإعدادات", "Settings",
            "إعدادات المركز واللغة والعملة والمنطقة الزمنية موجودة؛ إعدادات التطبيق العامة والثيم والاختصارات لم تكتمل.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("workshop-settings", "إعدادات مركز الصيانة الدولية", "International workshop settings", "الاسم والعنوان والدولة والهاتف والبريد والعملة واللغة والمنطقة الزمنية والضمان؛ يفتح تبويب الإعدادات في الإدارة العالمية.", ModuleImplementationStatus.Implemented, "OpenGlobalWorkshopSettings"),
                new("app-settings", "إعدادات التطبيق والثيم واللغة والاختصارات", "Application preferences", "لا يوجد مدير إعدادات عام شامل بعد.", ModuleImplementationStatus.Planned)
            ]),
        new("about", 36, "النظام والحماية", "حول SoftBox", "About",
            "بيانات الإصدار والتراخيص والمكونات موجودة في المستودع والوثائق؛ لا توجد صفحة About مستقلة داخل البرنامج.",
            ModuleImplementationStatus.Planned,
            [
                new("version-info", "عرض الإصدار والمكونات وتراخيص الطرف الثالث", "Version and third-party notices", "توجد ملفات المشروع وTHIRD-PARTY-NOTICES؛ الصفحة داخل التطبيق لم تُبنَ.", ModuleImplementationStatus.Implemented)
            ])
    ];

    public static IReadOnlySet<string> SupportedActionIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "RefreshDevices", "InspectDevice", "UpdateDeviceCatalog", "OpenPhoneTests",
        "AndroidLogs", "InstallSamsungDriver", "InspectFirmware", "UsbTransport",
        "OpenWorkshop", "MirrorScreen", "AnalyzeScreen", "MeasurementRig",
        "RepairGuides", "OpenApkLab", "OpenNetworkDiagnostics", "OpenAndroidDeviceTools", "OpenAndroidBatchTools", "OpenPhoneBackup", "OpenAndroidWorkflow", "OpenUsbDoctor", "ExportDeviceReport", "ConnectionHelp", "AgentHelp",
        "UpdateAgent", "SetupUsb", "ImportBoardMap", "OpenBoardView",
        "OpenWorkshopBackup", "OpenWorkshopInventory", "OpenGlobalWorkshopHistory",
        "OpenGlobalWorkshopReports", "OpenGlobalWorkshopSettings"
    };

    public static SoftBoxModule? Find(string id)
        => All.FirstOrDefault(module => module.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string StatusLabel(ModuleImplementationStatus status) => status switch
    {
        ModuleImplementationStatus.Planned => "لم يبدأ",
        ModuleImplementationStatus.InDevelopment => "قيد التطوير",
        ModuleImplementationStatus.Implemented => "تم تنفيذ الكود",
        ModuleImplementationStatus.Tested => "تم الاختبار",
        ModuleImplementationStatus.Verified => "تم التحقق",
        ModuleImplementationStatus.Unsupported => "غير مدعوم",
        _ => "غير محدد"
    };
}
