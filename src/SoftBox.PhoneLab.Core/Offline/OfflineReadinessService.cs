using System.Diagnostics;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SoftBox.PhoneLab.Core.Offline;

public enum OfflinePackageAvailability
{
    Available,
    Missing,
    Unreadable
}

public sealed record OfflinePackageStatus(
    string Id,
    string NameAr,
    string Category,
    string RelativePath,
    OfflinePackageAvailability Availability,
    long? SizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    string? Version,
    string? Sha256,
    string Details)
{
    public string AvailabilityLabelAr => Availability switch
    {
        OfflinePackageAvailability.Available => "متاح محليًا",
        OfflinePackageAvailability.Missing => "غير موجود في الحزمة",
        OfflinePackageAvailability.Unreadable => "موجود لكن تعذرت قراءته",
        _ => "حالة غير معروفة"
    };

    public string SizeLabel => SizeBytes is long size
        ? $"{size / 1024d / 1024d:N2} MB"
        : "—";
}

public sealed record OfflineDataDirectoryStatus(
    string Id,
    string NameAr,
    string RelativePath,
    bool Exists,
    int FileCount,
    long TotalBytes,
    bool ScanLimited,
    string Details)
{
    public string StatusLabelAr => !Exists ? "غير موجود" : ScanLimited ? "موجود — تجاوز حد الفحص" : "موجود";
    public string SizeLabel => $"{TotalBytes / 1024d / 1024d:N2} MB";
}

public sealed record OfflineReadinessSnapshot(
    DateTimeOffset CapturedAtUtc,
    string ApplicationRoot,
    string ApplicationVersion,
    string OperatingSystem,
    string Runtime,
    IReadOnlyList<string> ActiveNetworkInterfaces,
    string? StorageRoot,
    long? StorageTotalBytes,
    long? StorageAvailableBytes,
    IReadOnlyList<OfflinePackageStatus> Packages,
    IReadOnlyList<OfflineDataDirectoryStatus> DataDirectories)
{
    public int AvailablePackageCount => Packages.Count(x => x.Availability == OfflinePackageAvailability.Available);
    public int MissingPackageCount => Packages.Count(x => x.Availability == OfflinePackageAvailability.Missing);
    public int UnreadablePackageCount => Packages.Count(x => x.Availability == OfflinePackageAvailability.Unreadable);
    public string NetworkStatusLabel => ActiveNetworkInterfaces.Count == 0
        ? "لا توجد واجهة شبكة نشطة"
        : $"واجهات شبكة نشطة: {ActiveNetworkInterfaces.Count}";
    public string StorageAvailableLabel => StorageAvailableBytes is long value
        ? $"{value / 1024d / 1024d / 1024d:N2} GB متاحة"
        : "تعذر قراءة مساحة التخزين";
}

/// <summary>
/// Read-only inspection of files and data actually present in the current SoftBox installation.
/// It does not download, install, execute or repair external components.
/// </summary>
public sealed class OfflineReadinessService
{
    private const int MaximumDirectoryFiles = 10_000;
    private readonly string _applicationRoot;

    private static readonly (string Id, string NameAr, string Category, string RelativePath)[] PackageDefinitions =
    [
        ("desktop", "تطبيق SoftBox Desktop", "التطبيق", "SoftBox-PhoneLab.exe"),
        ("adb", "Android Debug Bridge (ADB)", "Android Platform-Tools", Path.Combine("Tools", "platform-tools", "adb.exe")),
        ("fastboot", "Android Fastboot", "Android Platform-Tools", Path.Combine("Tools", "platform-tools", "fastboot.exe")),
        ("scrcpy", "scrcpy — عرض وتحكم الهاتف", "Phone Link Studio", Path.Combine("Tools", "scrcpy", "scrcpy.exe")),
        ("agent", "SoftBox Android Agent", "Android Agent", Path.Combine("Agent", "SoftBox-PhoneLab-Agent.apk")),
        ("samsung-driver", "Samsung USB Driver", "USB Drivers", Path.Combine("Drivers", "Samsung_USB_Driver_v1.9.5.0.exe")),
        ("boardviewer", "TRACE Boardviewer", "Board Tools", Path.Combine("Tools", "TRACE-Boardviewer-1.3.3.exe"))
    ];

    private static readonly (string Id, string NameAr, string RelativePath)[] DataDefinitions =
    [
        ("device-profiles", "قاعدة ملفات تعريف الأجهزة", Path.Combine("Data", "device-profiles")),
        ("boardmaps", "خرائط اللوحات المحلية", Path.Combine("Data", "boardmaps"))
    ];

    public string ApplicationRoot => _applicationRoot;

    public OfflineReadinessService(string? applicationRoot = null)
    {
        _applicationRoot = Path.GetFullPath(applicationRoot ?? AppContext.BaseDirectory);
        if (!Directory.Exists(_applicationRoot))
            throw new DirectoryNotFoundException("مجلد تثبيت SoftBox غير موجود: " + _applicationRoot);
    }

    public async Task<OfflineReadinessSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var packages = new List<OfflinePackageStatus>(PackageDefinitions.Length);
        foreach (var definition in PackageDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            packages.Add(await InspectPackageAsync(definition, cancellationToken));
        }

        var directories = DataDefinitions
            .Select(InspectDataDirectory)
            .ToArray();

        var networkInterfaces = GetActiveNetworkInterfaces();
        var (storageRoot, totalBytes, availableBytes) = GetStorageStatus();

        return new OfflineReadinessSnapshot(
            DateTimeOffset.UtcNow,
            _applicationRoot,
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "غير متاح",
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            networkInterfaces,
            storageRoot,
            totalBytes,
            availableBytes,
            packages,
            directories);
    }

    private async Task<OfflinePackageStatus> InspectPackageAsync(
        (string Id, string NameAr, string Category, string RelativePath) definition,
        CancellationToken cancellationToken)
    {
        string fullPath;
        try
        {
            fullPath = ResolveInsideRoot(definition.RelativePath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new OfflinePackageStatus(
                definition.Id, definition.NameAr, definition.Category, definition.RelativePath,
                OfflinePackageAvailability.Unreadable, null, null, null, null,
                "تعذر التحقق من المسار: " + ex.Message);
        }

        if (HasReparsePointInPath(fullPath))
        {
            return new OfflinePackageStatus(
                definition.Id, definition.NameAr, definition.Category, definition.RelativePath,
                OfflinePackageAvailability.Unreadable, null, null, null, null,
                "المسار يحتوي على رابط رمزي أو Junction؛ تم رفض اتباعه خارج مجلد تثبيت SoftBox.");
        }

        if (!File.Exists(fullPath))
        {
            return new OfflinePackageStatus(
                definition.Id, definition.NameAr, definition.Category, definition.RelativePath,
                OfflinePackageAvailability.Missing, null, null, null, null,
                "الملف غير موجود داخل مجلد تثبيت SoftBox الحالي. لم يتم البحث في مسارات خارجية.");
        }

        try
        {
            var info = new FileInfo(fullPath);
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            var version = TryGetFileVersion(fullPath);
            return new OfflinePackageStatus(
                definition.Id,
                definition.NameAr,
                definition.Category,
                definition.RelativePath,
                OfflinePackageAvailability.Available,
                info.Length,
                info.LastWriteTimeUtc,
                version,
                Convert.ToHexString(hash),
                "تم العثور على الملف وقراءته وحساب بصمة SHA-256 محليًا. البصمة لا تثبت ناشر الملف أو توقيعه.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException)
        {
            return new OfflinePackageStatus(
                definition.Id, definition.NameAr, definition.Category, definition.RelativePath,
                OfflinePackageAvailability.Unreadable, null, null, null, null,
                "الملف موجود لكن تعذرت قراءته: " + ex.Message);
        }
    }

    private OfflineDataDirectoryStatus InspectDataDirectory((string Id, string NameAr, string RelativePath) definition)
    {
        string fullPath;
        try
        {
            fullPath = ResolveInsideRoot(definition.RelativePath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new OfflineDataDirectoryStatus(definition.Id, definition.NameAr, definition.RelativePath,
                false, 0, 0, false, "تعذر التحقق من المسار: " + ex.Message);
        }

        if (HasReparsePointInPath(fullPath))
            return new OfflineDataDirectoryStatus(definition.Id, definition.NameAr, definition.RelativePath,
                Directory.Exists(fullPath), 0, 0, true,
                "المجلد يحتوي على رابط رمزي أو Junction؛ تم رفض اتباعه خارج مجلد تثبيت SoftBox.");

        if (!Directory.Exists(fullPath))
            return new OfflineDataDirectoryStatus(definition.Id, definition.NameAr, definition.RelativePath,
                false, 0, 0, false, "المجلد المحلي غير موجود داخل حزمة التطبيق.");

        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(fullPath));
        var count = 0;
        long bytes = 0;
        var limited = false;

        try
        {
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var item in directory.EnumerateFileSystemInfos())
                {
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    if ((item.Attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push((DirectoryInfo)item);
                        continue;
                    }

                    count++;
                    bytes += ((FileInfo)item).Length;
                    if (count >= MaximumDirectoryFiles)
                    {
                        limited = true;
                        pending.Clear();
                        break;
                    }
                }
            }

            return new OfflineDataDirectoryStatus(
                definition.Id, definition.NameAr, definition.RelativePath,
                true, count, bytes, limited,
                limited
                    ? $"تم فحص أول {MaximumDirectoryFiles:N0} ملفًا فقط لحماية الأداء."
                    : "عدد الملفات والحجم الإجمالي محسوبان من الملفات المحلية الموجودة فعليًا.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new OfflineDataDirectoryStatus(
                definition.Id, definition.NameAr, definition.RelativePath,
                true, count, bytes, true,
                "المجلد موجود لكن تعذرت قراءة كامل محتوياته: " + ex.Message);
        }
    }

    private bool HasReparsePointInPath(string fullPath)
    {
        try
        {
            var current = _applicationRoot;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return true;

            var relative = Path.GetRelativePath(_applicationRoot, fullPath);
            foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        return true;
                }
                catch (FileNotFoundException)
                {
                    return false;
                }
                catch (DirectoryNotFoundException)
                {
                    return false;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            // Fail closed: if link status cannot be established, do not follow the path.
            return true;
        }
    }

    private string ResolveInsideRoot(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("مسار المكوّن غير صالح.", nameof(relativePath));

        var fullPath = Path.GetFullPath(Path.Combine(_applicationRoot, relativePath));
        var relative = Path.GetRelativePath(_applicationRoot, fullPath);
        if (relative == "." || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new IOException("المسار خارج مجلد تثبيت SoftBox.");

        return fullPath;
    }

    private static string? TryGetFileVersion(string path)
    {
        if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return !string.IsNullOrWhiteSpace(info.ProductVersion)
                ? info.ProductVersion
                : !string.IsNullOrWhiteSpace(info.FileVersion) ? info.FileVersion : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetActiveNetworkInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up &&
                            x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(x => $"{x.Name} — {x.NetworkInterfaceType}")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
        catch (PlatformNotSupportedException)
        {
            return [];
        }
    }

    private (string? Root, long? Total, long? Available) GetStorageStatus()
    {
        try
        {
            var root = Path.GetPathRoot(_applicationRoot);
            if (string.IsNullOrWhiteSpace(root))
                return (null, null, null);

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return (root, null, null);

            return (drive.Name, drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
        {
            return (null, null, null);
        }
    }
}
