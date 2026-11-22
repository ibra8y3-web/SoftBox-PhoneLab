using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Downloads the official Google Android SDK Platform-Tools (adb.exe / fastboot.exe) over HTTPS
/// into the current user's profile. No administrator rights are required.
/// </summary>
public static class AdbInstallerService
{
    private const string OfficialHost = "dl.google.com";
    private const string DownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
    private const long MaximumArchiveBytes = 300L * 1024 * 1024;

    /// <summary>%LOCALAPPDATA%\SoftBox\Tools</summary>
    public static string InstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoftBox", "Tools");

    public static async Task<string> InstallAsync(IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(DownloadUrl);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(OfficialHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("رابط تنزيل Platform-Tools غير موثوق.");

        Directory.CreateDirectory(InstallRoot);
        var archive = Path.Combine(Path.GetTempPath(), "softbox-platform-tools-" + Guid.NewGuid().ToString("N") + ".zip");
        var staging = Path.Combine(InstallRoot, "_staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            progress?.Report("جارٍ تنزيل Android Platform-Tools الرسمية من Google...");
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > MaximumArchiveBytes)
                    throw new InvalidOperationException("حجم ملف Platform-Tools أكبر من المتوقع.");
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(archive);
                await source.CopyToAsync(target, cancellationToken);
            }

            progress?.Report("جارٍ فك الضغط...");
            await Task.Run(() => ExtractSafely(archive, staging), cancellationToken);

            var extracted = Path.Combine(staging, "platform-tools");
            if (!File.Exists(Path.Combine(extracted, "adb.exe")))
                throw new InvalidOperationException("الملف المنزّل لا يحتوي على adb.exe.");

            var destination = Path.Combine(InstallRoot, "platform-tools");
            StopRunningAdb();
            if (Directory.Exists(destination))
            {
                try { Directory.Delete(destination, recursive: true); }
                catch { /* overwritten file-by-file below */ }
            }
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(extracted, file);
                var targetPath = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(file, targetPath, overwrite: true);
            }

            var adb = Path.Combine(destination, "adb.exe");
            if (!File.Exists(adb)) throw new InvalidOperationException("تعذر إنهاء تثبيت adb.exe.");
            progress?.Report("تم تثبيت ADB: " + adb);
            return adb;
        }
        finally
        {
            try { if (File.Exists(archive)) File.Delete(archive); } catch { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private static void ExtractSafely(string archivePath, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        var root = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archivePath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            var fullPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("مسار غير آمن داخل الأرشيف: " + entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, overwrite: true);
        }
    }

    private static void StopRunningAdb()
    {
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("adb"))
            {
                try
                {
                    var path = process.MainModule?.FileName ?? "";
                    if (path.StartsWith(InstallRoot, StringComparison.OrdinalIgnoreCase)) process.Kill();
                }
                catch { }
                finally { process.Dispose(); }
            }
        }
        catch { }
    }
}
