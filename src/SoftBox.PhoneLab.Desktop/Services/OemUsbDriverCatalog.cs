namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record OemUsbDriverResource(
    string Id,
    string Name,
    string OfficialUrl,
    string CoverageNote,
    string[] Aliases,
    string ResourceKind = "Official support portal");

/// <summary>
/// Official OEM USB-driver entry points listed by Android Developers.
/// These are manufacturer support/download portals, not a claim that one package covers every model.
/// </summary>
public static class OemUsbDriverCatalog
{
    public const string ReferenceUrl = "https://developer.android.com/studio/run/oem-usb";

    public static IReadOnlyList<OemUsbDriverResource> All { get; } = Array.AsReadOnly(
        new OemUsbDriverResource[]
        {
            new("acer", "Acer", "https://www.acer.com/worldwide/support/", "اختر موديل الجهاز ونظام Windows من صفحة دعم Acer.", ["acer"]),
            new("alcatel", "Alcatel Mobile", "https://www.alcatelmobile.com/support/", "صفحة دعم الشركة؛ قد يتطلب البحث برقم الموديل.", ["alcatel"]),
            new("asus", "ASUS", "https://www.asus.com/support/Download-Center/", "اختر موديل ASUS ثم Drivers & Tools.", ["asus"]),
            new("dell", "Dell", "https://www.dell.com/support/home/en-us", "بوابة دعم Dell الرسمية؛ اختر موديل الهاتف/الجهاز قبل تنزيل أي برنامج.", ["dell"]),
            new("intel", "Intel Android resources", "https://www.intel.com/software/android", "موارد Intel الخاصة بأجهزة Android المدعومة؛ ليست تعريفًا عامًا لكل الهواتف.", ["intel"]),
            new("blackberry", "BlackBerry", "https://swdownloads.blackberry.com/Downloads/entry.do?code=4EE0932F46276313B51570F46266A608", "بوابة تنزيل BlackBerry الرسمية؛ تحقق من توافق الحزمة مع موديلك.", ["blackberry"]),
            new("fcnt", "FCNT", "https://www.fcnt.com/support/develop/#anc-03", "موارد المطورين وتعريفات أجهزة FCNT المدعومة.", ["fcnt", "fujitsu"]),
            new("htc", "HTC", "https://www.htc.com/support/", "بوابة دعم HTC؛ قد تختلف ملفات USB حسب طراز الهاتف.", ["htc"]),
            new("huawei", "Huawei", "https://consumer.huawei.com/en/support/", "صفحة دعم Huawei؛ استخدم موديل الهاتف لاختيار HiSuite أو برنامج USB المناسب.", ["huawei"], "Official support portal / HiSuite tools"),
            new("kyocera", "Kyocera", "https://kyoceramobile.com/support/drivers/", "صفحة تعريفات Kyocera الرسمية؛ تحقق من الطراز والمنطقة.", ["kyocera"]),
            new("lenovo", "Lenovo", "https://support.lenovo.com/us/en/GlobalProductSelector", "اختر منتج Lenovo أو Motorola المدعوم ثم افتح Drivers & Software.", ["lenovo"]),
            new("lg", "LG", "https://www.lg.com/us/support/software-firmware", "صفحة دعم البرامج والـFirmware الرسمية؛ بعض موديلات الهواتف القديمة قد لا تتوفر لها حزم حديثة.", ["lg", "lge"]),
            new("mediatek", "MediaTek / MTK", "https://online.mediatek.com/apps/tool?id=83013128040570&action=download", "بوابة MediaTek Online الرسمية لأداة USB؛ قد تتطلب تسجيل الدخول واختيار المنتج، ويجب التحقق من توافق الحزمة مع موديل الجهاز.", ["mediatek", "mtk"], "OEM online tool portal (account may be required)"),
            new("motorola", "Motorola", "https://motorola-global-portal.custhelp.com/app/answers/detail/a_id/88481/", "صفحة Motorola الرسمية لتعريف USB/أدوات توصيل Windows.", ["motorola", "moto"], "Official Windows USB driver resource"),
            new("sharp", "Sharp", "https://k-tai.sharp.co.jp/support/", "بوابة Sharp اليابانية الرسمية؛ قد تكون معلومات الدعم باللغة اليابانية ومحددة للسوق.", ["sharp", "aquos"]),
            new("samsung", "Samsung", "https://developer.samsung.com/galaxy/others/android-usb-driver-for-windows", "صفحة تنزيل Samsung Android USB Driver for Windows؛ مثبت الحزمة موجود أيضًا في بناء Windows عند توفره.", ["samsung"], "Dedicated Windows Android USB driver"),
            new("sony", "Sony Mobile", "https://developer.sonymobile.com/downloads/drivers/", "مورد تعريفات Sony Mobile؛ قد تعتمد بعض الإصدارات على Sony Companion.", ["sony", "xperia"]),
            new("toshiba", "Toshiba", "https://support.toshiba.com/sscontent?docId=4001814", "بوابة دعم Toshiba؛ تحقق من توفر دعم للطراز قبل التنزيل.", ["toshiba"]),
            new("xiaomi", "Xiaomi / Redmi / POCO", "https://web.vip.miui.com/page/info/mio/mio/detail?postId=18464849&app_version=dev.20051", "صفحة Xiaomi المشار إليها من دليل Android OEM؛ يلزم التأكد من انطباق الحزمة على الموديل وإصدار Windows.", ["xiaomi", "redmi", "poco", "mi "]),
            new("zte", "ZTE", "https://www.zte.com.cn/global/support", "بوابة ZTE الرسمية؛ تحقق من توفر برنامج USB لموديلك ومنطقتك.", ["zte"]),
            new("google", "Google USB Driver", "https://developer.android.com/studio/run/win-usb", "تعريف Google USB مخصص لأجهزة Google/Nexus/Pixel؛ لا يُستخدم كبديل عام لتعريفات بقية الشركات.", ["google", "pixel", "nexus"], "Dedicated Windows USB driver"),
            new("apple", "Apple Devices for Windows", "https://support.apple.com/en-lamr/guide/devices-windows/mchl5ded2763/windows", "تثبيت Apple Devices عبر Microsoft Store يوفّر تكامل Apple مع Windows؛ هذا ليس تعريف Android ولا يتيح التحكم في iPhone.", ["apple", "iphone", "ipad"], "Apple Windows companion / USB support"),
            new("oneplus", "OnePlus", "https://service.oneplus.com/global", "بوابة OnePlus الرسمية؛ اختر المنتج والمنطقة للتحقق من أداة الكمبيوتر أو ملفات الدعم المتاحة.", ["oneplus"], "Official support portal"),
            new("oppo", "OPPO", "https://www.oppo.com/en/rom/", "صفحة OPPO الرسمية لبرامج وتحديثات الأجهزة؛ لا تعتبر رابطًا عامًا لتعريف ADB لكل موديلات OPPO.", ["oppo"], "Official Windows utility / software portal"),
            new("realme", "realme", "https://www.realme.com/eg/support/software-update", "صفحة realme الإقليمية لأدوات تحديث النظام؛ تحقق من الموديل والمنطقة قبل الاستخدام.", ["realme"], "Regional Windows update utility"),
            new("vivo", "vivo", "https://www.vivo.com/in/support/upgradePackageData?id=57", "صفحة vivo الرسمية لحزمة MTP Driver على Windows في السوق الهندي؛ لا تثبت أنها تعريف ADB لكل موديلات vivo.", ["vivo", "iqoo"], "Official MTP driver download (regional)"),
            new("honor", "HONOR", "https://www.honor.com/global/tech/honor-suite/", "HONOR Suite أداة كمبيوتر رسمية؛ اختيار الأدوات والتوافق مرتبط بالطراز والمنطقة.", ["honor"], "Official Windows companion utility"),
            new("tecno", "TECNO", "https://www.tecno-mobile.com/support/", "بوابة TECNO الرسمية للدعم والتنزيلات؛ راجع الموديل والسوق قبل تنزيل أي أداة.", ["tecno"], "Official support portal"),
            new("infinix", "Infinix", "https://battery.infinixmobility.com/care/support", "بوابة Infinix الرسمية للدعم؛ توفر تعريف أو أداة يعتمد على الموديل والمنطقة.", ["infinix"], "Official support portal"),
            new("nothing", "Nothing", "https://support.nothing.tech/hc/en-us", "مركز دعم Nothing الرسمي؛ لا تُفترض حزمة USB عامة لكل الهواتف.", ["nothing"], "Official support portal"),
            new("hmd", "HMD / Nokia", "https://www.hmd.com/en_eg/support/", "بوابة HMD الرسمية للدعم في مصر؛ اختر موديل Nokia/HMD لعرض الملفات المتاحة.", ["hmd", "nokia"], "Official support portal"),
            new("tcl", "TCL Mobile", "https://support.tcl.com/en_US/us-mobile-product-downloads", "بوابة تنزيلات TCL Mobile؛ التوفر مقيد بالموديل والسوق.", ["tcl", "alcatel tcl"], "Official mobile downloads portal"),
            new("fairphone", "Fairphone", "https://support.fairphone.com/hc/en-us/articles/18896094650513-How-to-manually-install-Android-on-your-Fairphone", "تعليمات Fairphone الرسمية لحزم النظام والتثبيت اليدوي؛ ليست تعريف USB عامًا.", ["fairphone"], "Official OS / software package guidance")
        });

    private static readonly IReadOnlyDictionary<string, string> VendorIdToResourceId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["0502"] = "acer",
            ["05AC"] = "apple",
            ["1914"] = "alcatel",
            ["0B05"] = "asus",
            ["413C"] = "dell",
            ["04C5"] = "fcnt",
            ["0BB4"] = "htc",
            ["12D1"] = "huawei",
            ["8087"] = "intel",
            ["0482"] = "kyocera",
            ["17EF"] = "lenovo",
            ["2006"] = "lenovo",
            ["1004"] = "lg",
            ["22B8"] = "motorola",
            ["0E8D"] = "mediatek",
            ["04E8"] = "samsung",
            ["04DD"] = "sharp",
            ["054C"] = "sony",
            ["0FCE"] = "sony",
            ["0930"] = "toshiba",
            ["2717"] = "xiaomi",
            ["22D9"] = "oppo",
            ["19D2"] = "zte",
            ["18D1"] = "google"
        };

    public static OemUsbDriverResource? FindForManufacturer(string? manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer)) return null;
        var value = manufacturer.Trim().ToLowerInvariant();
        return All.FirstOrDefault(resource =>
            resource.Aliases.Any(alias => value.Contains(alias, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Identifies a likely OEM support page using Windows PnP manufacturer/name first,
    /// then the Android Open Source Project ADB USB vendor identifiers.
    /// A VID suggests a USB vendor only; it does not prove the exact phone model or driver compatibility.
    /// </summary>
    public static OemUsbDriverResource? FindForUsbIdentity(
        string? manufacturer,
        string? friendlyName,
        string? vendorId)
    {
        var byManufacturer = FindForManufacturer(manufacturer);
        if (byManufacturer is not null) return byManufacturer;

        var byName = FindForManufacturer(friendlyName);
        if (byName is not null) return byName;

        var normalizedVendorId = vendorId?.Trim();
        if (normalizedVendorId?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true)
            normalizedVendorId = normalizedVendorId[2..];

        if (normalizedVendorId is null || !VendorIdToResourceId.TryGetValue(normalizedVendorId, out var resourceId))
            return null;

        return All.FirstOrDefault(resource => resource.Id.Equals(resourceId, StringComparison.OrdinalIgnoreCase));
    }
}
