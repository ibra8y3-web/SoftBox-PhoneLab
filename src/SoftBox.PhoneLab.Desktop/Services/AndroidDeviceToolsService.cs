using System.IO;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using SoftBox.PhoneLab.Core.Apk;
using SoftBox.PhoneLab.Core.Android;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record AndroidPackageInfo(string PackageName);
public enum AndroidRebootTarget { System, Recovery, Bootloader }
public sealed record AndroidSplitApkFile(string FullPath, string FileName, string? SplitName, string Sha256);
public sealed record AndroidSplitApkInstallPlan(
    string PackageName,
    string? VersionName,
    long VersionCode,
    IReadOnlyList<AndroidSplitApkFile> Files);
public sealed record AndroidBatchResult(string DeviceSerial, bool Success, string Output, string? Error);
public enum AndroidVendorKind { Generic, Samsung, Xiaomi }
public sealed record AndroidVendorDiagnostics(
    AndroidVendorKind Vendor,
    DateTimeOffset CollectedAtUtc,
    IReadOnlyDictionary<string, string> AvailableProperties,
    IReadOnlyList<string> UnavailableProperties);

/// <summary>
/// Authorized ADB operations for public shared storage, package inspection and allow-listed commands.
/// Third-party package removal is limited to packages revalidated through pm list packages -3.
/// </summary>
public sealed class AndroidDeviceToolsService(IAdbCommandRunner adb)
{
    private readonly ApkInspectionService _apkInspector = new();
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromHours(2);
    private static readonly IReadOnlyDictionary<string, string[]> ReadOnlyPresets =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["properties"] = ["shell", "getprop"],
            ["battery"] = ["shell", "dumpsys", "battery"],
            ["storage"] = ["shell", "df", "-h", "/storage/emulated/0"],
            ["uptime"] = ["shell", "cat", "/proc/uptime"],
            ["third-party-apps"] = ["shell", "pm", "list", "packages", "-3"],
            ["services"] = ["shell", "service", "list"]
        };

    public static IReadOnlyList<string> PresetIds => ReadOnlyPresets.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();

    public async Task<string> ListDirectoryAsync(
        string serial,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        ValidatePublicStoragePath(remotePath);
        return await RunDeviceAsync(serial,
            ["shell", "ls", "-la", "--", QuoteForRemoteShell(remotePath)],
            cancellationToken);
    }

    public async Task PullFileAsync(
        string serial,
        string remoteFilePath,
        string localFilePath,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        ValidatePublicStoragePath(remoteFilePath);
        if (string.IsNullOrWhiteSpace(localFilePath))
            throw new ArgumentException("حدد مسار حفظ محلي.", nameof(localFilePath));

        var localPath = Path.GetFullPath(localFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await RunDeviceAsync(serial,
            ["pull", remoteFilePath, localPath],
            cancellationToken, TransferTimeout);

        if (!File.Exists(localPath) && !Directory.Exists(localPath))
            throw new IOException("انتهى ADB دون إنشاء الملف أو المجلد المحلي المتوقع.");
    }

    public async Task PushFileAsync(
        string serial,
        string localFilePath,
        string remoteDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        ValidatePublicStoragePath(remoteDirectory);
        if (!File.Exists(localFilePath))
            throw new FileNotFoundException("الملف المحلي المحدد غير موجود.", localFilePath);

        await RunDeviceAsync(serial,
            ["push", Path.GetFullPath(localFilePath), remoteDirectory],
            cancellationToken, TransferTimeout);
    }

    public async Task CreateDirectoryAsync(
        string serial,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        ValidatePublicStoragePath(remotePath);
        await RunDeviceAsync(serial,
            ["shell", "mkdir", "-p", "--", QuoteForRemoteShell(remotePath)],
            cancellationToken);
    }

    public async Task<IReadOnlyList<AndroidPackageInfo>> ListThirdPartyPackagesAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        var output = await RunDeviceAsync(serial, ["shell", "pm", "list", "packages", "-3"], cancellationToken);
        return ParsePackageList(output);
    }

    public async Task<string> GetPackageDetailsAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        if (!IsSafePackageName(packageName))
            throw new ArgumentException("اسم حزمة Android غير صالح.", nameof(packageName));

        return await RunDeviceAsync(serial,
            ["shell", "dumpsys", "package", packageName],
            cancellationToken);
    }

    public async Task<string> InstallApkAsync(
        string serial,
        string apkFilePath,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        if (string.IsNullOrWhiteSpace(apkFilePath) || !File.Exists(apkFilePath))
            throw new FileNotFoundException("ملف APK المحلي غير موجود.", apkFilePath);
        if (!Path.GetExtension(apkFilePath).Equals(".apk", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("حدد ملف APK بامتداد صحيح.", nameof(apkFilePath));

        return await RunDeviceAsync(
            serial,
            ["install", "-r", Path.GetFullPath(apkFilePath)],
            cancellationToken,
            TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// Inspects a local base APK and its configuration/feature splits before any device write.
    /// A valid set has exactly one base manifest, one or more uniquely named splits, one package
    /// name and one shared version code.
    /// </summary>
    public async Task<AndroidSplitApkInstallPlan> PrepareSplitApkInstallAsync(
        IReadOnlyList<string> apkFilePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apkFilePaths);
        if (apkFilePaths.Count is < 2 or > 32)
            throw new ArgumentException("حدد ملف APK أساسيًا وملف Split واحدًا على الأقل، وبحد أقصى 32 ملفًا.", nameof(apkFilePaths));

        var fullPaths = apkFilePaths
            .Select(path => string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("مسار APK فارغ.", nameof(apkFilePaths))
                : Path.GetFullPath(path))
            .ToArray();

        if (fullPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fullPaths.Length)
            throw new InvalidDataException("تم اختيار ملف APK نفسه أكثر من مرة.");

        var inspected = new List<(string Path, ApkInspectionResult Result)>(fullPaths.Length);
        foreach (var path in fullPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Path.GetExtension(path).Equals(".apk", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("كل الملفات المحددة يجب أن تكون APK.", nameof(apkFilePaths));
            if (!File.Exists(path))
                throw new FileNotFoundException("ملف APK محلي غير موجود.", path);

            var result = await _apkInspector.InspectAsync(path, cancellationToken);
            if (!result.ArchiveEntriesReadable ||
                result.ManifestFormat == ApkManifestFormat.Unavailable ||
                !IsSafePackageName(result.PackageName) ||
                result.VersionCode is null)
                throw new InvalidDataException(
                    "تعذر التحقق من قراءة ZIP واسم الحزمة وVersion Code من Manifest للملف: " + Path.GetFileName(path));

            inspected.Add((path, result));
        }

        var packageNames = inspected.Select(item => item.Result.PackageName!)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (packageNames.Length != 1)
            throw new InvalidDataException("تم رفض المجموعة: أسماء الحزم داخل ملفات APK ليست متطابقة.");

        var versionCodes = inspected.Select(item => item.Result.VersionCode!.Value)
            .Distinct().ToArray();
        if (versionCodes.Length != 1)
            throw new InvalidDataException("تم رفض المجموعة: Version Code غير متطابق بين ملفات APK.");

        var versionNames = inspected.Select(item => item.Result.VersionName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (versionNames.Length > 1)
            throw new InvalidDataException("تم رفض المجموعة: Version Name غير متطابق بين ملفات APK.");

        var baseApks = inspected.Where(item => string.IsNullOrWhiteSpace(item.Result.SplitName)).ToArray();
        var splitApks = inspected.Where(item => !string.IsNullOrWhiteSpace(item.Result.SplitName)).ToArray();
        if (baseApks.Length != 1 || splitApks.Length != inspected.Count - 1)
            throw new InvalidDataException("يجب أن تحتوي المجموعة على ملف Base واحد فقط، وبقية الملفات يجب أن تحمل Split Name في Manifest.");

        var splitNames = splitApks.Select(item => item.Result.SplitName!).ToArray();
        if (splitNames.Any(name => name.Length > 255 || name.Any(char.IsControl)) ||
            splitNames.Distinct(StringComparer.Ordinal).Count() != splitNames.Length)
            throw new InvalidDataException("Split Name مفقود أو غير صالح أو مكرر بين الملفات.");

        var ordered = new[] { baseApks[0] }
            .Concat(splitApks.OrderBy(item => item.Result.SplitName, StringComparer.Ordinal))
            .ToArray();

        return new AndroidSplitApkInstallPlan(
            packageNames[0],
            versionNames.FirstOrDefault(),
            versionCodes[0],
            ordered.Select(item => new AndroidSplitApkFile(
                item.Path,
                Path.GetFileName(item.Path),
                string.IsNullOrWhiteSpace(item.Result.SplitName) ? null : item.Result.SplitName,
                item.Result.Sha256)).ToArray());
    }

    /// <summary>
    /// Installs a previously inspected set with the base APK first. Each local file is re-hashed
    /// immediately before ADB is invoked so a changed file cannot silently replace the inspected bytes.
    /// Android remains responsible for validating signer compatibility during installation.
    /// </summary>
    public async Task<string> InstallSplitApkSetAsync(
        string serial,
        AndroidSplitApkInstallPlan plan,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        ArgumentNullException.ThrowIfNull(plan);
        if (!IsSafePackageName(plan.PackageName) || plan.VersionCode < 0 ||
            plan.Files is null || plan.Files.Count is < 2 or > 32)
            throw new InvalidDataException("بيانات مجموعة Split APK غير صالحة.");

        var baseFiles = plan.Files.Where(file => string.IsNullOrWhiteSpace(file.SplitName)).ToArray();
        var splitFiles = plan.Files.Where(file => !string.IsNullOrWhiteSpace(file.SplitName)).ToArray();
        if (baseFiles.Length != 1 ||
            splitFiles.Length != plan.Files.Count - 1 ||
            splitFiles.Select(file => file.SplitName!).Distinct(StringComparer.Ordinal).Count() != splitFiles.Length)
            throw new InvalidDataException("خطة التثبيت لا تحتوي Base واحدًا وملفات Split فريدة.");

        var orderedFiles = new[] { baseFiles[0] }
            .Concat(splitFiles.OrderBy(file => file.SplitName, StringComparer.Ordinal))
            .ToArray();
        var arguments = new List<string> { "install-multiple", "-r" };

        foreach (var file in orderedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(file.FullPath);
            if (!Path.GetExtension(fullPath).Equals(".apk", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath) ||
                !string.Equals(Path.GetFileName(fullPath), file.FileName, StringComparison.Ordinal) ||
                !Regex.IsMatch(file.Sha256 ?? "", "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
                throw new InvalidDataException("تغير مسار أحد ملفات APK أو بيانات بصمته بعد الفحص.");

            await using var stream = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(hash, file.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("تغير محتوى ملف APK بعد الفحص؛ أُوقف التثبيت: " + file.FileName);

            arguments.Add(fullPath);
        }

        var output = await RunDeviceAsync(
            serial,
            arguments,
            cancellationToken,
            TimeSpan.FromMinutes(15));
        var confirmed = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().Equals("Success", StringComparison.OrdinalIgnoreCase));
        if (!confirmed)
            throw new InvalidOperationException(
                "لم يؤكد Android نجاح install-multiple. قد يكون هناك اختلاف في توقيع المطور أو توافق الملفات أو إصدار النظام. مخرجات ADB: " + output);

        return output.Trim();
    }

    public async Task<string> UninstallThirdPartyPackageAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        if (!IsSafePackageName(packageName))
            throw new ArgumentException("اسم حزمة Android غير صالح.", nameof(packageName));

        var thirdPartyPackages = await ListThirdPartyPackagesAsync(serial, cancellationToken);
        if (!thirdPartyPackages.Any(package =>
                package.PackageName.Equals(packageName, StringComparison.Ordinal)))
        {
            throw new UnauthorizedAccessException(
                "رُفضت الإزالة: الحزمة ليست ضمن قائمة تطبيقات الطرف الثالث التي يعرضها Android لهذا الجهاز.");
        }

        var output = await RunDeviceAsync(
            serial,
            ["uninstall", packageName],
            cancellationToken,
            TimeSpan.FromMinutes(5));

        var confirmed = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().Equals("Success", StringComparison.OrdinalIgnoreCase));
        if (!confirmed)
            throw new InvalidOperationException(
                "لم يؤكد ADB نجاح إزالة التطبيق. لم يتم عرض العملية على أنها ناجحة. مخرجات ADB: " + output);

        return output.Trim();
    }


    public async Task<IReadOnlyList<AndroidRuntimePermission>> GetThirdPartyRuntimePermissionsAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        await EnsureThirdPartyPackageAsync(serial, packageName, cancellationToken);
        var output = await GetPackageDetailsAsync(serial, packageName, cancellationToken);
        return AndroidRuntimePermissionParser.Parse(output);
    }

    public async Task<string> SetThirdPartyRuntimePermissionAsync(
        string serial,
        string packageName,
        string permissionName,
        bool granted,
        CancellationToken cancellationToken = default)
    {
        await EnsureThirdPartyPackageAsync(serial, packageName, cancellationToken);
        if (!AndroidRuntimePermissionParser.IsSafePermissionName(permissionName))
            throw new ArgumentException("اسم صلاحية Android غير صالح.", nameof(permissionName));

        var currentOutput = await GetPackageDetailsAsync(serial, packageName, cancellationToken);
        var permission = AndroidRuntimePermissionParser.Parse(currentOutput)
            .SingleOrDefault(item => item.Name.Equals(permissionName, StringComparison.Ordinal));
        if (permission is null)
            throw new UnauthorizedAccessException(
                "رُفض التغيير: الصلاحية غير موجودة ضمن صلاحيات Runtime التي طلبها التطبيق. لا يمكن منح صلاحية لم يطلبها التطبيق أو صلاحية تثبيت/نظام.");
        if (permission.IsConflicting || !permission.IsGranted.HasValue)
            throw new InvalidDataException("حالة الصلاحية متعارضة داخل مخرجات PackageManager؛ لم يتم إرسال أمر تغيير.");
        if (permission.IsGranted.Value == granted)
            return granted
                ? "الصلاحية ممنوحة بالفعل؛ لم يتم إرسال أمر مكرر."
                : "الصلاحية مرفوضة بالفعل؛ لم يتم إرسال أمر مكرر.";

        var command = granted ? "grant" : "revoke";
        await RunDeviceAsync(
            serial,
            ["shell", "pm", command, packageName, permissionName],
            cancellationToken,
            TimeSpan.FromSeconds(30));

        // Verify the actual post-operation state instead of trusting the command exit code alone.
        await EnsureThirdPartyPackageAsync(serial, packageName, cancellationToken);
        var verifiedOutput = await GetPackageDetailsAsync(serial, packageName, cancellationToken);
        var verified = AndroidRuntimePermissionParser.Parse(verifiedOutput)
            .SingleOrDefault(item => item.Name.Equals(permissionName, StringComparison.Ordinal));
        if (verified is null || verified.IsConflicting || verified.IsGranted != granted)
            throw new InvalidOperationException(
                $"أُرسل أمر pm {command} لكن PackageManager لم يؤكد الحالة المطلوبة للصلاحية {permissionName}. راجع إصدار Android وسياسة التطبيق.");

        return $"تم {(granted ? "منح" : "سحب")} الصلاحية وتحقق SoftBox من الحالة الجديدة: {permissionName} = {(granted ? "granted" : "denied")}.";
    }

    public async Task<string> LaunchThirdPartyPackageAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        await EnsureThirdPartyPackageAsync(serial, packageName, cancellationToken);
        var output = await RunDeviceAsync(
            serial,
            ["shell", "monkey", "-p", packageName, "-c", "android.intent.category.LAUNCHER", "1"],
            cancellationToken,
            TimeSpan.FromSeconds(45));

        if (output.Contains("No activities found", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("monkey aborted", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Error", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "لم يتمكن Android من تشغيل نشاط Launcher لهذا التطبيق. قد لا يحتوي التطبيق على واجهة تشغيل. مخرجات الهاتف: " + output);

        return string.IsNullOrWhiteSpace(output)
            ? $"تم إرسال طلب تشغيل تطبيق الطرف الثالث {packageName} عبر Android Activity Manager."
            : output.Trim();
    }

    public async Task<string> ForceStopThirdPartyPackageAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        await EnsureThirdPartyPackageAsync(serial, packageName, cancellationToken);
        var output = await RunDeviceAsync(
            serial,
            ["shell", "am", "force-stop", packageName],
            cancellationToken,
            TimeSpan.FromSeconds(30));
        return string.IsNullOrWhiteSpace(output)
            ? $"قبل Android أمر إيقاف التطبيق إجباريًا: {packageName}."
            : output.Trim();
    }

    public async Task<string> ClearThirdPartyPackageDataAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        await EnsureThirdPartyPackageAsync(serial, packageName, cancellationToken);
        var output = await RunDeviceAsync(
            serial,
            ["shell", "pm", "clear", packageName],
            cancellationToken,
            TimeSpan.FromMinutes(2));
        var confirmed = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().Equals("Success", StringComparison.OrdinalIgnoreCase));
        if (!confirmed)
            throw new InvalidOperationException(
                "لم يؤكد Android نجاح مسح بيانات التطبيق. لم يتم عرض العملية على أنها ناجحة. المخرجات: " + output);

        await EnsureThirdPartyPackageAsync(serial, packageName, cancellationToken);
        return $"أكد Android مسح بيانات التطبيق {packageName}. التطبيق ما زال مثبتًا، لكن بياناته وإعداداته المحلية أُزيلت.";
    }

    public async Task<string> RebootDeviceAsync(
        string serial,
        AndroidRebootTarget target,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        var state = await adb.RunAsync(["-s", serial, "get-state"], cancellationToken, TimeSpan.FromSeconds(12));
        if (!state.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "الجهاز المحدد ليس في حالة ADB device المصرح بها حاليًا؛ لم يتم إرسال أمر إعادة التشغيل.");

        var arguments = target switch
        {
            AndroidRebootTarget.System => new[] { "reboot" },
            AndroidRebootTarget.Recovery => new[] { "reboot", "recovery" },
            AndroidRebootTarget.Bootloader => new[] { "reboot", "bootloader" },
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        var output = await adb.RunAsync(
            new[] { "-s", serial }.Concat(arguments).ToArray(),
            cancellationToken,
            TimeSpan.FromSeconds(25));

        var mode = target switch
        {
            AndroidRebootTarget.System => "Android System",
            AndroidRebootTarget.Recovery => "Recovery",
            AndroidRebootTarget.Bootloader => "Bootloader / Fastboot",
            _ => "Unknown"
        };
        return $"تم قبول أمر إعادة تشغيل الجهاز المحدد إلى {mode}. قد ينقطع ADB أثناء انتقال الهاتف إلى الوضع المطلوب. {output}".Trim();
    }

    private async Task EnsureThirdPartyPackageAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken)
    {
        ValidateAuthorizedSerial(serial);
        if (!IsSafePackageName(packageName))
            throw new ArgumentException("اسم حزمة Android غير صالح.", nameof(packageName));

        var packages = await ListThirdPartyPackagesAsync(serial, cancellationToken);
        if (!packages.Any(item => item.PackageName.Equals(packageName, StringComparison.Ordinal)))
            throw new UnauthorizedAccessException(
                "رُفضت العملية: الحزمة ليست ضمن قائمة تطبيقات الطرف الثالث الحالية على الجهاز. لا يتم تنفيذ هذه الأوامر على تطبيقات النظام.");
    }

    public async Task<AndroidVendorDiagnostics> ReadVendorDiagnosticsAsync(
        string serial,
        string manufacturer,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        var vendor = NormalizeVendor(manufacturer);
        var properties = vendor switch
        {
            AndroidVendorKind.Samsung => new[]
            {
                "ro.product.model", "ro.product.device", "ro.product.name",
                "ro.build.version.release", "ro.build.version.sdk",
                "ro.build.version.oneui", "ro.csc.sales_code", "ro.boot.sales_code",
                "ril.sales_code", "ro.bootloader", "ro.build.PDA",
                "ro.build.fingerprint", "ro.boot.verifiedbootstate"
            },
            AndroidVendorKind.Xiaomi => new[]
            {
                "ro.product.model", "ro.product.device", "ro.product.name",
                "ro.product.marketname", "ro.product.mod_device",
                "ro.build.version.release", "ro.build.version.sdk",
                "ro.build.version.incremental", "ro.build.version.miui",
                "ro.miui.ui.version.name", "ro.miui.region",
                "ro.boot.hwc", "ro.boot.hwcountry", "ro.boot.verifiedbootstate"
            },
            _ => new[]
            {
                "ro.product.manufacturer", "ro.product.brand", "ro.product.model",
                "ro.product.device", "ro.product.name", "ro.build.version.release",
                "ro.build.version.sdk", "ro.build.fingerprint", "ro.boot.verifiedbootstate"
            }
        };

        var results = await Task.WhenAll(properties.Select(async key =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = await RunDeviceAsync(serial, ["shell", "getprop", key], cancellationToken);
            return new KeyValuePair<string, string>(key, value.Trim());
        }));

        return new AndroidVendorDiagnostics(
            vendor,
            DateTimeOffset.UtcNow,
            results.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            results.Where(pair => string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => pair.Key).ToArray());
    }

    public static AndroidVendorKind NormalizeVendor(string? manufacturer)
    {
        var value = (manufacturer ?? "").Trim().ToLowerInvariant();
        if (value.Contains("samsung", StringComparison.Ordinal))
            return AndroidVendorKind.Samsung;
        if (value.Contains("xiaomi", StringComparison.Ordinal) ||
            value.Equals("redmi", StringComparison.Ordinal) ||
            value.Equals("poco", StringComparison.Ordinal))
            return AndroidVendorKind.Xiaomi;
        return AndroidVendorKind.Generic;
    }

    public async Task<string> RunReadOnlyPresetAsync(
        string serial,
        string presetId,
        CancellationToken cancellationToken = default)
    {
        ValidateAuthorizedSerial(serial);
        if (!ReadOnlyPresets.TryGetValue(presetId, out var args))
            throw new ArgumentException("الأمر غير موجود في قائمة الأوامر المسموح بها.", nameof(presetId));

        return await RunDeviceAsync(serial, args, cancellationToken);
    }

    public async Task<IReadOnlyList<AndroidBatchResult>> RunBatchReadOnlyPresetAsync(
        IEnumerable<string> deviceSerials,
        string presetId,
        int maximumConcurrency = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceSerials);
        if (!ReadOnlyPresets.ContainsKey(presetId))
            throw new ArgumentException("الأمر غير موجود في قائمة الأوامر المسموح بها.", nameof(presetId));
        if (maximumConcurrency is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrency), "حد التوازي يجب أن يكون بين 1 و8.");

        var serials = deviceSerials.Distinct(StringComparer.Ordinal).ToArray();
        if (serials.Length == 0)
            throw new ArgumentException("اختر جهازًا واحدًا على الأقل.", nameof(deviceSerials));
        foreach (var serial in serials)
            ValidateAuthorizedSerial(serial);

        using var gate = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        var tasks = serials.Select(async serial =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                try
                {
                    var output = await RunReadOnlyPresetAsync(serial, presetId, cancellationToken);
                    return new AndroidBatchResult(serial, true, output, null);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    return new AndroidBatchResult(serial, false, "", ex.Message);
                }
            }
            finally { gate.Release(); }
        });
        return await Task.WhenAll(tasks);
    }

    public static IReadOnlyList<AndroidPackageInfo> ParsePackageList(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("package:", StringComparison.Ordinal))
            .Select(line => line["package:".Length..].Trim())
            .Where(IsSafePackageName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new AndroidPackageInfo(name))
            .ToArray();
    }

    public static bool IsSafePackageName(string? packageName)
        => !string.IsNullOrWhiteSpace(packageName) &&
           packageName.Length <= 255 &&
           Regex.IsMatch(packageName, @"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)+$", RegexOptions.CultureInvariant);

    public static string ValidatePublicStoragePath(string? remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            throw new ArgumentException("مسار Android مطلوب.", nameof(remotePath));

        var value = remotePath.Trim();
        if (value.Length > 1024 || value.Any(char.IsControl) ||
            value.Contains('\0') || !value.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("يجب أن يكون المسار مطلقًا وصالحًا.", nameof(remotePath));

        var allowedRoots = new[] { "/sdcard", "/storage/emulated/0", "/storage/self/primary" };
        var inAllowedRoot = allowedRoots.Any(root =>
            value.Equals(root, StringComparison.Ordinal) ||
            value.StartsWith(root + "/", StringComparison.Ordinal));
        if (!inAllowedRoot)
            throw new UnauthorizedAccessException("تقتصر عمليات إدارة الملفات والنسخ على مساحة التخزين المشتركة التي يتيحها Android، ولا تشمل /data أو ملفات التطبيقات الخاصة.");

        if (value.Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException("المسار لا يسمح بمقاطع التنقل إلى مجلد أب.", nameof(remotePath));

        return value;
    }


    private Task<string> RunDeviceAsync(
        string serial,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken,
        TimeSpan? commandTimeout = null)
    {
        ValidateAuthorizedSerial(serial);
        var arguments = new List<string> { "-s", serial };
        arguments.AddRange(args);
        return adb.RunAsync(arguments, cancellationToken, commandTimeout);
    }

    private static void ValidateAuthorizedSerial(string serial)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
    }

    private static string QuoteForRemoteShell(string value)
        => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
