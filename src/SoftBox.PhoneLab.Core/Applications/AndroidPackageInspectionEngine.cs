using System.Globalization;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Applications;

public enum AndroidPermissionRisk
{
    DangerousRuntime,
    SpecialAccess,
    Normal,
    Unknown
}

public sealed record AndroidPackagePermission(
    string Name,
    AndroidPermissionRisk Risk,
    bool? Granted,
    string Category);

public sealed record AndroidPackageInspectionReport(
    string PackageName,
    long? VersionCode,
    string? VersionName,
    int? MinSdkVersion,
    int? TargetSdkVersion,
    int? Uid,
    string? CodePath,
    string? DataDirectory,
    string? FirstInstallTime,
    string? LastUpdateTime,
    bool? InstalledForUser,
    bool? HiddenForUser,
    bool? SuspendedForUser,
    int? EnabledStateForUser,
    IReadOnlyList<AndroidPackagePermission> Permissions,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Parses one package's read-only "dumpsys package" output into explicit metadata and
/// permission evidence. It does not decide whether an app is malicious or whether a permission
/// is actually being used at runtime.
/// </summary>
public static class AndroidPackageInspectionEngine
{
    public const int MaximumInputCharacters = 2 * 1024 * 1024;

    private static readonly Regex PackageLine = new(
        @"^\s*Package\s+\[(?<name>[A-Za-z0-9_.]+)\]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex VersionCodeLine = new(
        @"\bversionCode=(?<value>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex VersionNameLine = new(
        @"\bversionName=(?<value>[^\s]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TargetSdkLine = new(
        @"\btargetSdk=(?<value>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MinSdkLine = new(
        @"\bminSdk=(?<value>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UidLine = new(
        @"\buserId=(?<value>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CodePathLine = new(
        @"^\s*codePath=(?<value>\S+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DataDirectoryLine = new(
        @"^\s*(?:dataDir|dataDirectory)=(?<value>\S+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FirstInstallLine = new(
        @"^\s*firstInstallTime=(?<value>.+?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LastUpdateLine = new(
        @"^\s*lastUpdateTime=(?<value>.+?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UserStateLine = new(
        @"^\s*User\s+\d+:\s*(?<value>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PermissionLine = new(
        @"(?<name>android\.permission\.[A-Za-z0-9_.]+)(?:\s*:\s*granted=(?<granted>true|false))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BooleanField = new(
        @"\b(?<name>installed|hidden|suspended|stopped|notLaunched|instant)=?(?<value>true|false)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EnabledField = new(
        @"\benabled=(?<value>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> DangerousPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "android.permission.CAMERA",
        "android.permission.RECORD_AUDIO",
        "android.permission.READ_CONTACTS",
        "android.permission.WRITE_CONTACTS",
        "android.permission.GET_ACCOUNTS",
        "android.permission.ACCESS_FINE_LOCATION",
        "android.permission.ACCESS_COARSE_LOCATION",
        "android.permission.ACCESS_BACKGROUND_LOCATION",
        "android.permission.READ_PHONE_STATE",
        "android.permission.READ_CALL_LOG",
        "android.permission.WRITE_CALL_LOG",
        "android.permission.CALL_PHONE",
        "android.permission.ADD_VOICEMAIL",
        "android.permission.USE_SIP",
        "android.permission.PROCESS_OUTGOING_CALLS",
        "android.permission.READ_SMS",
        "android.permission.RECEIVE_SMS",
        "android.permission.SEND_SMS",
        "android.permission.RECEIVE_MMS",
        "android.permission.READ_EXTERNAL_STORAGE",
        "android.permission.WRITE_EXTERNAL_STORAGE",
        "android.permission.READ_MEDIA_AUDIO",
        "android.permission.READ_MEDIA_IMAGES",
        "android.permission.READ_MEDIA_VIDEO",
        "android.permission.READ_MEDIA_VISUAL_USER_SELECTED",
        "android.permission.POST_NOTIFICATIONS",
        "android.permission.BODY_SENSORS",
        "android.permission.BODY_SENSORS_BACKGROUND",
        "android.permission.ACTIVITY_RECOGNITION",
        "android.permission.BLUETOOTH_SCAN",
        "android.permission.BLUETOOTH_CONNECT",
        "android.permission.BLUETOOTH_ADVERTISE",
        "android.permission.NEARBY_WIFI_DEVICES"
    };

    private static readonly HashSet<string> SpecialPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "android.permission.SYSTEM_ALERT_WINDOW",
        "android.permission.WRITE_SETTINGS",
        "android.permission.REQUEST_INSTALL_PACKAGES",
        "android.permission.MANAGE_EXTERNAL_STORAGE",
        "android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS",
        "android.permission.SCHEDULE_EXACT_ALARM",
        "android.permission.PACKAGE_USAGE_STATS",
        "android.permission.BIND_ACCESSIBILITY_SERVICE",
        "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE"
    };

    private static readonly HashSet<string> NormalPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "android.permission.INTERNET",
        "android.permission.ACCESS_NETWORK_STATE",
        "android.permission.ACCESS_WIFI_STATE",
        "android.permission.CHANGE_WIFI_STATE",
        "android.permission.WAKE_LOCK",
        "android.permission.VIBRATE",
        "android.permission.RECEIVE_BOOT_COMPLETED",
        "android.permission.FOREGROUND_SERVICE",
        "android.permission.FOREGROUND_SERVICE_DATA_SYNC",
        "android.permission.NFC",
        "android.permission.SET_WALLPAPER",
        "android.permission.QUERY_ALL_PACKAGES",
        "android.permission.BLUETOOTH",
        "android.permission.BLUETOOTH_ADMIN"
    };

    public static AndroidPackageInspectionReport Parse(
        string expectedPackageName,
        string? dumpsysOutput)
    {
        if (!IsSafePackageName(expectedPackageName))
            throw new ArgumentException("اسم حزمة Android غير صالح.", nameof(expectedPackageName));

        var raw = dumpsysOutput ?? "";
        var truncated = raw.Length > MaximumInputCharacters;
        if (truncated)
            raw = raw[..MaximumInputCharacters];

        string? observedPackageName = null;
        long? versionCode = null;
        string? versionName = null;
        int? minSdk = null;
        int? targetSdk = null;
        int? uid = null;
        string? codePath = null;
        string? dataDirectory = null;
        string? firstInstall = null;
        string? lastUpdate = null;
        bool? installed = null;
        bool? hidden = null;
        bool? suspended = null;
        int? enabledState = null;
        var permissionGrants = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var permissionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var packageMatch = PackageLine.Match(line);
            if (packageMatch.Success)
                observedPackageName ??= packageMatch.Groups["name"].Value;

            versionCode ??= MatchLong(VersionCodeLine, line);
            versionName ??= MatchString(VersionNameLine, line);
            minSdk ??= MatchInt(MinSdkLine, line);
            targetSdk ??= MatchInt(TargetSdkLine, line);
            uid ??= MatchInt(UidLine, line);
            codePath ??= MatchString(CodePathLine, line);
            dataDirectory ??= MatchString(DataDirectoryLine, line);
            firstInstall ??= MatchString(FirstInstallLine, line);
            lastUpdate ??= MatchString(LastUpdateLine, line);

            var userMatch = UserStateLine.Match(line);
            if (userMatch.Success)
            {
                var userText = userMatch.Groups["value"].Value;
                installed ??= MatchBoolean(userText, "installed");
                hidden ??= MatchBoolean(userText, "hidden");
                suspended ??= MatchBoolean(userText, "suspended");
                enabledState ??= MatchInt(EnabledField, userText);
            }

            foreach (Match permissionMatch in PermissionLine.Matches(line))
            {
                var name = permissionMatch.Groups["name"].Value;
                if (!IsSafePermissionName(name))
                    continue;
                permissionNames.Add(name);

                if (permissionMatch.Groups["granted"].Success &&
                    bool.TryParse(permissionMatch.Groups["granted"].Value, out var granted))
                    permissionGrants[name] = granted;
            }
        }

        if (observedPackageName is not null &&
            !observedPackageName.Equals(expectedPackageName, StringComparison.Ordinal))
        {
            warnings.Add($"اسم الحزمة في مخرجات dumpsys ({observedPackageName}) لا يطابق الحزمة المختارة ({expectedPackageName}).");
        }
        else if (observedPackageName is null)
        {
            warnings.Add("لم يظهر Package header واضح في المخرجات؛ تم الاحتفاظ باسم الحزمة الذي طلبه المستخدم كمفتاح للتقرير.");
        }

        if (versionCode is null)
            warnings.Add("Version Code غير متاح في المخرجات.");
        if (targetSdk is null)
            warnings.Add("Target SDK غير متاح؛ لا يتم استنتاجه من إصدار Android على الهاتف.");
        if (installed is null)
            warnings.Add("لم يتم العثور على حالة تثبيت واضحة للمستخدم.");
        if (truncated)
            warnings.Add($"تم تقليص dumpsys package إلى أول {MaximumInputCharacters:N0} حرف بسبب حد القراءة.");

        var permissions = permissionNames
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                var classification = ClassifyPermission(name);
                var granted = permissionGrants.TryGetValue(name, out var value)
                    ? value
                    : (bool?)null;
                return new AndroidPackagePermission(
                    name,
                    classification.Risk,
                    granted,
                    classification.Category);
            })
            .ToArray();

        return new AndroidPackageInspectionReport(
            expectedPackageName,
            versionCode,
            versionName,
            minSdk,
            targetSdk,
            uid,
            codePath,
            dataDirectory,
            firstInstall,
            lastUpdate,
            installed,
            hidden,
            suspended,
            enabledState,
            permissions,
            warnings);
    }

    public static (AndroidPermissionRisk Risk, string Category) ClassifyPermission(string permissionName)
    {
        if (DangerousPermissions.Contains(permissionName))
            return (AndroidPermissionRisk.DangerousRuntime, "صلاحية حساسة/وقت تشغيل");
        if (SpecialPermissions.Contains(permissionName))
            return (AndroidPermissionRisk.SpecialAccess, "وصول خاص للنظام");
        if (NormalPermissions.Contains(permissionName))
            return (AndroidPermissionRisk.Normal, "صلاحية عادية معروفة");
        return (AndroidPermissionRisk.Unknown, "صلاحية غير مصنفة في قاعدة SoftBox");
    }

    public static bool IsSafePackageName(string? packageName)
        => !string.IsNullOrWhiteSpace(packageName) &&
           packageName.Length <= 255 &&
           Regex.IsMatch(packageName, @"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)+$", RegexOptions.CultureInvariant);

    private static bool IsSafePermissionName(string name)
        => name.Length <= 255 &&
           Regex.IsMatch(name, @"^android\.permission\.[A-Za-z0-9_.]+$", RegexOptions.CultureInvariant);

    private static string? MatchString(Regex regex, string line)
    {
        var match = regex.Match(line);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static int? MatchInt(Regex regex, string line)
        => int.TryParse(MatchString(regex, line), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static long? MatchLong(Regex regex, string line)
        => long.TryParse(MatchString(regex, line), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool? MatchBoolean(string input, string field)
    {
        var match = Regex.Match(
            input,
            $@"\b{Regex.Escape(field)}=(?<value>true|false)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && bool.TryParse(match.Groups["value"].Value, out var value)
            ? value
            : null;
    }
}
