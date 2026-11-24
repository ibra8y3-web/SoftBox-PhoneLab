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
    public const int ExpectedModuleCount = 38;

    public static IReadOnlyList<SoftBoxModule> All { get; } =
    [
        new("dashboard", 1, "الرئيسية", "لوحة التحكم", "Dashboard",
            "ملخص حالة SoftBox والأجهزة المتصلة والعمليات المتاحة حاليًا.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("live-usb", "حالة محرك ADB", "ADB engine status", "يعرض توفر أداة ADB الفعلية وحالة اكتشاف الأجهزة.", ModuleImplementationStatus.Implemented),
                new("connected-list", "قائمة الأجهزة المتصلة", "Connected devices", "قائمة أجهزة ADB تتحدث تلقائيًا؛ لوحة مؤشرات العمليات والإحصائيات الشاملة لم تكتمل بعد.", ModuleImplementationStatus.Implemented),
                new("dashboard-reports", "إحصائيات العمليات والتقارير", "Operations analytics", "نافذة تحليلات فعلية من سجل الأجهزة والإشعارات المحلي: عدد الأجهزة المسجلة، أحداث 24 ساعة و7 و30 يومًا، الإشعارات غير المقروءة، أكثر العمليات والفئات تكرارًا، واتجاه النشاط اليومي لآخر 14 يومًا. لا تدّعي وجود طابور مهام خلفي.", ModuleImplementationStatus.Tested, "OpenDashboardAnalytics")
            ]),
        new("device-center", 2, "مركز الأجهزة", "مركز الأجهزة", "Device Center",
            "اكتشاف الأجهزة المحمولة عبر Windows USB PnP وADB، وفصل هوية USB عن تصريح نظام الهاتف ومسار الشاشة المدعوم.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("adb-discovery", "اكتشاف ADB تلقائيًا", "Automatic ADB discovery", "فحص دوري كل 3 ثوانٍ مع التمييز بين الجهاز المصرح به وغير المصرح.", ModuleImplementationStatus.Implemented, "RefreshDevices"),
                new("universal-usb-discovery", "اكتشاف أجهزة USB متعددة الأنظمة والشركات", "Cross-vendor USB device discovery", "يعرض Windows PnP وADB كقناتين منفصلتين، ويستدل على الشركة من Manufacturer/FriendlyName/VID دون ادعاء تحديد الموديل أو ربط USB بـADB بالتخمين. يعرض مسار النقل المعروف وحدود الشاشة والتحكم.", ModuleImplementationStatus.InDevelopment, "OpenUniversalDeviceDiscovery"),
                new("maintenance-mode-center", "مركز Download وFastboot وEDL وBROM", "Universal service-mode center", "يعرض واجهات Windows PnP حتى عند اختفاء ADB، ويفتح أدوات Samsung/Odin وFastboot وQualcomm وMediaTek بعد فحص الصلاحية وتسجيل التدقيق. التعرف على الواجهة لا يثبت موديل الهاتف أو صلاحية ملفات التفليش.", ModuleImplementationStatus.InDevelopment, "OpenMaintenanceModeCenter"),
                new("multi-device-list", "عرض أكثر من جهاز", "Multi-device list", "يمكن عرض عدة أجهزة واختيار جهاز للفحص؛ التنفيذ الجماعي المتوازي لم يبدأ.", ModuleImplementationStatus.Implemented),
                new("wireless-adb", "اتصال ADB لاسلكي", "Wireless ADB pairing", "ينفذ اقتران Android Wireless debugging برمز الهاتف عبر adb pair ثم الاتصال بمنفذ Wireless debugging عبر adb connect. يتطلب Android 11+ وشبكة Wi-Fi مشتركة؛ رمز الاقتران لا يُحفظ في إعدادات SoftBox أو تقاريره. منطق البروتوكول مغطى باختبارات آلية، ولم يُسجل بعد تحقق على هاتف فعلي.", ModuleImplementationStatus.Tested, "OpenWirelessAdbPairing")
            ]),
        new("device-intelligence", 3, "مركز الأجهزة", "ذكاء الأجهزة", "Device Intelligence",
            "تحليل بيانات Android ومطابقتها مع ملفات تعريف الموديلات المتاحة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("properties", "قراءة خصائص الجهاز وSoC والذاكرة والتخزين والبطارية", "Device properties", "تُقرأ القيم التي يسمح بها النظام عبر ADB؛ القراءة لا تثبت سلامة القطع.", ModuleImplementationStatus.Implemented, "InspectDevice"),
                new("profiles", "مطابقة الموديل وقاعدة ملفات التعريف", "Model profile matching", "كتالوج محلي من SoftBox مع مصدر مجتمعي؛ المطابقة المجتمعية احتمالية وليست إثباتًا للوحة.", ModuleImplementationStatus.Implemented, "UpdateDeviceCatalog"),
                new("board-map", "استيراد خريطة PCB موثقة للموديل", "Verified PCB map import", "يتطلب ملف خريطة موثقًا ومتطابقًا مع الموديل ومصدره؛ الخريطة العامة ليست تخطيطًا دقيقًا.", ModuleImplementationStatus.Implemented, "ImportBoardMap"),
                new("board-view", "فتح ملف BoardView في برنامج خارجي مستقل", "Native BoardView viewer", "يفتح الصيغ المدعومة في TRACE Boardviewer المنفصل؛ لا يحول كل الصيغ إلى المجسم ثلاثي الأبعاد.", ModuleImplementationStatus.Implemented, "OpenBoardView"),
                new("compatibility", "محرك قدرات وتوافق الجهاز والعمليات", "Device capability and compatibility engine", "محرك مركزي يحسم هوية الجهاز بالأدلة ويميز بين دعم ADB وAgent وFastboot وخريطة PCB وملف Firmware؛ يتعامل مع التعارضات والمطابقات الملتبسة كحالات غير مؤكدة، ويمنع التفليش وفتح Bootloader والمسح وعمليات EDL/BROM وعمليات OEM غير الموثقة. مرتبط بمطابقة Device Profile وفحص الاستعادة الآمنة.", ModuleImplementationStatus.InDevelopment, "OpenDeviceCapabilityCenter"),
                new("model-capability-matrix", "مصفوفة تغطية موديلات ولوحات الأجهزة", "Model and board coverage matrix", "تصدير مصفوفة من كتالوج الموديلات الفعلي تعرض أرقام الموديلات وDevice Code وProduct وSoC وBoard ID ومراجع الخرائط والمصادر. لا تعتبر المراجع أو أسماء الموديلات دليلًا على وجود خريطة PCB؛ النواقص تظل ظاهرة.", ModuleImplementationStatus.Tested)
            ]),
        new("android-service", 4, "أدوات Android", "مركز صيانة Android", "Android Service Center",
            "مركز موحد لأدوات ADB وFastboot وAgent وإدارة ملفات التخزين المشترك وقراءة التطبيقات والأوامر الآمنة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("adb-core", "اكتشاف أجهزة ADB وقراءة معلوماتها", "ADB discovery and inspection", "مدمج في محرك الاكتشاف والفحص الحالي؛ تشغيل الفحص العام من قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("fastboot-manager", "اكتشاف أجهزة Fastboot وقراءة متغيرات Bootloader", "Fastboot manager", "يستخدم fastboot.exe الرسمي من Android SDK Platform-Tools لاكتشاف الأجهزة وقراءة getvar all، مع إعادة تشغيل النظام بعد تأكيد المستخدم. يعرض محرك قراءة منظم لحالة القفل وSecure Boot وفتحات A/B ومتغيرات Anti-Rollback الخام. لا توجد عمليات Flash أو Unlock أو Erase.", ModuleImplementationStatus.Tested, "OpenFastbootManager"),
                new("fastboot-recovery-modes", "إعادة التشغيل إلى Recovery وFastbootd", "Fastboot Recovery and Fastbootd modes", "أوامر fastboot reboot recovery وfastboot reboot fastboot على Serial الجهاز المختار بعد تأكيد المستخدم. Fastbootd يعتمد على دعم Bootloader وFirmware للموديل؛ لا يتم Flash أو Erase.", ModuleImplementationStatus.Implemented),
                new("desktop-crash-telemetry", "سجلات JSONL والتقاط أعطال تطبيق Windows", "Windows desktop structured logs and crash capture", "يراقب AppDomain وWPF Dispatcher والمهام غير المرصودة، ويحفظ مصدر الخطأ ونوع الاستثناء والرسالة والمكدس في ملف JSONL محلي محدود الحجم تحت LocalAppData. لا يرسل السجلات تلقائيًا ولا يخفي فشل التطبيق.", ModuleImplementationStatus.InDevelopment),
                new("agent-extended-hardware", "اختبارات Bluetooth وWi-Fi وNFC وGPS والبصمة والأزرار وSIM", "Extended Android Agent hardware checks", "يقرأ حالات Bluetooth وWi-Fi وSIM، ويختبر قراءة NFC الفعلية وإصلاح GPS الفعلي ومصادقة البصمة عبر Android BiometricPrompt، ويختبر زري الصوت فقط لأن Android يحمي Power/Home. يحتاج أذونات المستخدم، وقد يتطلب ملحق Bluetooth أو بطاقة NFC أو مكانًا مفتوحًا أو بصمة مسجلة.", ModuleImplementationStatus.InDevelopment),
                new("agent-battery-telemetry", "رصد البطارية كل 30 دقيقة وسجلات JSONL والتقاط الأعطال", "Scheduled battery telemetry and crash capture", "يستخدم Android WorkManager لأخذ عينات البطارية وحفظها محليًا، ويسجل الأحداث والأعطال في ملفات JSONL محدودة الحجم؛ يرسل ملخصًا إلى Windows ويصدر التقرير الكامل فقط بطلب الفني عبر ADB محلي.", ModuleImplementationStatus.InDevelopment),
                new("agent-bridge", "تثبيت PhoneLab Agent وجلب نتائج الاختبارات", "PhoneLab Agent bridge", "اتصال محلي عبر ADB forward مع موافقة المستخدم على الهاتف؛ يجلب اختبارات العتاد وقراءات البطارية الدورية وملخص السجلات إلى معلومات الجهاز، ويتيح تصدير تقرير JSON للأحداث والأعطال بطلب الفني عبر ADB محلي.", ModuleImplementationStatus.InDevelopment),
                new("agent-update", "تحديث PhoneLab Agent", "PhoneLab Agent update", "يفحص ويحدّث مكوّن Agent ضمن دعم التطبيق الحالي.", ModuleImplementationStatus.Implemented, "UpdateAgent"),
                new("logs", "قراءة سجلات Android المسموح بها", "Android logs", "يجمع حزمة أدلة محدودة؛ السجلات قد تحتوي بيانات شخصية ويجب مراجعتها قبل المشاركة. أداة جمعها في قسم الدعم والسجلات.", ModuleImplementationStatus.Implemented),
                new("logcat-analysis", "تحليل سجلات Android وتصنيف الأعطال", "Android Logcat failure analysis", "يجمع آخر 3000 سطر بقراءة ADB فقط، ويصنف Crash وANR والذاكرة والصلاحيات وSELinux والحرارة والتخزين وUSB والشبكة. يحفظ ملخصات مجمعة دون السطور الخام؛ النتائج مؤشرات triage وليست تشخيصًا ماديًا مؤكدًا.", ModuleImplementationStatus.Implemented, "OpenAndroidLogAnalyzer"),
                new("app-manager", "إدارة التطبيقات وتشغيلها وإيقافها وتثبيتها وإزالتها", "Installed app lifecycle and APK management", "يعرض التطبيقات الخارجية وبيانات الحزم، ويثبت APK وSplit APK، ويشغل التطبيق عبر Monkey، ويوقفه عبر am force-stop، ويمسح بيانات التطبيق عبر pm clear بعد تحذير وتأكيد صريح، ويزيله بعد إعادة التحقق من قائمة الطرف الثالث؛ لا يتعامل مع تطبيقات النظام.", ModuleImplementationStatus.Tested, "OpenAndroidAppManager"),
                new("runtime-permissions", "منح وسحب صلاحيات Runtime للتطبيق", "Runtime permission grant and revoke", "يقرأ requested permissions وruntime permissions من dumpsys package، ويتيح pm grant وpm revoke فقط للصلاحيات التي طلبها التطبيق وظهرت كصلاحيات Runtime، ثم يعيد قراءة PackageManager ويتحقق من الحالة الجديدة. الصلاحيات التثبيتية وPrivileged غير متاحة.", ModuleImplementationStatus.Tested),
                new("device-service-operations", "إعادة التشغيل إلى System وRecovery وBootloader", "Device reboot and service-mode operations", "ينفذ adb reboot وadb reboot recovery وadb reboot bootloader على Serial المحدد بعد تأكيد المستخدم والتحقق من حالة ADB device. هذه العمليات لا تنفذ Wipe أو Unlock أو Flash.", ModuleImplementationStatus.Tested, "OpenAndroidServiceOperations"),
                new("split-apk-install", "تثبيت مجموعات Split APK", "Validated split APK set installation", "داخل مدير التطبيقات: يفحص ملف Base وملفات Split محليًا، ويتحقق من تطابق اسم الحزمة وVersion Code ووجود Base واحد وSplit Names فريدة، ثم يعيد التحقق من SHA-256 لكل ملف قبل ADB install-multiple. Android يتحقق من توافق توقيع التطبيق أثناء التثبيت؛ لا يدّعي SoftBox تحققًا تشفيريًا مستقلًا من شهادة المطور.", ModuleImplementationStatus.Tested),
                new("file-manager", "مدير ملفات Android عبر ADB", "Android shared-storage file manager", "استعراض ورفع وتنزيل ملفات وإنشاء مجلدات داخل التخزين المشترك المسموح به فقط؛ لا يصل إلى /data أو بيانات التطبيقات الخاصة.", ModuleImplementationStatus.Implemented, "OpenAndroidFileManager"),
                new("command-center", "مركز أوامر Android بقائمة قراءة فقط", "Allow-listed Android command center", "أوامر getprop والبطارية والتخزين وuptime والتطبيقات والخدمات من قائمة ثابتة؛ لا يقبل أوامر Shell حرة.", ModuleImplementationStatus.Implemented, "OpenAndroidCommandCenter")
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
                new("apk-signature-verification", "التحقق التشفيري من توقيع APK وشهادات الموقّعين", "Cryptographic APK signature verification", "ينفذ Android SDK Build-Tools apksigner verify --verbose --print-certs على APK محلي؛ يعرض مخططات التوقيع التي اجتازت التحقق وDN وبصمات SHA-256/SHA-1 ومعلومات المفتاح، ويرفض نتيجة الملف إذا تغير أثناء التحقق. نجاح التوقيع لا يثبت أن الشهادة تخص الناشر الرسمي؛ قارن بصمتها بقناة مستقلة موثوقة.", ModuleImplementationStatus.Tested),
                new("apk-version-diff", "مقارنة إصدارين وتحليل فروق الصلاحيات والملفات", "APK version and permission comparison", "يقارن اسم الحزمة وبيانات الإصدار وSHA-256 والصلاحيات وأسماء مدخلات الأرشيف بين ملفين محليين؛ لا يقارن محتوى كل ملف ولا يثبت صحة التوقيع.", ModuleImplementationStatus.Tested)
            ]),
        new("samsung-tools", 6, "الشركات والمعالجات", "أدوات Samsung", "Samsung Tools",
            "قراءة خصائص Samsung والدخول إلى Download Mode عبر ADB بعد إعادة فحص الهوية، مع فحص حقيقي لبنية حزم TAR/TAR.MD5 وبصمة MD5 الملحقة وملفات الأقسام وSHA-256 قبل فتح نسخة Odin الخارجية. لا يتم تضمين Odin أو أتمتة زر Start.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("samsung-driver", "مثبت تعريف Samsung USB الرسمي الاختياري", "Samsung USB driver installer", "مضمن في حزمة Windows مع تحقق مثبت التعريف؛ مكان تشغيله الأساسي قسم طبيب USB.", ModuleImplementationStatus.Implemented),
                new("samsung-adb", "قراءة خصائص One UI وCSC وPDA وBootloader", "Samsung vendor property diagnostics", "يقرأ مجموعة getprop ثابتة على الجهاز المصرح به ويعرض الخصائص المتاحة والفارغة مع حفظ تقرير JSON.", ModuleImplementationStatus.Implemented, "OpenSamsungDiagnostics"),
                new("samsung-download-mode", "الدخول إلى Samsung Download Mode", "Samsung Download Mode entry", "يفتح مركز عمليات Samsung على الجهاز المختار، ويتحقق من هوية Samsung مجددًا قبل إرسال adb reboot download. لا يبدأ التفليش ولا يضمن توفر الأمر على كل إصدار أو موديل.", ModuleImplementationStatus.Implemented, "OpenSamsungServiceOperations"),
                new("samsung-odin-assistant", "مساعد Odin وتجهيز حزم Firmware", "Samsung Odin handoff assistant", "يفحص وجود حزم BL/AP/CP/CSC أو HOME_CSC، يقرأ عناصر TAR دون استخراج، ويتحقق من MD5 الملحقة في TAR.MD5 وSHA-256 وملفات الأقسام المعروفة، ثم يفتح ملف Odin التنفيذي الذي يحدده المستخدم. التحقق من MD5 يثبت سلامة النقل فقط؛ لا يثبت ناشر الملف أو توافق الموديل/CSC/Anti-Rollback. لا يبدأ التفليش.", ModuleImplementationStatus.Tested, "OpenSamsungOdinAssistant"),
                new("samsung-archive-inspection", "فحص بنية حزمة Odin والتحقق من TAR.MD5", "Read-only Odin TAR and appended MD5 inspection", "اختبارات حقيقية لحزم TAR/TAR.MD5، تطابق MD5، رفض البصمة المعدلة ومسارات TAR غير الآمنة؛ لا يتم استخراج الملفات أو تفليش الهاتف.", ModuleImplementationStatus.Tested),
                new("samsung-official-resources", "روابط أدوات ودعم Samsung الرسمية", "Samsung official tool directory", "يفتح روابط Samsung Developer والدعم الرسمي ويشرح حدود Odin؛ لا ينسب Odin غير المنشور رسميًا إلى Samsung ولا يحقن أوامر داخل واجهته.", ModuleImplementationStatus.Tested, "OpenSamsungOfficialTools"),
                new("samsung-service", "أتمتة عمليات Odin وDownload Mode المتقدمة", "Samsung Odin advanced service automation", "يتحقق من مسار Odin والنافذة وحقول الملفات ومنفذ COM وزر Start القابل للاستدعاء، ويطلب تأكيد التوافق والنسخة الاحتياطية قبل Invoke واحد. بعد البدء يقرأ PASS/FAIL ونسبة التقدم من Windows UI Automation دون إرسال أوامر إضافية، ويعرض Unknown عند غياب دليل صريح. لا ينفذ بروتوكول Download Mode خاصًا أو ضغطات إحداثية أو تجاوزًا لمصادقة OEM؛ يلزم اختبار Hardware-in-the-Loop على موديلات ونسخ Odin معتمدة.", ModuleImplementationStatus.InDevelopment)
            ]),
        new("xiaomi-tools", 7, "الشركات والمعالجات", "أدوات Xiaomi وRedmi وPOCO", "Xiaomi Tools",
            "يجمع خصائص Xiaomi/Redmi/POCO وMIUI/HyperOS والمنطقة ونسخة البناء وVerified Boot من مفاتيح getprop ثابتة على الهاتف المصرح به. عمليات Xiaomi Fastboot الخاصة بالموديل ما زالت غير مفعّلة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("xiaomi-identity", "قراءة موديل الجهاز واسم البناء والمنطقة ونسخة MIUI/HyperOS", "Xiaomi vendor property diagnostics", "يجمع getprop من قائمة مفاتيح ثابتة ويعرض القيم التي يوفرها ROM فقط؛ لا يخمن المنطقة أو موديل اللوحة.", ModuleImplementationStatus.Implemented, "OpenXiaomiDiagnostics"),
                new("xiaomi-fastboot", "تشخيص Xiaomi Fastboot وقراءة متغيرات Bootloader", "Xiaomi Fastboot read-only diagnostics", "من نافذة Xiaomi Diagnostics: يعيد التحقق من هوية الهاتف عبر ADB، يطلب تأكيد الانتقال إلى Bootloader، ينتظر ظهور نفس Serial في Fastboot ثم يقرأ getvar all ويعرض Product والقفل وVerified Boot وAnti-Rollback. لا ينفذ Unlock أو Flash أو Erase.", ModuleImplementationStatus.Tested),
                new("xiaomi-fastboot-rom-inspector", "فحص حزم Xiaomi Fastboot ROM وProduct والسكربتات", "Xiaomi Fastboot ROM archive inspector", "يفحص TAR/TGZ محليًا دون استخراج أو تشغيل؛ يقرأ android-info.txt وصور partitions وأوامر flash/erase/unlock الموجودة في سكربتات الحزمة، ويقارن Product مع آخر Product مقروء من Fastboot. لا يفليش ولا يفتح Bootloader.", ModuleImplementationStatus.Tested),
                new("xiaomi-fastboot-service", "تفليش صورة IMG واحدة إلى قسم Fastboot مسموح", "Restricted Xiaomi Fastboot single-image flash", "متاح فقط عند مطابقة Xiaomi/Redmi/POCO وهوية Serial وProduct بدقة، وقراءة حالة Bootloader Unlocked، وصورة IMG لقائمة الأقسام المحدودة، مع تأكيد مصدر OEM والنسخة الاحتياطية وإعادة قراءة الحالة قبل الكتابة. لا ينفذ Unlock أو Erase أو Relock أو تفليش أقسام أخرى.", ModuleImplementationStatus.InDevelopment, "OpenXiaomiFastbootService"),
                new("xiaomi-mi-unlock-official", "فتح أداة Xiaomi Mi Unlock الرسمية", "Official Xiaomi Mi Unlock hand-off", "يفتح صفحة أداة Mi Unlock الرسمية. تنفيذ الفتح وأهلية الحساب وربط الهاتف ومهلة الانتظار تظل لدى Xiaomi ولا يتجاوزها SoftBox.", ModuleImplementationStatus.Tested, "OpenXiaomiOfficialTools")
            ]),
        new("android-brands", 8, "الشركات والمعالجات", "شركات Android الأخرى", "Android Brands",
            "ملفات خصائص قراءة فقط مخصصة لـGoogle Pixel وMotorola/Lenovo وOnePlus وOPPO وvivo وrealme وHuawei/HONOR وTECNO وInfinix، مع ملف عام للشركات الأخرى؛ لا تنفذ بروتوكولات صيانة خاصة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("brand-recognition", "قراءة الشركة والموديل من خصائص Android", "Generic brand recognition", "تُقرأ بيانات الشركة والموديل المتاحة من الجهاز عبر محرك ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("brand-catalog", "مطابقة ملفات تعريف المجتمع", "Community device identities", "مصدر مجتمعي يساعد في التعرف ولا يثبت إصدار اللوحة أو التوافق؛ تحديث الكتالوج موجود في قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("brand-generic-properties", "قراءة خصائص الشركات وملفات التعريف الثابتة", "Read-only vendor property profiles", "يختار مجموعة مفاتيح getprop ثابتة حسب الشركة: Pixel وMotorola وOnePlus وOPPO وvivo وrealme وHuawei/HONOR وTECNO وInfinix، ويعرض القيم التي وفرها النظام فقط. لا ينفذ عمليات خدمة أو تفليش خاصة بالشركة.", ModuleImplementationStatus.Tested, "OpenBrandDiagnostics"),
                new("brand-official-tools", "دليل روابط أدوات الاستعادة الرسمية للشركات", "Official brand recovery tool directory", "يوفر روابط إلى Android Flash Tool وFactory Images من Google وأداة Motorola Software Fix وروابط Qualcomm/MediaTek الرسمية المنشورة. يوضح أهلية الموديل؛ لا يعمم أداة على جهاز غير مدعوم.", ModuleImplementationStatus.Tested, "OpenBrandOfficialTools"),
                new("brand-specific", "بروتوكولات صيانة خاصة بكل شركة", "Brand-specific service protocols", "تتطلب أدوات OEM وتفويضًا وDA/Programmer لكل موديل. لا يتم توليد أوامر غير موثقة أو استخدام أدوات شركة على موديل غير معتمد.", ModuleImplementationStatus.Planned)
            ]),
        new("qualcomm-tools", 9, "الشركات والمعالجات", "أدوات Qualcomm", "Qualcomm Tools",
            "قراءة تلميح SoC ورصد Qualcomm 05C6:9008 من Windows PnP مع تقرير جاهزية النقل وحالة تعريف Windows والمتطلبات الناقصة؛ لا يتم فتح بروتوكول EDL أو تفليش.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("qualcomm-soc", "قراءة تلميح المعالج من النظام", "SoC identification hint", "قراءة تعريفية فقط من بيانات Android، ولا تكفي لتحديد منصة EDL أو اختيار programmer؛ محرك الهوية في قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("qualcomm-edl", "رصد مؤشر USB Qualcomm EDL / 9008", "Qualcomm EDL USB mode hint", "يرصد VID/PID الشائع 05C6:9008 في Windows PnP ويفتح عرض Qualcomm المفلتر؛ لا يفتح قناة EDL ولا يتحقق من programmer أو توافق الجهاز.", ModuleImplementationStatus.Implemented, "OpenQualcommUsbDiagnostics"),
                new("qualcomm-transport-readiness", "تقرير جاهزية تعريف واتصال Qualcomm EDL", "Qualcomm EDL transport readiness report", "يصنف ملاحظة PnP ورمز مشكلة التعريف ويعرض ما ينقص من هوية الموديل وProgrammer موقع وتفويض OEM؛ يمنع الفلاش دائمًا حتى تتوفر متطلبات رسمية.", ModuleImplementationStatus.Tested),
                new("qualcomm-qdl-package-preflight", "فحص حزمة Qualcomm Firehose وrawprogram/patch", "QDL Firehose package inspector", "يفحص prog_firehose ELF/MBN وملفات XML باستخدام محلل آمن يمنع DTD، ويتحقق من ملفات الصور والمسارات والأحجام ويحسب SHA-256؛ لا يثبت توقيع OEM أو توافق الشريحة وحده.", ModuleImplementationStatus.Tested),
                new("qualcomm-qdl-dryrun-flash", "تشغيل Qualcomm QDL chipinfo وDry-Run والاستعادة", "Qualcomm QDL recovery workflow", "يشغل qdl.exe الذي يقدمه الفني بقائمة وسائط ثابتة؛ يتطلب PnP 05C6:9008 وChip Info متطابقًا وحزمة Firehose/XML مفحوصة وDry-Run ناجحًا وتأكيد OEM ونسخة احتياطية وتأكيد Serial. لا يتجاوز DAA أو Secure Boot أو مصادقة OEM؛ يلزم تحقق جهاز مصرح به قبل ترقية الحالة.", ModuleImplementationStatus.InDevelopment, "OpenQualcommRecovery")
            ]),
        new("mediatek-tools", 10, "الشركات والمعالجات", "أدوات MediaTek", "MediaTek Tools",
            "قراءة تلميح SoC ورصد MediaTek 0E8D:0003 من Windows PnP مع تقرير جاهزية النقل وحالة التعريف والمتطلبات الناقصة؛ لا يتم فتح BROM أو تحميل DA.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("mediatek-soc", "قراءة تلميح المعالج من Android", "SoC identification hint", "قد يظهر تعريف المعالج ضمن خصائص الجهاز؛ لا يثبت وضع Preloader. الفحص العام من قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("mediatek-preloader", "رصد مؤشر USB MediaTek Preloader/BROM", "MediaTek Preloader/BROM USB hint", "يرصد VID/PID الشائع 0E8D:0003 في Windows PnP ويفتح عرض MediaTek المفلتر؛ لا يقرأ بروتوكول BROM ولا يختار DA ولا ينفذ تفليشًا.", ModuleImplementationStatus.Implemented, "OpenMediaTekUsbDiagnostics"),
                new("mediatek-transport-readiness", "تقرير جاهزية تعريف واتصال MediaTek Preloader/BROM", "MediaTek transport readiness report", "يصنف ملاحظة PnP ورمز مشكلة التعريف ويعرض ما ينقص من هوية الموديل وDownload Agent موقع وتفويض OEM؛ لا يفتح BROM ولا ينفذ تفليشًا.", ModuleImplementationStatus.Tested),
                new("mediatek-scatter-preflight", "تحليل Scatter والتحقق من صور الأقسام وSHA-256", "MediaTek scatter package inspector", "يقرأ platform/project وكل partition وملفات الصور المشار إليها، ويرفض المسارات غير الآمنة ويحسب SHA-256؛ لا يتحقق تشفيريًا من DA/Auth ولا يفتح BROM.", ModuleImplementationStatus.Tested),
                new("mediatek-oem-tool-handoff", "فحص DA/Auth وتسليم إلى أداة MediaTek الرسمية", "MediaTek OEM Download Agent workflow", "يفحص Scatter والصور وDA/Auth وأحجامها وبصماتها ويعيد التحقق منها قبل فتح SP Flash Tool الذي يقدمه الفني. لا يتضمن DA عام أو تجاوز DAA/SLA أو أوامر BROM آلية؛ يلزم DA رسمي وموديل/لوحة محددان.", ModuleImplementationStatus.InDevelopment, "OpenMediaTekRecovery")
            ]),
        new("firmware-center", 11, "Firmware والاستعادة", "مركز Firmware", "Firmware Center",
            "مكتبة Firmware محلية وتنزيل HTTPS قابل للاستكمال، وفحص بنية الأرشيف وبوابة توافق fail-closed تقارن الشركة والموديل والمنتج واللوحة والمنطقة وAnti-Rollback وسلامة الملف وثقة الناشر.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("firmware-inspection", "فحص أرشيف محلي وحساب SHA-256", "Local archive inspection and SHA-256", "يقرأ بنية ZIP وعناصر OTA الشائعة فقط؛ لا يثبت المصدر أو التوافق أو سلامة التوقيع.", ModuleImplementationStatus.Implemented, "InspectFirmware"),
                new("firmware-library", "استيراد وتنظيم مكتبة Firmware محلية", "Offline firmware library", "ينسخ ملفًا يختاره المستخدم إلى مكتبة منظمة حسب الشركة والموديل والمنطقة والإصدار ويحفظ بياناته محليًا.", ModuleImplementationStatus.Implemented, "OpenFirmwareCenter"),
                new("firmware-hash-verify", "إعادة حساب SHA-256 والتحقق من سلامة الملف المحلي", "Firmware hash revalidation", "يقارن SHA-256 والحجم الحاليين بسجل الاستيراد ويكشف التغيير أو الفقد؛ هذا لا يثبت مصدر الناشر أو التوافق.", ModuleImplementationStatus.Implemented),
                new("firmware-download", "تنزيل واستكمال ملفات Firmware من مصادر الشركة", "Resumable firmware download manager", "مدير تنزيل داخل Firmware Center يقبل رابط HTTPS مباشرًا يحدده المستخدم وبصمة SHA-256 منشورة من المصدر؛ يدعم HTTP Range resume بعد الانقطاع، وETag/If-Range، والإلغاء والتقدم، ويرفض التحويلات ومصادر HTTP وIP المباشر وبصمة غير مطابقة، ثم يستورد الملف المتحقق محليًا. لا يثبت الناشر أو توافق الجهاز ولا ينفذ تفليشًا.", ModuleImplementationStatus.Tested),
                new("firmware-compatibility", "محرك توافق Firmware قبل الاستخدام", "Fail-closed firmware compatibility gate", "يقارن الشركة والموديل وProduct وBoard/Hardware SKU والمنطقة وAnti-Rollback وسلامة الملف وتوقيع الناشر؛ القيم الناقصة Unknown وتمنع السماح. هذه بوابة معلوماتية ولا تبدأ Flash أو تمنح تفويضًا.", ModuleImplementationStatus.Tested)
            ]),
        new("usb-doctor", 12, "الاتصال والتشخيص", "طبيب USB", "USB Doctor",
            "يجمع فحص ADB/Fastboot مع جرد Windows PnP لأجهزة USB وVID/PID ورموز مشكلات الجهاز. إدارة حزم تعريف Windows متاحة في أداة منفصلة وبفحوص توقيع وتوافق وصلاحيات؛ لا يوجد تثبيت تلقائي لتعريف مجهول.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("usb-transports", "فحص أوضاع ADB وFastboot المتاحة", "ADB and Fastboot transport inspection", "استعلامات قراءة فقط؛ لا تكتشف كل أوضاع الشركات مثل EDL وDownload وPreloader.", ModuleImplementationStatus.Implemented, "UsbTransport"),
                new("usb-pnp-inventory", "جرد أجهزة Windows USB وقراءة VID/PID والحالة", "Windows USB PnP inventory", "يستخدم SetupAPI وConfiguration Manager لقراءة الأجهزة الحالية ورموز المشكلة والخدمة المرتبطة دون تعديل الجهاز.", ModuleImplementationStatus.Implemented, "OpenUsbDoctor"),
                new("usb-mode-hints", "تمييز مؤشرات Qualcomm وMediaTek وSamsung وGoogle", "Known USB mode hints", "كتالوج محدود لمعرفات VID/PID الشائعة؛ لا يثبت الموديل أو التوافق ولا ينفذ بروتوكول الخدمة.", ModuleImplementationStatus.Implemented),
                new("usb-setup", "إرشادات وتجهيز اتصال USB", "USB connection setup", "يفتح تعليمات إعداد أدوات USB وADB ولا يثبت تعريفات مجهولة.", ModuleImplementationStatus.Implemented, "SetupUsb"),
                new("connection-help", "مساعدة توصيل الهاتف وحل اتصال ADB", "USB connection help", "تعليمات الاتصال الحالية في البرنامج.", ModuleImplementationStatus.Implemented),
                new("oem-driver-center", "مركز 33 موردًا رسميًا لتعريفات وأدوات الشركات", "33 official OEM driver, tool and support resources", "يعرض 33 مصدر HTTPS رسميًا من الشركات وموردي الأنظمة، ويصنف كل مصدر إلى تعريف USB أو MTP إقليمي أو أداة Windows أو بوابة دعم. يفحص وصول صفحة المصدر لحظيًا عبر HTTPS ويعرض رمز HTTP والتحويلات دون تنزيل الحزمة. كما يطابق Hardware IDs التي قرأها Windows مع أقسام Models في INF لتحديد تطابق مرشح للمعمارية وBuild؛ أي زخرفة ProductType/SuiteMask غير محسومة تبقى Unknown. لا يثبت الوصول وجود ملف تعريف مباشر أو أن Windows سيختار هذا القسم؛ يلزم تحقق CAT والتوقيع واختيار SetupAPI النهائي قبل التثبيت.", ModuleImplementationStatus.InDevelopment, "OpenUsbDriverCenter"),
                new("samsung-driver", "تثبيت تعريف Samsung الرسمي الاختياري", "Samsung official driver", "مضمن في حزمة Windows؛ لا يعني دعم Download Mode أو عمليات صيانة خاصة.", ModuleImplementationStatus.Implemented, "InstallSamsungDriver"),
                new("windows-driver-health", "فحص حالة PnP ومشكلات تعريف USB", "Windows PnP device health", "يعرض حالة الجهاز ورمز المشكلة المبلغ عنه من Windows؛ لا يتحقق من توقيع التعريف أو يغيره.", ModuleImplementationStatus.Implemented),
                new("usb-driver-signature", "التحقق من توقيع Windows Authenticode وعضوية CAT لتعريف USB", "Windows USB driver Authenticode and catalog membership verification", "يستخدم WinVerifyTrust لتوقيع الملف المضمّن أو الكتالوج، ويستخدم Microsoft SignTool من Windows SDK بالأمر verify /v /pa /c للتحقق أيضًا من تطابق ملف SYS/INF/DLL مع أحد أعضاء CAT. لا يعتمد العضوية إذا غاب SignTool أو أخفق التحقق؛ يعرض الناشر والبصمة عند توفرهما ولا يثبت تعريفًا أو يغير Driver Store.", ModuleImplementationStatus.InDevelopment, "OpenUsbDriverSignatureVerifier"),
                new("driver-package-manager", "إدارة حزم تعريفات Windows", "Windows driver package management", "إدارة محلية لحزم INF/CAT عبر PnPUtil بعد مطابقة Hardware IDs وتوقيع الكتالوج وعضوية INF؛ يشمل الجرد والتصدير والتثبيت والإزالة الصريحة دون /force.", ModuleImplementationStatus.InDevelopment, "OpenDriverPackageManager"),
                new("driver-package-inspection", "فحص حزمة التعريف قبل التثبيت", "Driver package compatibility and trust validation", "يفحص تطابق INF مع Hardware IDs وإصدار Windows والمعمارية، ثم توقيع CAT وعضوية INF قبل السماح بالتثبيت.", ModuleImplementationStatus.InDevelopment, "InspectUsbDriverPackage"),
                new("driver-package-inventory", "جرد حزم التعريفات المثبتة", "Installed driver package inventory", "يعرض نتائج pnputil /enum-drivers من Windows كما هي دون افتراض صيغة لغة النظام.", ModuleImplementationStatus.InDevelopment, "ListUsbDriverPackages"),
                new("driver-package-install", "تثبيت أو استعادة حزمة تعريف معتمدة", "Install or restore a verified driver package", "يستخدم pnputil /add-driver /install بعد فحوص التوافق والتوقيع، ويتطلب UAC ولا يدعي سلامة الجهاز قبل إعادة فحصه.", ModuleImplementationStatus.InDevelopment, "InstallUsbDriverPackage"),
                new("driver-package-export", "تصدير نسخة احتياطية من حزمة تعريف", "Export driver package backup", "يصدر حزمة منشورة بالاسم oemNN.inf إلى مجلد يحدده الفني عبر PnPUtil.", ModuleImplementationStatus.InDevelopment, "ExportUsbDriverPackage"),
                new("driver-package-remove", "إزالة حزمة تعريف منشورة", "Remove a published driver package", "يتطلب اسمًا مطابقًا للنمط oemNN.inf وتأكيدًا وUAC؛ لا يستخدم /force وقد يرفض Windows الإزالة إذا كانت الحزمة مطلوبة.", ModuleImplementationStatus.InDevelopment, "RemoveUsbDriverPackage")
            ]),
        new("smart-backup", 13, "الاتصال والتشخيص", "النسخ الاحتياطي الذكي", "Smart Backup",
            "نسخ مجلدات التخزين المشترك عبر ADB إلى ZIP مع SHA-256 لكل ملف، وخيار SBX مشفر ومصادق عليه، والتحقق والاستعادة إلى مجلد جديد على الهاتف دون الكتابة فوق ملفات موجودة؛ يدعم الآن جدولة مشفرة عند التوصيل أو يوميًا أو أسبوعيًا أثناء جلسة SoftBox المفتوحة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("workshop-backup", "نسخ واستعادة قاعدة بيانات مركز الصيانة", "Workshop database backup", "نسخ JSON محلي واستعادته؛ النسخ الحالي غير مشفر. فتح تبويب النسخ يتم من هذا القسم.", ModuleImplementationStatus.Implemented, "OpenWorkshopBackup"),
                new("phone-backup", "نسخ مجلدات التخزين المشترك من Android", "Android shared-storage backup", "ينقل المجلد المحدد عبر ADB إلى ZIP محلي ويحسب SHA-256 لكل ملف؛ لا ينسخ بيانات التطبيقات الخاصة أو النظام.", ModuleImplementationStatus.Implemented, "OpenPhoneBackup"),
                new("backup-verify", "التحقق من النسخة ومطابقة بصمات الملفات", "Backup archive verification", "يفحص Manifest ويعيد حساب SHA-256 ومقارنة حجم كل ملف داخل ZIP؛ مغطى باختبارات آلية.", ModuleImplementationStatus.Tested),
                new("backup-encryption", "تشفير النسخ بكلمة مرور ومصادقة SBX", "Authenticated password-encrypted backups", "ينشئ حاوية SBX بتشفير AES-256-CBC وHMAC-SHA256 ومفتاح PBKDF2-HMAC-SHA256 (600,000 دورة)، ويتحقق من المصادقة قبل فك مؤقت وفحص Manifest؛ ZIP العادي يظل غير مشفر.", ModuleImplementationStatus.Implemented),
                new("backup-restore", "استعادة الملفات بعد التحقق إلى مجلد جديد على الهاتف", "Verified restore to a new Android folder", "يتحقق من Manifest وبصمات SHA-256 أو مصادقة SBX أولًا، ثم يرسل الملفات إلى مجلد فريد في التخزين المشترك دون حذف أو استبدال الملفات الموجودة.", ModuleImplementationStatus.Implemented),
                new("backup-scheduler", "جدولة النسخ المشفر عند التوصيل أو يوميًا أو أسبوعيًا", "Scheduled automatic encrypted backups", "يحفظ الجداول وكلمات المرور في DPAPI، ويشغّل عامل Windows Task Scheduler كل 5 دقائق حتى بعد إغلاق واجهة SoftBox، بشرط بقاء حساب Windows الحالي مسجلًا واتصال الهاتف وتصريح ADB. يحجز الجداول بين العمليات لمنع التكرار وينفذ ملفات SBX مشفرة. لا يعمل عند تسجيل الخروج أو إيقاف الكمبيوتر، ولا ينسخ بيانات التطبيقات الخاصة أو النظام.", ModuleImplementationStatus.InDevelopment, "OpenAndroidBackupScheduler")
            ]),
        new("phone-link", 14, "الاتصال والتشخيص", "استوديو الهاتف", "Phone Link Studio",
            "منظومة عرض متعددة المسارات: Android عبر ADB/scrcpy عند التصريح، وiPhone عبر AirPlay مستقل للعرض فقط، ومسار Miracast الأصلي في Windows عند توافق الطرفين. التحكم منفصل عن العرض، وبروتوكولات الشركات الأخرى مرتبطة بدعم النظام والموديل.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("screen-mirror", "عرض والتحكم في شاشة هاتف مصرح به", "Authorized screen mirroring and control", "يستخدم scrcpy الرسمي؛ يحتاج اتصال ADB مصرحًا به.", ModuleImplementationStatus.Implemented, "MirrorScreen"),
                new("screen-record", "تسجيل شاشة الهاتف الفعلية وحفظ MP4/MKV", "Android screen recording", "يشغّل scrcpy على الجهاز المحدد مع خيار --record إلى مسار يختاره المستخدم؛ عند الإيقاف يتم إرسال WM_CLOSE لإنهاء الحاوية والتحقق من وجود ملف غير فارغ.", ModuleImplementationStatus.Implemented, "OpenScreenRecording"),
                new("multi-session", "إدارة عدة جلسات عرض مستقلة", "Multi-device screen sessions", "نافذة عرض متعددة حتى أربع جلسات scrcpy مستقلة؛ لكل جهاز عملية ونافذة مدمجة وإيقاف منفصل، مع مراقبة ADB والعملية والنافذة كل 5 ثوانٍ. أُضيف فحص get-state جديد مباشرة قبل إطلاق scrcpy بعد تجهيز الأداة؛ اختبار الهواتف المتعددة فعليًا ما زال مطلوبًا.", ModuleImplementationStatus.InDevelopment, "OpenMultiScreenSessions"),
                new("session-live-adb-guard", "إعادة التحقق من تصريح ADB عند بدء كل جلسة", "Fresh ADB authorization before each screen session", "يعيد فحص get-state للـSerial المحدد بعد تنزيل/تجهيز scrcpy مباشرة قبل التشغيل؛ يمنع بدء جلسة عند unauthorized أو offline أو فقد الجهاز.", ModuleImplementationStatus.Tested),
                new("ios-airplay-view", "عرض شاشة iPhone عبر AirPlay على Windows", "iPhone AirPlay screen viewing on Windows", "يشغّل مستقبل AirPlay-Windows مستقلًا اختاره المستخدم من ملفه الرسمي؛ يحتاج شبكة محلية مشتركة وتأكيد المستخدم من iPhone. مسار عرض فقط ولا يوفر تحكمًا باللمس أو تجاوز رمز القفل. لا يضمّن SoftBox برنامج GPL أو يعيد توزيعه.", ModuleImplementationStatus.InDevelopment, "OpenAirPlayReceiver"),
                new("windows-miracast-fallback", "إعداد مستقبل Windows Wireless Display لأجهزة Miracast", "Windows Wireless Display / Miracast fallback", "يفتح إعدادات Windows الرسمية لتثبيت ميزة Wireless Display وتهيئة Projecting to this PC. يعمل فقط إذا كان الكمبيوتر والهاتف يدعمان Miracast؛ الاستقبال عبر تطبيق Windows الأصلي خارج لوحة SoftBox ولا يوفر تحكمًا باللمس من SoftBox.", ModuleImplementationStatus.Implemented, "OpenMiracastSetup")
            ]),
        new("hardware-diagnostics", 15, "الاتصال والتشخيص", "تشخيص العتاد", "Hardware Diagnostics",
            "توجد اختبارات Agent وCamera2 وقراءات النظام والقياس الخارجي؛ بعض الفحوص يدوية أو تتطلب معدات.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("agent-tests", "اختبارات الشاشة واللمس والصوت والاهتزاز والحساسات", "Agent hardware screening tests", "اختبارات تتطلب موافقة المستخدم على الهاتف؛ النتائج لا تعادل قياسات معملية معايرة.", ModuleImplementationStatus.Implemented, "OpenPhoneTests"),
                new("diagnostic-evidence-history", "سجل أدلة نتائج المكونات المشفر محليًا", "Encrypted local component diagnostic evidence history", "يحفظ نتائج DiagnosticEngine الفعلية مع الحالة والدليل ومصدر القراءة/الاختبار والتاريخ، ويستخدم بصمة SHA-256 ورقمًا تسلسليًا مقنّعًا وتشفير Windows DPAPI. الحالات المتطابقة المتكررة تُزال من التكرار؛ عدم الاختبار لا يتحول إلى نجاح.", ModuleImplementationStatus.InDevelopment, "OpenDiagnosticEvidenceHistory"),
                new("camera-tests", "جرد Camera2 والتقاط صور وفحص AF", "Camera2 route tests", "المسارات التي يكشفها مصنع الجهاز فقط؛ لا يقيس جودة العدسة بصريًا. تُدار اختبارات Agent من نفس شاشة تشخيص العتاد.", ModuleImplementationStatus.Implemented),
                new("screen-photo", "تحليل صورة فعلية للشاشة", "Physical screen photo analysis", "يعرض مرشحات بكسلات وتفاوت إضاءة، وليس إثباتًا نهائيًا لعيب الشاشة.", ModuleImplementationStatus.Implemented, "AnalyzeScreen"),
                new("bench-measurements", "قراءات جهاز قياس خارجي متوافق", "External bench measurements", "بروتوكول Serial JSON-Lines للقراءات فقط؛ لا يصدر أوامر طاقة.", ModuleImplementationStatus.Implemented, "MeasurementRig"),
                new("reference-measurement-profile", "مقارنة القياسات بحدود مرجعية فنية", "Technician-supplied measurement limit profiles", "يحمّل ملف JSON موثق المصدر بحدود دنيا/عليا، ويقارن القراءات الفعلية من جهاز القياس ويصنف Passed/OutOfRange/NotMeasured دون اختراع حدود مصنع.", ModuleImplementationStatus.Tested),
                new("board-level", "قياسات اللوحة والمكونات الكهربائية", "Board-level electrical tests", "تتطلب Fixture ومخططات ومجسات آمنة مناسبة للموديل.", ModuleImplementationStatus.Unsupported)
            ]),
        new("network-diagnostics", 16, "الاتصال والتشخيص", "تشخيص الشبكات", "Network Diagnostics",
            "تشخيص شبكة Windows، وقراءة واجهات ومسارات Android، مع اختبار DNS وطلب ICMP واحد من الهاتف نفسه للهدف الذي يحدده المستخدم.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("host-network-interfaces", "قراءة واجهات Windows وعناوين IP والبوابات وDNS", "Windows network interfaces", "يقرأ بيانات واجهات الشبكة المحلية المتاحة من النظام دون إرسال طلبات خارجية.", ModuleImplementationStatus.Implemented),
                new("host-network-probe", "حل DNS واختبار ICMP لهدف يختاره المستخدم", "User-selected DNS and ICMP probe", "يجري حل DNS ثم طلب Ping واحدًا لعنوان واحد؛ قد يحجب الجدار الناري ICMP ولا يعني فشل DNS.", ModuleImplementationStatus.Implemented, "OpenNetworkDiagnostics"),
                new("phone-network", "قراءة واجهات ومسارات الشبكة من Android", "Android network interface and route observations", "يقرأ عناوين واجهات الشبكة ومسارات IP التي يعرضها Android عبر ADB للجهاز المصرح به؛ لا يدّعي قراءة كلمات مرور Wi-Fi أو بيانات خاصة، ولا يثبت اتصال الإنترنت.", ModuleImplementationStatus.Implemented),
                new("phone-network-probe", "اختبار DNS وPing من الهاتف نفسه", "Android-origin DNS and ICMP probe", "يقرأ خادمي DNS اللذين يعرضهما Android ويرسل طلب Ping واحدًا إلى مضيف يحدده الفني من الهاتف المحدد؛ يحتفظ بالمخرجات الخام ولا يعتبر حجب ICMP دليلًا على انقطاع الإنترنت.", ModuleImplementationStatus.Tested)
            ]),
        new("smart-repair-ai", 17, "الذكاء والمعرفة", "مساعد الإصلاح الذكي", "Smart Repair AI",
            "يتضمن الآن محرك استدلال محليًا قائمًا على قواعد وأدلة فعلية؛ لا يدّعي أنه نموذج AI أو تشخيص مادي نهائي.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("repair-guides", "البحث عن أدلة إصلاح خارجية", "External repair guide search", "يفتح مصادر iFixit الأصلية؛ هذه ليست ميزة ذكاء اصطناعي.", ModuleImplementationStatus.Implemented, "RepairGuides"),
                new("evidence-reasoning", "تحليل قراءات البطارية والتخزين ونتائج الاختبارات", "Evidence-based repair reasoning", "يرتب توصيات غير مدمرة ويربط كل توصية بدليل ودرجة ثقة وحدود واضحة؛ لا يستنتج أعطالًا من بيانات مفقودة.", ModuleImplementationStatus.Implemented, "OpenRepairAdvisor"),
                new("offline-knowledge", "قاعدة معرفة محلية مشفرة قابلة للبحث والإدارة", "Encrypted technician-authored repair knowledge", "قاعدة DPAPI محلية للفني تشمل إضافة وتعديل وحذف وبحث ملاحظات حسب الشركة والموديل والعَرَض والتشخيص والدليل والخطوات الآمنة؛ تبدأ من مساعد الإصلاح مع تهيئة موديل الجهاز. لا تُعرض كبيانات OEM أو عيوب مصنع دون مصدر يضيفه الفني.", ModuleImplementationStatus.Tested)
            ]),
        new("one-click-diagnostic", 18, "الذكاء والمعرفة", "الفحص الشامل بزر واحد", "One-Click Diagnostic",
            "يجمع قراءات ADB غير المدمرة في جلسة واحدة ويصدر تقريرًا مفصلًا مع نتائج جزئية وحدود واضحة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("device-inspect", "قراءة بيانات الجهاز وتحديث تقرير المكونات", "Device inspection", "قراءات ADB المتاحة دون تغيير إعدادات الهاتف؛ الفحص العام موجود في قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("automated-suite", "سلسلة قراءات ADB للخصائص والبطارية والتخزين ووقت التشغيل والتطبيقات", "Read-only diagnostic orchestration", "ينفذ خمسة أوامر قراءة محددة على الجهاز المختار، يدعم الإلغاء ويحفظ تقرير JSON؛ لا يشغل اختبارات العتاد أو العمليات الحساسة تلقائيًا.", ModuleImplementationStatus.Implemented, "OpenOneClickDiagnostic")
            ]),
        new("safe-recovery", 19, "Firmware والاستعادة", "الاستعادة الآمنة", "Safe Recovery",
            "فحص مسبق محافظ داخل Firmware Center يعيد التحقق من الملف ويقارن الشركة والموديل المقروءين، ويعرض شروط المصدر والتوافق وAnti-Rollback والنسخة الاحتياطية. لا توجد عملية Flash.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("firmware-precheck", "فحص أولي لبنية ملف Firmware وبصمته", "Firmware pre-inspection", "الفحص المحلي وSHA-256 متاحان في Firmware Center، ولا يثبتان توقيع الناشر أو التوافق.", ModuleImplementationStatus.Implemented),
                new("recovery-gate", "بوابة فحص الشركة والموديل والتصريح وسلامة الملف والمخاطر", "Conservative recovery preflight", "يعيد حساب SHA-256 قبل التقييم، ويطلب Snapshot مطابقًا للجهاز المصرح به، ويعرض Passed/Failed/Unknown/Blocked مع الأدلة. CanProceed يبقى false لأن سلسلة ثقة الناشر والتوافق وAnti-Rollback وإثبات النسخة غير متاحة.", ModuleImplementationStatus.Implemented),
                new("official-recovery", "إجراءات استعادة رسمية حسب الموديل", "Official model-specific recovery", "غير منفذة؛ لا يوجد تفليش أو فتح Bootloader.", ModuleImplementationStatus.Unsupported),
                new("forgotten-lock-owner-guidance", "مساعد استرداد قفل الشاشة للمالك", "Owner-safe forgotten screen-lock recovery guidance", "يختار إرشادات الشركة الرسمية حسب النظام والموديل وإصدار One UI/OS وتوفر بيانات الحساب والنسخة الاحتياطية وإثبات الملكية. لا يخمن أو يقرأ PIN/Passcode، ولا يتجاوز FRP أو Activation Lock أو MDM، ولا ينفذ إعادة ضبط أو مسح.", ModuleImplementationStatus.InDevelopment, "OpenLockRecoveryAssistant")
            ]),
        new("workflow-automation", 20, "الأتمتة والإنتاجية", "أتمتة سير العمل", "Workflow Automation",
            "إنشاء وحفظ وتشغيل سلاسل فحص Android من خطوات قراءة فقط محددة مسبقًا مع تتبع النتائج والإلغاء.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("workflow-builder", "إنشاء وحفظ قوالب Workflow بصيغة JSON", "Workflow builder and JSON templates", "محرر خطوات من قائمة سماح، مع حفظ وتحميل ملفات JSON محلية والتحقق من المخطط قبل القبول.", ModuleImplementationStatus.Implemented, "OpenAndroidWorkflow"),
                new("workflow-runner", "تشغيل متتابع مع تقدم وإلغاء وتقرير لكل خطوة", "Sequential workflow runner", "ينفذ خطوات القراءة فقط على الجهاز المحدد، ويتوقف عند الفشل أو الإلغاء ويسجل ناتج كل خطوة؛ لا ينفذ أوامر Shell حرة.", ModuleImplementationStatus.Implemented)
            ]),
        new("batch-operations", 21, "الأتمتة والإنتاجية", "العمليات الجماعية", "Batch Operations",
            "تنفيذ أوامر Android غير المدمرة على أجهزة ADB المصرح بها بالتوازي المحدود وعزل نتيجة كل جهاز.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("batch-selection", "اختيار مجموعة أجهزة مصرح بها", "Authorized multi-device selection", "يتيح اختيار عدة أجهزة من قائمة الاكتشاف الفعلية ويربط كل نتيجة بالمعرف المحدد.", ModuleImplementationStatus.Implemented, "OpenAndroidBatchTools"),
                new("batch-runner", "تنفيذ متوازٍ محدود لأوامر قراءة فقط", "Bounded read-only batch runner", "ينفذ أوامر الكتالوج الآمنة بحد أقصى 3 أجهزة متزامنة مع إلغاء مشترك ونتيجة مستقلة لكل جهاز.", ModuleImplementationStatus.Implemented)
            ]),
        new("repair-history", 22, "إدارة مركز الصيانة", "سجل الصيانة", "Repair History",
            "سجل محلي مستمر لأحداث الأجهزة والورشة، مع البحث النصي والتصفية حسب نوع العملية وتصدير CSV للسجلات المفلترة مع بقاء معرف الجهاز مقنّعًا.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("workshop-events", "سجل فعاليات وأوامر مركز الصيانة", "Workshop activity history", "يحفظ محليًا آخر 5000 فعالية مرتبطة بالعملاء والأوامر والمخزون؛ يفتح تبويب العمليات والسجل في الإدارة العالمية.", ModuleImplementationStatus.Implemented, "OpenGlobalWorkshopHistory"),
                new("device-history", "تاريخ الفحوص والأجهزة والتقارير لكل جهاز", "Per-device repair history", "يحفظ الأحداث محليًا ويربطها ببصمة SHA-256 لمعرف الجهاز بدل تخزين Serial الخام؛ يعرض الرقم مقنّعًا ويمكن فلترة السجل حسب الجهاز.", ModuleImplementationStatus.Implemented, "OpenDeviceHistory"),
                new("history-search-export", "البحث والتصفية وتصدير سجل الصيانة", "Repair history search, event filter and CSV export", "بحث نصي في الجهاز والموديل والعملية والملخص والدليل، وتصفية حسب نوع العملية، وتصدير النتائج المطابقة CSV دون Serial خام أو بصمة الجهاز.", ModuleImplementationStatus.Tested)
            ]),
        new("workshop-manager", 23, "إدارة مركز الصيانة", "إدارة مركز الصيانة", "Workshop Manager",
            "إدارة العملاء وأوامر الإصلاح والحالات وتكاليف العمل والضمان وبيانات المركز متاحة محليًا.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("customers", "إدارة العملاء", "Customer management", "إضافة وتعديل بيانات العملاء مع تخزين محلي.", ModuleImplementationStatus.Implemented),
                new("repair-orders", "إنشاء أوامر الصيانة ومتابعة الحالات", "Repair orders", "أرقام Ticket وحالة الجهاز ونتائج ما قبل/بعد الإصلاح.", ModuleImplementationStatus.Implemented, "OpenWorkshop"),
                new("warranty", "الإعدادات الدولية والضمان وسجل الفني", "International profile and warranty", "العملة واللغة والمنطقة الزمنية ومدة الضمان وسجل المتابعة وتقارير HTML.", ModuleImplementationStatus.Implemented),
                new("quotes-invoices", "إيصالات الاستلام وعروض الأسعار والفواتير", "Receipts, quotes and invoices", "مستندات HTML قابلة للطباعة لإيصال الاستلام والعرض والفاتورة؛ سجل موافقة أو رفض العميل مع تاريخ القرار وطريقة التواصل والفني؛ سجل دفعات متعددة مع منع التجاوز وحساب الرصيد وإيصال منفصل لكل دفعة. هذه سجلات محلية يدوية وليست توقيعًا إلكترونيًا أو تحققًا مصرفيًا؛ لا توجد معالجة/رد مدفوعات أو حساب ضرائب أو فاتورة إلكترونية معتمدة.", ModuleImplementationStatus.Tested),
                new("workshop-backup-scheduler", "جدولة النسخ اليومي المشفر لقاعدة الورشة", "Scheduled encrypted workshop database backup", "ينشئ مهمة Windows Task Scheduler يومية لحساب المستخدم الحالي، تنسخ ملف قاعدة الورشة المحمي بـDPAPI كما هو دون فك تشفير بيانات العملاء، مع تحديد وقت النسخ ومجلد الحفظ ومدة الاحتفاظ وفحص الحالة وحذف المهمة. تعمل مع جلسة Windows مسجلة الدخول؛ لا تعمل بعد تسجيل الخروج.", ModuleImplementationStatus.Implemented, "OpenWorkshopBackupScheduler")
            ]),
        new("parts-inventory", 24, "إدارة مركز الصيانة", "مخزون قطع الغيار", "Parts Inventory",
            "إضافة الأصناف وتعديلها وحركات الصرف والإضافة والتنبيه بانخفاض المخزون متاحة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("stock-items", "إدارة الأصناف والكميات وحد إعادة الطلب", "Inventory items", "يدير SKU والكمية والتكلفة والموقع وحد إعادة الطلب من شاشة مخزون قطع الغيار الموحدة.", ModuleImplementationStatus.Implemented, "OpenPartsInventory"),
                new("stock-movements", "حركات المخزون وسجلها", "Stock movements", "يمنع الرصيد السالب ويسجل سبب الحركة.", ModuleImplementationStatus.Implemented),
                new("supplier-purchasing", "الموردون واستلام المشتريات وفواتير الموردين", "Supplier receipts and purchase invoices", "استلام كمية لصنف موجود مع المورد ورقم الفاتورة وتكلفة الشراء، وتحديث متوسط التكلفة المرجح وتسجيل إيصال الشراء وحركة المخزون في حفظ ذري واحد؛ يدعم تصدير سجل المشتريات CSV. أوامر الشراء متعددة الأصناف واعتماد الفواتير والمدفوعات غير منفذة.", ModuleImplementationStatus.Implemented),
                new("inventory-catalogue", "إدارة الموردين والتصنيفات والتكلفة وسجل الحركات", "Supplier and stock movement catalogue", "نافذة موسعة لإدارة SKU والتصنيف والمورد وتكلفة الشراء وسعر البيع وحد إعادة الطلب، مع حركات مخزون قابلة للمراجعة وتصدير CSV.", ModuleImplementationStatus.Implemented),
                new("repair-order-part-usage", "صرف قطع الغيار وربط تكلفتها بأمر الصيانة", "Repair order parts consumption", "يخصم الكمية من المخزون ويربط القطعة والكمية وتكلفة الوحدة والفني بأمر الإصلاح، ويسجل حركة المخزون ضمن عملية حفظ واحدة.", ModuleImplementationStatus.Implemented),
                new("inventory-movement-history", "سجل حركات الإضافة والصرف والتعديل", "Inventory movement ledger", "يسجل فرق الكمية والرصيد بعد الحركة والسبب والفني وأمر الصيانة المرتبط، ويظهر في شاشة المخزون والتقارير.", ModuleImplementationStatus.Implemented)
            ]),
        new("service-reports", 25, "إدارة مركز الصيانة", "تقارير الخدمة والإحصائيات", "Service Reports",
            "تقارير تشخيص JSON وتقارير ورشة HTML/TXT وتصدير CSV موجودة؛ لوحة التحليلات المالية غير مكتملة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("device-json-report", "تقرير تشخيص الجهاز بصيغة JSON", "Device diagnostic JSON report", "يتضمن القراءات والاختبارات وحدود التفسير.", ModuleImplementationStatus.Implemented, "ExportDeviceReport"),
                new("workshop-html", "تقرير صيانة HTML قابل للطباعة أو PDF من المتصفح", "Printable workshop report", "يدعم العربية والإنجليزية وبيانات المركز والضمان؛ يفتح تبويب التقارير في الإدارة العالمية.", ModuleImplementationStatus.Implemented, "OpenGlobalWorkshopReports"),
                new("workshop-csv", "تصدير أوامر الصيانة والمخزون CSV", "Repair and inventory CSV export", "تصدير UTF-8 مع حماية حقول CSV من تفسيرها كصيغ.", ModuleImplementationStatus.Implemented),
                new("analytics", "تحليلات الأعطال والإيرادات وأداء الفنيين", "Service analytics", "يعرض مؤشرات فعلية محسوبة من أوامر الصيانة والمخزون المحفوظة: حالات الأوامر، الإيراد المسجل للأوامر المسلمة، قيمة التقديرات المفتوحة، أوصاف الأعطال المتكررة، الموديلات، أداء الفنيين، الإيراد الشهري والمخزون؛ لا يحسب الربح أو الضرائب.", ModuleImplementationStatus.Tested)
            ]),
        new("plugin-store", 26, "التوسعة والمكونات", "متجر الإضافات", "Plugin Store",
            "يدعم فحص الحزم وتوقيع RSA والتحقق من مفتاح الناشر المخزن بـDPAPI وتثبيت الحزم الموقعة في مكتبة محلية؛ التشغيل المتاح Workflow JSON قراءة فقط من خلال قوائم ADB ثابتة، ولا يتم تحميل DLL خارجي.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("plugin-package-inspection", "فحص Manifest وبنية الحزمة والصلاحيات والبصمة", "Local package inspection", "يفحص ZIP ويدعم منع Zip Slip والروابط الرمزية والحزم الضخمة والصلاحيات غير المعروفة؛ لا يثبت أو يشغّل DLL.", ModuleImplementationStatus.Implemented, "OpenPluginStore"),
                new("plugin-signing", "توقيع الحزمة والتحقق من مفتاح الناشر وجذر الثقة", "RSA publisher signatures and trusted public-key roots", "يوقّع حزمة ZIP بمفتاح خاص يختاره الناشر، ويتحقق من RSA-PSS/SHA-256 مقابل مفتاح عام استورده المستخدم بعد مقارنة بصمته عبر قناة مستقلة؛ تُحمى المفاتيح العامة محليًا عبر DPAPI.", ModuleImplementationStatus.Tested),
                new("plugin-install", "إدارة الحزم الموقعة وتشغيل Workflow قراءة فقط", "Signed package library and read-only workflow execution", "يحفظ ويزيل الحزم الموقعة من المكتبة المحلية، ويطلب موافقات صلاحيات لكل جلسة ويعيد التحقق من توقيع الحزمة والهاتف قبل تنفيذ workflow.json عبر أوامر Android المسموحة فقط؛ لا ينفذ DLL.", ModuleImplementationStatus.Tested),
                new("plugin-native-runtime", "تشغيل إضافات DLL خارجية داخل عزل منخفض الصلاحيات", "Isolated native plugin runtime", "غير متاح في هذا الإصدار؛ لا يتم تحميل DLL خارجي أو تشغيل كود Plugin أصلي. يلزم عزل Windows/IPC وصلاحيات موارد واختبارات اختراق قبل السماح بذلك.", ModuleImplementationStatus.Planned)
            ]),
        new("plugin-sdk", 27, "التوسعة والمكونات", "حزمة تطوير الإضافات", "Plugin SDK",
            "أصبح هناك نموذج Manifest بإصدار 1 وقائمة صلاحيات محددة ومدقق توافق fail-closed؛ لا يوجد Host لتشغيل الإضافات.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("plugin-contracts", "عقود Plugin Manifest وإصدارات API والصلاحيات", "Plugin manifest and permission contracts", "عقود Core تفرض semantic versioning ومعرفات مستقرة وترفض الصلاحيات المجهولة أو الخطرة.", ModuleImplementationStatus.Implemented),
                new("plugin-sample", "فاحص حزم ZIP ونموذج توثيق للمطور", "Package inspection and developer starter", "فاحص الحزم محليًا وملفات بداية SDK لا تحمل DLL ولا تشغل Shell.", ModuleImplementationStatus.Implemented),
                new("plugin-workflow-host", "مضيف Workflow بيانات موقّع للقراءة فقط", "Signed data-only plugin workflow host", "ينفذ workflow.json بعد التحقق من التوقيع ومنح صلاحيات القراءة/التشخيص وإعادة التحقق من ADB؛ يمرر فقط preset IDs المسموحة ويتوقف عند أول فشل. لا يعد محرك CLR لإضافات DLL.", ModuleImplementationStatus.Tested)
            ]),
        new("offline-center", 28, "النظام والحماية", "مركز العمل دون إنترنت", "Offline Center",
            "لوحة قراءة فقط تفحص حزمة SoftBox المحلية وتعرض الأدوات المرفقة والملفات الناقصة وإصداراتها وأحجامها وبصمات SHA-256 وقواعد البيانات المحلية.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("local-adb", "أدوات ADB والفحوص المحلية", "Local ADB and diagnostics", "تعمل بأدوات الحزمة المحلية بعد تثبيتها؛ مدخل الاكتشاف الأساسي موجود في مركز الأجهزة.", ModuleImplementationStatus.Implemented),
                new("local-workshop", "قاعدة ورشة العمل والنسخ المحلي", "Local workshop records", "قاعدة JSON محلية ونسخ يدوي؛ ليست مزامنة متعددة الفروع. نسخة الورشة تُفتح من إدارة مركز الصيانة.", ModuleImplementationStatus.Implemented),
                new("offline-status", "فحص جاهزية الحزمة المحلية وتصدير تقرير", "Offline package readiness report", "يفحص وجود ADB وFastboot وscrcpy وAndroid Agent وتعريف Samsung وBoardviewer داخل مجلد الحزمة الحالي، ويحسب بصمات SHA-256 للملفات الموجودة ويحصي ملفات قواعد بيانات الأجهزة واللوحات. لا ينزل أو يثبت أي مكوّن ولا يعتبر واجهة الشبكة دليلًا على الإنترنت.", ModuleImplementationStatus.Tested, "OpenOfflineCenter")
            ]),
        new("security-privacy", 29, "النظام والحماية", "الأمان والخصوصية", "Device Security & Privacy",
            "فحص قراءة فقط لمؤشرات قفل الشاشة وإدارة الجهاز وخصائص SIM، مع مسارات الاستعادة الرسمية. لا تتضمن أدوات تجاوز FRP أو حسابات OEM أو PIN أو MDM أو قفل الشبكة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("adb-arguments", "تمرير وسائط البرامج دون بناء أوامر Shell نصية", "Safe process arguments", "AdbClient يستخدم ArgumentList ويقيد Serial ومعلمات Agent المسموح بها.", ModuleImplementationStatus.Implemented),
                new("agent-consent", "موافقة المستخدم قبل تشغيل جسر Agent", "On-device consent", "جسر Agent لا يبدأ إلا بعد موافقة المستخدم داخل تطبيق الهاتف.", ModuleImplementationStatus.Implemented),
                new("read-only-forensics", "فحص USB وFirmware والسجلات بوضع قراءة فقط", "Read-only forensic operations", "لا ينفذ هذا المسار Flash أو Unlock أو Wipe أو تجاوز أقفال. فحص أوضاع النقل في طبيب USB.", ModuleImplementationStatus.Implemented),
                new("encrypted-data", "تشفير قاعدة البيانات والنسخ وإدارة مفاتيح", "At-rest encryption and key management", "تُشفّر ملفات حسابات المستخدمين والتدقيق، وقاعدة الورشة، وسجل الأجهزة والإشعارات باستخدام Windows DPAPI CurrentUser مع غرض تشفير منفصل لكل مخزن. تُرحّل ملفات JSON القديمة عند أول قراءة ناجحة. ملفات النسخ .sbxbackup مشفرة ومقيدة بحساب Windows نفسه؛ لا توجد مزامنة مفاتيح أو استعادة عابرة للحسابات والأجهزة.", ModuleImplementationStatus.Tested),
                new("device-access-posture", "فحص حالة شاشة القفل ومؤشرات إدارة الجهاز وخصائص SIM", "Read-only device access diagnostics", "ينفذ على الجهاز المحدد المصرح عبر ADB قراءات dumpsys window وdumpsys device_policy وخصائص gsm.sim.state وgsm.operator.alpha. يعرض ظهور Keyguard لحظة الفحص ومؤشرات Device Owner/Profile Owner/Active Admin ومكونات ComponentInfo ورموز قيود no_* المرصودة وخصائص SIM، ويقرأ من dumpsys telephony.registry حالة تسجيل الصوت والبيانات ونوع الراديو وحالة التجوال والطوارئ والبحث واسم المشغل بعد فلترة الحقول المسموح بها. لا يقرأ PIN/Pattern أو حسابات Google/Samsung/Xiaomi ولا يحسم حالة FRP أو فتح الشبكة؛ غياب المؤشر لا يثبت غياب القفل أو MDM. لم يُتحقق ماديًا من كل موديلات Android.", ModuleImplementationStatus.InDevelopment, "OpenDeviceAccessRecoveryCenter"),
                new("official-recovery-case-builder", "معالج إنشاء ملف طلب الاسترداد الرسمي للحالات الست", "Official recovery case builder", "ينشئ ملف Markdown محليًا لطلب Google FRP أو Xiaomi Mi Account أو Samsung FRP أو استرداد قفل الشاشة أو طلب فك MDM بتفويض مسؤول المؤسسة أو طلب فتح الشبكة بتفويض صاحب حساب المشغّل. يتحقق من إقرارات الصلاحية، ولا يجمع IMEI أو Serial أو كلمات المرور أو PIN أو رموز OTP، ولا يرسل الطلب أو يغيّر الهاتف.", ModuleImplementationStatus.Implemented),
                new("official-account-recovery", "استعادة حساب Google أو Samsung أو Xiaomi بالمسارات الرسمية", "Official account recovery hand-off", "يعرض بوابات استرداد الحساب وطلبات المراجعة الرسمية ومراكز الخدمة. لا يتصل بخوادم الحسابات نيابة عن المستخدم ولا يزيل Activation Lock.", ModuleImplementationStatus.Implemented),
                new("screen-lock-recovery-guidance", "إرشادات PIN وPattern وScreen Lock", "Official screen-lock recovery guidance", "يوجه إلى خيارات الاسترداد الرسمية الخاصة بالشركة. لا يستخرج رمز القفل أو يمحو القفل؛ أي إعادة ضبط قد تحذف بيانات المستخدم وقد تترك حماية FRP أو Activation Lock قائمة.", ModuleImplementationStatus.Implemented),
                new("carrier-network-unlock-guidance", "طلب فتح SIM / Network من شركة الاتصالات", "Carrier-authorized network unlock request", "مسار إرشادي للتحقق من IMEI والأهلية والعقد لدى شركة الاتصالات الأصلية. لا يغير Baseband أو NV أو Network Lock ولا يدّعي فتح الشبكة من الهاتف.", ModuleImplementationStatus.Implemented),
                new("google-frp-bypass", "Google FRP Bypass", "Google FRP bypass", "غير مدعوم: لا ينشئ SoftBox تجاوزًا لحماية Google من إعادة ضبط المصنع. استخدم حساب Google السابق أو قناة استرداد الحساب/الشركة المعتمدة.", ModuleImplementationStatus.Unsupported),
                new("xiaomi-account-bypass", "Xiaomi Mi Account Bypass", "Xiaomi Mi Account bypass", "غير مدعوم: لا يتجاوز SoftBox قفل تنشيط Xiaomi المرتبط بحساب Mi أو خوادم Find Device. استخدم استرداد الحساب أو طلب المراجعة الرسمي من Xiaomi.", ModuleImplementationStatus.Unsupported),
                new("samsung-frp-bypass", "Samsung FRP Bypass", "Samsung FRP bypass", "غير مدعوم: لا يتجاوز SoftBox حماية Google على أجهزة Samsung أو تحقق حساب Samsung. استخدم استرداد الحساب أو مركز خدمة Samsung المعتمد مع إثبات الملكية.", ModuleImplementationStatus.Unsupported),
                new("screen-lock-bypass", "تخطي PIN / Pattern / Screen Lock", "PIN / pattern / screen-lock bypass", "غير مدعوم: لا يستخرج SoftBox بيانات الاعتماد ولا يعطل قفل الشاشة. استخدم وسائل الاسترداد التي توفرها الشركة المالكة للجهاز؛ إعادة الضبط قد تمسح البيانات.", ModuleImplementationStatus.Unsupported),
                new("mdm-bypass", "MDM Bypass", "MDM bypass", "غير مدعوم: لا يزيل SoftBox إدارة المؤسسات أو Device Owner أو Profile Owner. إزالة الإدارة تتطلب مسؤول المؤسسة أو مزود EMM المخول.", ModuleImplementationStatus.Unsupported),
                new("sim-network-unlock-bypass", "SIM / Network Unlock Bypass", "SIM / network unlock bypass", "غير مدعوم: لا يغير SoftBox حالة قفل المشغل أو بيانات Baseband/NV. فتح الشبكة يتم فقط بموافقة شركة الاتصالات الأصلية وفق أهلية IMEI.", ModuleImplementationStatus.Unsupported)
            ]),
        new("user-technician-management", 30, "النظام والحماية", "المستخدمون والفنيون والصلاحيات", "User & Technician Management",
            "حسابات محلية حقيقية بأدوار مدير وفني وقراءة فقط، ومصادقة PBKDF2، وقفل مؤقت بعد محاولات فاشلة، وسجل تدقيق حسب المستخدم.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("technician-field", "تسجيل اسم الفني على أمر الصيانة", "Technician attribution field", "حقل اسم فني وليس حساب مستخدم موثق؛ يظهر مع أوامر الصيانة في إدارة مركز الصيانة.", ModuleImplementationStatus.Implemented),
                new("accounts", "حسابات ومصادقة وأدوار وصلاحيات", "Accounts and roles", "إعداد أول مدير، وتسجيل دخول محلي، وإدارة حسابات مدير وفني وقراءة فقط، وتجزئة كلمات المرور باستخدام PBKDF2-HMAC-SHA256.", ModuleImplementationStatus.Tested, "OpenUserManagement"),
                new("audit-user", "قفل الجلسة وسجل عمليات حسب هوية المستخدم", "Session locking and per-user audit", "قفل الجلسة يتطلب إعادة المصادقة، ويُسجل الدخول والفشل وتغيير كلمة المرور ومنع الصلاحيات وبدء تشغيل الأدوات حسب هوية المستخدم في سجل محلي محدود الحجم.", ModuleImplementationStatus.Tested)
            ]),
        new("update-center", 31, "النظام والحماية", "مركز التحديثات", "Update Center",
            "يفحص GitHub Stable/Beta، ويتيح استيراد مفتاح ناشر مستقل، وتنزيل حزمة Windows موقعة والتحقق من Manifest وبصمات كل الملفات ثم تجهيزها للتثبيت بعد إغلاق التطبيق مع Snapshot للرجوع.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("catalog-update", "تحديث كتالوج الموديلات", "Device catalog refresh", "يحدّث بيانات الموديلات ويخزن نسخة محلية؛ مدخل التحديث في قسم ذكاء الأجهزة.", ModuleImplementationStatus.Implemented),
                new("release-check", "فحص إصدارات SoftBox الرسمية Stable/Beta", "Official release channel check", "يقرأ metadata من GitHub API، يقارن SemVer، ويقبل صفحة الإصدار وروابط الأصول من مستودع SoftBox الرسمي فقط؛ لا ينزّل أو ينفذ ملفات.", ModuleImplementationStatus.Implemented, "OpenUpdateCenter"),
                new("application-updater", "تنزيل وتحقق توقيع وتثبيت تحديث SoftBox", "Signed application updater and rollback helper", "ينزّل فقط أصلًا من GitHub Releases الرسمي، ويتحقق من مفتاح RSA موثوق وManifest وبصمات الملفات والحد الأدنى للإصدار قبل المرحلة؛ ينشئ مساعد تثبيت بعد إغلاق العملية وSnapshot للرجوع. يحتاج مفتاح إصدار عام استورده الفني بعد التحقق المستقل، ولا توجد مفاتيح توقيع خاصة داخل المستودع.", ModuleImplementationStatus.Tested),
                new("release-channels", "قنوات Stable/Beta وسياسة الرجوع الآمن", "Release channels and rollback", "واجهة Stable/Beta تستخدم ترتيب SemVer وتستبعد prerelease من Stable؛ تثبيت حزمة موقعة يتطلب موافقة المستخدم، وRollback يعيد Snapshot السابق عبر مساعد منفصل تم اختباره على Windows.", ModuleImplementationStatus.Tested),
                new("signed-release-build", "إنشاء حزمة تحديث Windows موقعة ومطابقة لمدقق التطبيق", "Signed Windows release package builder", "أداة إصدار تبني Manifest من ملفات النشر الفعلية وتحسب الأحجام وSHA-256 وتوقع الحزمة بمفتاح RSA-PSS خاص يقدمه مسؤول الإصدار؛ المفتاح الخاص لا يدخل الحزمة أو المستودع.", ModuleImplementationStatus.Tested),
                new("release-key-adoption", "اعتماد مفتاح الناشر في GitHub والتحقق المستقل منه", "Production publisher key adoption", "يتطلب تعيين SOFTBOX_RELEASE_PRIVATE_KEY_PEM في GitHub Actions ونشر المفتاح العام وبصمته عبر قناة مستقلة ثم استيراده داخل Update Center؛ لا يمكن ادعاء نشر إصدار إنتاجي موقع قبل توفير هذا المفتاح.", ModuleImplementationStatus.InDevelopment)
            ]),
        new("notification-center", 32, "النظام والحماية", "مركز الإشعارات", "Notification Center",
            "صندوق إشعارات محلي محفوظ لأحداث اتصال الأجهزة ونتائج الفحص وتصدير التقارير، مع تعليم الإشعار كمقروء.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("status-text", "رسالة حالة فورية للعمليات", "Immediate status text", "يعرض StatusMessage نتيجة آخر إجراء، وليس سجل إشعارات.", ModuleImplementationStatus.Implemented),
                new("notification-inbox", "صندوق إشعارات محفوظ مع حالات القراءة", "Persistent notification inbox", "يحفظ وقت الإشعار ونوعه وعنوانه ورسالة مختصرة والقسم المرتبط وبصمة الجهاز الاختيارية؛ يدعم القراءة الفردية أو الجماعية.", ModuleImplementationStatus.Implemented, "OpenNotificationCenter")
            ]),
        new("help-center", 33, "النظام والحماية", "مركز المساعدة", "Help Center",
            "مركز مساعدة محلي قابل للبحث بالعربية والإنجليزية ويشرح أدوات SoftBox الحالية وحدود التوافق والأمان.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("connection-help", "تعليمات USB وADB", "USB and ADB connection help", "نافذة المساعدة العامة لتوصيل USB وADB متاحة.", ModuleImplementationStatus.Implemented, "ConnectionHelp"),
                new("agent-help", "تعليمات PhoneLab Agent", "PhoneLab Agent help", "إرشادات فتح وتفويض تطبيق الهاتف.", ModuleImplementationStatus.Implemented, "AgentHelp"),
                new("offline-help", "قاعدة مساعدة قابلة للبحث دون إنترنت", "Searchable offline help", "كتالوج محلي لمقالات الاتصال وADB وAPK والنسخ وFirmware وUSB والشبكات وWorkflow والدعم، مع بحث بالعربية والإنجليزية.", ModuleImplementationStatus.Implemented, "OpenHelpCenter")
            ]),
        new("support-logs", 34, "النظام والحماية", "الدعم والسجلات", "Support & Logs",
            "جمع سجلات Android وحزمة دعم محلية بمعلومات الإصدار والبيئة والملفات التي يختارها المستخدم مع تنقيح أسرار شائعة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("android-logs", "جمع سجلات Android وتحليل أولي", "Android logs and boot hints", "يحفظ أدلة محدودة مع تنبيه خصوصية؛ تحليل إعادة التشغيل heuristic وليس تشخيصًا مؤكدًا.", ModuleImplementationStatus.Implemented, "AndroidLogs"),
                new("firmware-log", "تقرير فحص Firmware والبصمة", "Firmware inspection report", "يعرض SHA-256 وبنية الأرشيف دون تفليش؛ أداة الفحص والتقرير في قسم Firmware Center.", ModuleImplementationStatus.Implemented),
                new("app-debug-bundle", "حزمة دعم محلية مع تنقيح كلمات المرور وTokens", "Offline redacted support bundle", "تضم معلومات النظام وإصدار SoftBox والملفات التي يختارها المستخدم فقط، مع تنقيح حقول Password/Token/Authorization/Serial قبل إنشاء ZIP؛ لا يتم رفعها.", ModuleImplementationStatus.Implemented, "OpenSupportBundle"),
                new("whatsapp-support", "التواصل مع دعم SoftBox عبر WhatsApp", "WhatsApp support contact", "يفتح مركز الدعم المحلي مع رقم WhatsApp وخطوات استرداد الحساب برمز أحادي الاستخدام.", ModuleImplementationStatus.Implemented, "OpenSupportCenter")
            ]),
        new("settings", 35, "النظام والحماية", "الإعدادات", "Settings",
            "إعدادات مركز الصيانة وإعدادات تشغيل SoftBox المحلية محفوظة في ملف JSON مع تحقق من القيم.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("workshop-settings", "إعدادات مركز الصيانة الدولية", "International workshop settings", "الاسم والعنوان والدولة والهاتف والبريد والعملة واللغة والمنطقة الزمنية والضمان؛ يفتح تبويب الإعدادات في الإدارة العالمية.", ModuleImplementationStatus.Implemented, "OpenGlobalWorkshopSettings"),
                new("app-settings", "إعدادات الاكتشاف التلقائي وفتح الشاشة وAgent والتوازي", "Application preferences", "يحفظ إعدادات الاكتشاف والفترة الزمنية وفتح العرض وتهيئة Agent والفحص التلقائي وحد Batch محليًا؛ القيم الافتراضية تحافظ على السلوك السابق.", ModuleImplementationStatus.Implemented, "OpenApplicationSettings")
            ]),
        new("about", 36, "النظام والحماية", "حول SoftBox", "About",
            "صفحة مستقلة تعرض هوية SoftBox ورقم الإصدار وبيئة التشغيل والمسارات المحلية ومراجع إشعارات الطرف الثالث إذا كانت مرفقة بالحزمة.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("version-info", "عرض الإصدار وبيئة التشغيل", "Version and runtime information", "يقرأ إصدار Assembly و.NET وWindows ومعمارية التطبيق مباشرة من النظام.", ModuleImplementationStatus.Implemented, "OpenAbout"),
                new("third-party-notices", "عرض إشعارات ومراجع مكونات الطرف الثالث", "Third-party notices", "يعرض ملف THIRD-PARTY-NOTICES المحلي عند تضمينه مع الحزمة، أو يوضح أنه غير موجود في التثبيت الحالي.", ModuleImplementationStatus.Implemented)
            ]),
        new("iphone-center", 37, "أجهزة Apple", "مركز iPhone وiPad", "iPhone & iPad Center",
            "قسم مستقل لأجهزة Apple: قراءة هوية الجهاز عبر Lockdown، سجل القطع الرسمي، بيانات البطارية والفحوص الفيزيائية المسجلة بواسطة الفني. لا يخلط جلسات العرض مع Android.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("iphone-identity", "اكتشاف iPhone وقراءة معلوماته", "iPhone discovery and device information", "يقرأ معلومات plist الفعلية عبر أدوات Apple Lockdown بعد توصيل الجهاز وفتحه والموافقة على الوثوق بالكمبيوتر. لا يقرأ كلمات المرور ولا ينفذ Pair/Restore.", ModuleImplementationStatus.Implemented, "OpenIPhoneCenter"),
                new("iphone-parts-history", "سجل القطع والإصلاحات", "Parts and Service History", "يسجل الحالة التي يراها الفني في الإعدادات > عام > حول مثل Genuine أو Used أو Unknown أو Unverified؛ غياب السجل لا يثبت أن القطع أصلية.", ModuleImplementationStatus.Implemented, "OpenIPhonePartsHistory"),
                new("iphone-battery-health", "صحة البطارية ودورات الشحن", "Battery health and cycle count", "يسجل السعة القصوى وعدد الدورات ورسالة الخدمة من إعدادات iPhone الرسمية عند توفرها. نسبة الشحن الحالية لا تستخدم كبديل لصحة البطارية.", ModuleImplementationStatus.Implemented, "OpenIPhoneBatteryHealth"),
                new("iphone-functional-checks", "اختبارات الشاشة واللمس والكاميرا والحساسات", "Physical component checks", "يسجل نتائج فحص فعلي يجريه الفني للشاشة واللمس والكاميرات وFace ID والصوت والميكروفون والحساسات ومنفذ الشحن. لا تُملأ النتائج تلقائيًا.", ModuleImplementationStatus.Implemented, "OpenIPhoneFunctionalTests"),
                new("iphone-airplay", "عرض شاشة iPhone عبر AirPlay", "iPhone AirPlay screen viewing", "يفتح مستقبل AirPlay في نافذة مستقلة ذات مساحة عرض مستقلة عن Android/scrcpy. عرض فقط، ولا يرسل لمسًا أو رمز قفل إلى iPhone.", ModuleImplementationStatus.Implemented),
                new("iphone-hdmi-capture", "التقاط شاشة iPhone عبر HDMI/UVC", "iPhone HDMI/UVC capture", "يفتح مسار التقاط فيديو سلكي عند دعم موديل iPhone لمخرج الفيديو وتوفر محول Apple المناسب وكارت UVC؛ لا يدعم كل الموديلات ولا يوفر تحكم لمس.", ModuleImplementationStatus.InDevelopment, "OpenIPhoneHdmiCapture"),
                new("iphone-authenticity", "تقييم أدلة هوية الجهاز والأصالة", "Device identity evidence assessment", "يجمع استجابة Apple Lockdown ونتيجة فحص التغطية التي أدخلها الفني وسجل القطع؛ يصدر حالة أدلة متوافقة أو تحتاج مراجعة أو غير كافية، ولا يزعم إثبات أصالة مطلقًا.", ModuleImplementationStatus.InDevelopment, "OpenIPhoneAuthenticityEvidence")
            ]),
        new("device-identity-integrity", 38, "التحقق والحماية", "سلامة هوية الجهاز والتعبئة الذكية", "Device Identity Integrity & Smart Auto-Fill",
            "محرك قراءة مستقل يجمع أدلة Windows USB وADB وFastboot وApple Lockdown، يصنّف أوضاع الاتصال، يكشف تعارضات المعرّفات، ويملأ حقول معلومات الجهاز من المصدر الحقيقي دون تعديل هوية الهاتف.",
            ModuleImplementationStatus.InDevelopment,
            [
                new("live-mode-detection", "اكتشاف وضع الاتصال تلقائيًا", "Live transport-mode detection", "يرصد USB PnP وADB وFastboot كقنوات مستقلة، ويصنّف أوضاع EDL وPreloader/BROM وDownload Mode وRecovery وMTP وADB عند توفر دليل واضح. التلميح لا يُعامل كتعريف موديل أو تصريح وصول.", ModuleImplementationStatus.Tested, "OpenDeviceIdentityIntegrity"),
                new("identity-source-map", "خريطة مصدر كل معرّف ودرجة الثقة", "Identifier provenance and confidence", "يعرض نوع المعرّف ومصدره ووقت القراءة ودرجة الثقة، ويفصل USB VID/PID عن Serial وADB transport ID وFastboot transport ID وApple Serial/UDID.", ModuleImplementationStatus.Implemented),
                new("identity-conflict-analysis", "كشف تعارض المعرّفات والبيانات", "Identity conflict analysis", "يقارن فقط المعرّفات المتطابقة في المنصة والنوع والقيمة؛ لا يربط جهازين بتشابه آخر أربعة أرقام أو قرب وقت التوصيل، ويضع اختلاف الموديل/الشركة تحت المراجعة.", ModuleImplementationStatus.Tested),
                new("smart-device-field-fill", "تعبئة حقول الشركة والموديل والوضع والمعرّف تلقائيًا", "Smart device-field population", "تُملأ حقول مركز الهوية من القراءة الفعلية المختارة مع إخفاء المعرّف افتراضيًا، ويمكن نسخ القيمة الدقيقة صراحةً. في Odin تُملأ عبارة FLASH من آخر أحرف Serial عبر ADB المصرّح فقط؛ تظل موافقات التوافق والنسخة الاحتياطية وCOM والتأكيد النهائي يدوية.", ModuleImplementationStatus.Implemented),
                new("identity-evidence-report", "تقرير أدلة الهوية المقنّع", "Masked identity evidence report", "ينسخ تقريرًا بمعلومات المصدر والوضع ودرجة الثقة والحالة مع إخفاء المعرّفات. لا يحفظ أرقامًا خامًا أو يرسلها إلى الشبكة.", ModuleImplementationStatus.Implemented),
                new("imei-serial-mutation", "تغيير أو تزوير IMEI أو الرقم التسلسلي", "IMEI or serial mutation", "غير مدعوم: لا توجد كتابة IMEI/Serial أو تعديل NVRAM/EFS/مناطق الخدمة أو تغيير هوية الجهاز. إصلاح الهوية المسموح به يكون عبر الشركة المصنّعة أو مركز خدمة معتمد.", ModuleImplementationStatus.Unsupported)
            ])
    ];

    public static IReadOnlySet<string> SupportedActionIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "RefreshDevices", "InspectDevice", "UpdateDeviceCatalog", "OpenUpdateCenter", "OpenScreenRecording", "OpenPhoneTests",
        "AndroidLogs", "InstallSamsungDriver", "InspectFirmware", "UsbTransport",
        "OpenWorkshop", "MirrorScreen", "OpenMultiScreenSessions", "OpenUsbDriverCenter", "AnalyzeScreen", "MeasurementRig",
        "RepairGuides", "OpenApkLab", "OpenNetworkDiagnostics", "OpenFastbootManager", "OpenAndroidAppManager", "OpenAndroidFileManager", "OpenAndroidCommandCenter", "OpenAndroidBatchTools", "OpenAndroidServiceOperations", "OpenPhoneBackup", "OpenAndroidWorkflow", "OpenWirelessAdbPairing", "OpenUsbDoctor", "OpenUsbDriverSignatureVerifier", "OpenQualcommUsbDiagnostics", "OpenMediaTekUsbDiagnostics", "OpenFirmwareCenter", "OpenDeviceHistory", "OpenNotificationCenter", "OpenDashboardAnalytics", "OpenSupportBundle", "OpenSupportCenter", "OpenHelpCenter", "OpenOfflineCenter", "OpenAbout", "OpenPartsInventory", "OpenSamsungDiagnostics", "OpenSamsungOdinAssistant", "OpenSamsungServiceOperations", "OpenSamsungOfficialTools", "OpenXiaomiFastbootService", "OpenXiaomiOfficialTools", "OpenBrandOfficialTools", "OpenQualcommRecovery", "OpenMediaTekRecovery", "OpenAndroidLogAnalyzer", "OpenAndroidBackupScheduler", "OpenXiaomiDiagnostics", "OpenBrandDiagnostics", "OpenDeviceCapabilityCenter", "OpenPluginStore", "OpenApplicationSettings", "OpenRepairAdvisor", "OpenOneClickDiagnostic", "ExportDeviceReport", "ConnectionHelp", "AgentHelp",
        "UpdateAgent", "SetupUsb", "ImportBoardMap", "OpenBoardView",
        "OpenWorkshopBackup", "OpenWorkshopBackupScheduler", "OpenGlobalWorkshopHistory",
        "OpenUniversalDeviceDiscovery", "OpenMaintenanceModeCenter", "OpenAirPlayReceiver", "OpenMiracastSetup", "OpenLockRecoveryAssistant", "OpenDiagnosticEvidenceHistory",
        "OpenIPhoneCenter", "OpenIPhonePartsHistory", "OpenIPhoneBatteryHealth", "OpenIPhoneFunctionalTests", "OpenIPhoneHdmiCapture", "OpenIPhoneAuthenticityEvidence",
        "OpenDriverPackageManager", "InspectUsbDriverPackage", "ListUsbDriverPackages", "InstallUsbDriverPackage", "ExportUsbDriverPackage", "RemoveUsbDriverPackage",
        "OpenDeviceIdentityIntegrity",
        "OpenGlobalWorkshopReports", "OpenGlobalWorkshopSettings", "OpenUserManagement", "OpenDeviceAccessRecoveryCenter"
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
