using System.IO;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record AndroidPackageInfo(string PackageName);
public sealed record AndroidBatchResult(string DeviceSerial, bool Success, string Output, string? Error);

/// <summary>
/// ADB operations restricted to authorized devices, public shared storage and read-only command presets.
/// It intentionally has no arbitrary shell-command or package-removal method.
/// </summary>
public sealed class AndroidDeviceToolsService(AdbClient adb)
{
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
