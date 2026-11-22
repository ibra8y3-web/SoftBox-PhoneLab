using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Lightweight runtime localization for the desktop shell, sign-in flow,
/// application preferences and common labels shared by tool windows.
/// Unmapped specialist text is deliberately left unchanged rather than mistranslated.
/// </summary>
internal static class SoftBoxLocalization
{
    private static readonly IReadOnlyDictionary<string, string> ArabicToEnglish =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SoftBox | إعداد المدير الأول"] = "SoftBox | First Administrator Setup",
            ["SoftBox | تسجيل الدخول"] = "SoftBox | Sign In",
            ["إعداد الحساب الإداري الأول"] = "First Administrator Setup",
            ["حساب المستخدم المحلي"] = "Local User Account",
            ["لا يوجد حساب في هذا التثبيت بعد. أنشئ حساب المدير بنفسك؛ لا توجد كلمة مرور افتراضية."] = "No account exists on this installation yet. Create your own administrator account; no default password is provided.",
            ["أدخل بيانات حساب SoftBox المحلي للمتابعة. الحسابات المعطلة أو المقفلة مؤقتًا لن تتمكن من الدخول."] = "Enter your local SoftBox account details to continue. Disabled or temporarily locked accounts cannot sign in.",
            ["اسم المدير الظاهر"] = "Administrator Display Name",
            ["مثال: مدير مركز الصيانة"] = "Example: Workshop Administrator",
            ["اسم المستخدم"] = "Username",
            ["admin أو اسم عشوائي"] = "admin or a random name",
            ["كلمة المرور"] = "Password",
            ["تأكيد كلمة المرور"] = "Confirm Password",
            ["توليد بيانات عشوائية لهذا التثبيت"] = "Generate Random Credentials for This Installation",
            ["نسخ بيانات الدخول"] = "Copy Sign-In Details",
            ["إنشاء حساب المدير والدخول"] = "Create Administrator Account & Sign In",
            ["تسجيل الدخول إلى SoftBox"] = "Sign In to SoftBox",
            ["سجّل الدخول لفتح مساحة العمل."] = "Sign in to open the workspace.",
            ["نسيت كلمة المرور؟ استرداد بموافقة دعم موقعة"] = "Forgot your password? Recover with a signed support approval",
            ["خروج"] = "Exit",
            ["تم تصفير الحسابات أو لم يتم إنشاء حساب بعد. أنشئ حساب المدير الجديد الآن. يمكنك توليد اسم مستخدم وكلمة مرور عشوائيين ثم نسخهما وحفظهما. لا توجد كلمة مرور افتراضية."] = "Accounts have been reset or no account exists yet. Create the new administrator now. You can generate a random username and password, then copy and save them. No default password is provided.",
            ["إنشاء اسم مستخدم وكلمة مرور عشوائيين"] = "Generate a random username and password",
            ["إنشاء حساب المدير الجديد: اكتب بياناتك بنفسك أو ولّد بيانات عشوائية ثم انسخها قبل تسجيل الدخول."] = "Create the new administrator account: enter your own details or generate random credentials and copy them before signing in.",
            ["أدخل بيانات حساب SoftBox المحلي للمتابعة. إذا أكملت استرداد كلمة المرور للتو، استخدم كلمة المرور الجديدة وليس القديمة."] = "Enter your local SoftBox account details. After password recovery, use the new password, not the old one.",
            ["تم استرداد الحساب بنجاح. أدخل كلمة المرور الجديدة التي اخترتها في شاشة الاسترداد، ثم اضغط تسجيل الدخول. لا تستخدم كلمة المرور القديمة."] = "Password recovery succeeded. Enter the new password you chose in the recovery screen, then sign in. Do not use the old password.",
            ["سيتم حفظ الحساب محليًا في ملف حماية SoftBox."] = "Your account will be stored locally in protected SoftBox storage.",
            ["الحد الأدنى"] = "Minimum",
            ["الإعدادات"] = "Settings",
            ["SoftBox | إعدادات التطبيق"] = "SoftBox | Application Settings",
            ["إعدادات SoftBox"] = "SoftBox Settings",
            ["LOCAL USER PREFERENCES • DEVICE AUTOMATION • SAFE CONCURRENCY"] = "LOCAL USER PREFERENCES • DEVICE AUTOMATION • SAFE CONCURRENCY",
            ["اكتشاف الأجهزة تلقائيًا أثناء تشغيل SoftBox"] = "Automatically discover devices while SoftBox is running",
            ["فترة اكتشاف الأجهزة (ثانية)"] = "Device discovery interval (seconds)",
            ["فتح عرض شاشة الهاتف تلقائيًا عند الاتصال"] = "Automatically open the phone screen when connected",
            ["تثبيت / تجهيز PhoneLab Agent تلقائيًا بعد تصريح ADB"] = "Automatically install / prepare PhoneLab Agent after ADB authorization",
            ["قراءة معلومات الجهاز تلقائيًا بعد الاتصال"] = "Automatically inspect device information after connection",
            ["الحد الأقصى للأجهزة المتزامنة في Batch"] = "Maximum concurrent devices in a batch",
            ["تكبير / تصغير واجهة الكمبيوتر"] = "Desktop interface scale",
            ["لغة التطبيق"] = "Application Language",
            ["حفظ وتطبيق الإعدادات"] = "Save & Apply Settings",
            ["الإعدادات الحالية لم تتغير."] = "Current settings have not changed.",
            ["تم حفظ الإعدادات وتطبيقها."] = "Settings saved and applied.",
            ["تعذر حفظ الإعدادات: "] = "Could not save settings: ",
            ["هذه الإعدادات تتحكم في سلوك SoftBox المحلي فقط. إيقاف تثبيت Agent تلقائيًا لا يمنع التثبيت اليدوي عند طلبك، وإيقاف الاكتشاف التلقائي لا يمنع استخدام زر تحديث الأجهزة. لا يتم تخزين كلمات مرور أو مفاتيح."] = "These preferences control local SoftBox behavior only. Disabling automatic Agent installation does not prevent manual installation, and disabling automatic discovery does not prevent manual device refresh. Passwords and keys are not stored here.",
            ["SoftBox | Universal Smart Device Service & Repair Platform"] = "SoftBox | Universal Smart Device Service & Repair Platform",
            ["حجم الواجهة"] = "Interface Scale",
            ["تصغير الواجهة"] = "Decrease interface scale",
            ["تكبير الواجهة"] = "Increase interface scale",
            ["المستخدم الحالي"] = "Current User",
            ["قفل الجلسة"] = "Lock Session",
            ["أقسام SoftBox"] = "SoftBox Modules",
            ["36 قسمًا • كل قسم يفتح داخل نفس الواجهة"] = "36 modules • each opens inside the same workspace",
            ["اختر قسمًا لعرض أدواته وحالة تنفيذها"] = "Select a module to view its tools and implementation status",
            ["الأجهزة المتصلة"] = "Connected Devices",
            ["USB / ADB • اكتشاف دوري"] = "USB / ADB • periodic discovery",
            ["مكونات الهاتف"] = "Phone Components",
            ["اختر القطعة لرؤية الأدلة"] = "Select a component to view evidence",
            ["المجسم ثلاثي الأبعاد"] = "3D Model",
            ["اسحب للتدوير • عجلة الماوس للتكبير • اختر القطعة"] = "Drag to rotate • scroll to zoom • select a component",
            ["لا يوجد هاتف متصل"] = "No phone connected",
            ["بانتظار جهاز Android عبر USB"] = "Waiting for an Android device over USB",
            ["01–03 | الرئيسية والأجهزة"] = "01–03 | Home & Devices",
            ["04–05 | Android والتطبيقات"] = "04–05 | Android & Apps",
            ["06–10 | الشركات والمعالجات"] = "06–10 | Manufacturers & Chipsets",
            ["11–14 | Firmware والاتصال والنسخ"] = "11–14 | Firmware, Connectivity & Backup",
            ["15–19 | الفحص والذكاء والاستعادة"] = "15–19 | Diagnostics, Intelligence & Recovery",
            ["20–25 | الأتمتة وإدارة الصيانة"] = "20–25 | Automation & Workshop Management",
            ["26–28 | الإضافات والعمل دون إنترنت"] = "26–28 | Plugins & Offline Work",
            ["29–36 | النظام والحماية والدعم"] = "29–36 | System, Security & Support",
            ["01  لوحة التحكم — Dashboard"] = "01  Dashboard",
            ["02  مركز الأجهزة — Device Center"] = "02  Device Center",
            ["03  ذكاء الأجهزة — Device Intelligence"] = "03  Device Intelligence",
            ["04  مركز صيانة Android"] = "04  Android Service Center",
            ["05  مختبر APK — APK Lab"] = "05  APK Lab",
            ["06  أدوات Samsung"] = "06  Samsung Tools",
            ["07  أدوات Xiaomi / Redmi / POCO"] = "07  Xiaomi / Redmi / POCO Tools",
            ["08  شركات Android الأخرى"] = "08  Other Android Brands",
            ["09  أدوات Qualcomm"] = "09  Qualcomm Tools",
            ["10  أدوات MediaTek"] = "10  MediaTek Tools",
            ["11  مركز Firmware"] = "11  Firmware Center",
            ["12  طبيب USB — USB Doctor"] = "12  USB Doctor",
            ["13  النسخ الاحتياطي الذكي"] = "13  Smart Backup",
            ["14  استوديو الهاتف — Phone Link"] = "14  Phone Link Studio",
            ["15  تشخيص العتاد"] = "15  Hardware Diagnostics",
            ["16  تشخيص الشبكات"] = "16  Network Diagnostics",
            ["17  مساعد الإصلاح الذكي"] = "17  Smart Repair Assistant",
            ["18  الفحص الشامل بزر واحد"] = "18  One-Click Diagnostics",
            ["19  الاستعادة الآمنة"] = "19  Safe Recovery",
            ["20  أتمتة سير العمل"] = "20  Workflow Automation",
            ["21  العمليات الجماعية"] = "21  Batch Operations",
            ["22  سجل الصيانة"] = "22  Repair History",
            ["23  إدارة مركز الصيانة"] = "23  Workshop Management",
            ["24  مخزون قطع الغيار"] = "24  Parts Inventory",
            ["25  تقارير الخدمة"] = "25  Service Reports",
            ["26  متجر الإضافات"] = "26  Plugin Store",
            ["27  Plugin SDK للمطورين"] = "27  Plugin SDK",
            ["28  مركز Offline"] = "28  Offline Center",
            ["29  الأمان والخصوصية"] = "29  Security & Privacy",
            ["30  المستخدمون والفنيون"] = "30  Users & Technicians",
            ["31  مركز التحديثات"] = "31  Update Center",
            ["32  مركز الإشعارات"] = "32  Notification Center",
            ["33  مركز المساعدة"] = "33  Help Center",
            ["34  الدعم والسجلات"] = "34  Support & Logs",
            ["35  الإعدادات"] = "35  Settings",
            ["36  حول SoftBox"] = "36  About SoftBox",
            ["تم تغيير لغة التطبيق إلى العربية."] = "Application language set to Arabic.",
            ["تبديل العربية والإنجليزية"] = "Switch Arabic / English",
            ["معلومات الجهاز"] = "Device Information",
            ["تصدير تقرير PhoneLab"] = "Export PhoneLab Report",
            ["حفظ تقرير PhoneLab"] = "Save PhoneLab Report",
            ["تم حفظ حجم واجهة الكمبيوتر."] = "Desktop interface scale saved.",
            ["العربية"] = "Arabic",
            ["English"] = "الإنجليزية",
            ["تعذر حفظ حجم الواجهة: "] = "Could not save interface scale: ",
            ["اختر قطعة من المجسم"] = "Select a component from the model",
            ["لم تختبر"] = "Not tested",
            ["غير متاح"] = "Unavailable",
            ["لا توجد أدلة اختبار بعد."] = "No test evidence yet.",
            ["طلب الدعم الوارد"] = "Incoming Support Request",
            ["استيراد ملف JSON"] = "Import JSON File",
            ["مراجعة وإصدار الموافقة"] = "Review & Issue Approval",
            ["نوع الإجراء"] = "Action",
            ["مدة الصلاحية"] = "Validity Period",
            ["توقيع الموافقة وتصديرها"] = "Sign & Export Approval",
            ["تنبيه: WhatsApp لا يُدار آليًا. استلم الطلب يدويًا، راجعه، ثم أرسل الموافقة للعميل بنفسك."] = "Notice: WhatsApp is not automated. Receive and review the request manually, then send the approval to the customer yourself."
        };

    private static readonly IReadOnlyDictionary<string, string> EnglishToArabic =
        ArabicToEnglish.GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

    private static bool _installed;

    public static string CurrentLanguage { get; private set; } = "ar";

    public static void Install()
    {
        if (_installed) return;
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window) Apply(window);
            }),
            handledEventsToo: true);
        _installed = true;
    }

    public static void SetLanguage(string language)
    {
        CurrentLanguage = language is "ar" or "en" ? language : "ar";

        if (Application.Current is null) return;
        foreach (Window window in Application.Current.Windows)
            Apply(window);
    }

    public static string Translate(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var dictionary = CurrentLanguage == "en" ? ArabicToEnglish : EnglishToArabic;
        return dictionary.TryGetValue(value, out var translated) ? translated : value;
    }

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.FlowDirection = CurrentLanguage == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.Title = Translate(window.Title);
        if (window.Content is DependencyObject content)
            ApplyTree(content);
    }

    /// <summary>Translates a UI subtree that was moved out of its original window (embedded pages).</summary>
    public static void ApplyTo(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        ApplyTree(root);
    }

    private static void ApplyTree(DependencyObject node)
    {
        if (node is TextBlock textBlock &&
            !BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty))
        {
            textBlock.Text = Translate(textBlock.Text);
        }

        if (node is ContentControl contentControl &&
            !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty) &&
            contentControl.Content is string content)
        {
            contentControl.Content = Translate(content);
        }

        if (node is HeaderedContentControl headeredContentControl &&
            !BindingOperations.IsDataBound(headeredContentControl, HeaderedContentControl.HeaderProperty) &&
            headeredContentControl.Header is string header)
        {
            headeredContentControl.Header = Translate(header);
        }

        if (node is HeaderedItemsControl headeredItemsControl &&
            !BindingOperations.IsDataBound(headeredItemsControl, HeaderedItemsControl.HeaderProperty) &&
            headeredItemsControl.Header is string itemsHeader)
        {
            headeredItemsControl.Header = Translate(itemsHeader);
        }

        if (node is FrameworkElement element && element.ToolTip is string toolTip)
            element.ToolTip = Translate(toolTip);

        foreach (var child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is DependencyObject dependencyChild)
                ApplyTree(dependencyChild);
        }
    }
}
