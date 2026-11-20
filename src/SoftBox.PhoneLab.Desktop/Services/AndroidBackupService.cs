using System.IO;
using SoftBox.PhoneLab.Core.Backup;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Copies files from Android shared storage through authorized ADB and builds a SHA-256 indexed archive.
/// The operation does not access app-private data or perform a device restore.
/// </summary>
public sealed class AndroidBackupService(AdbClient adb)
{
    private readonly BackupArchiveService _archives = new();

    public async Task<BackupArchiveManifest> BackupSharedDirectoryAsync(
        string serial,
        string remoteDirectory,
        string destinationArchive,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        var remote = AndroidDeviceToolsService.ValidatePublicStoragePath(remoteDirectory);
        if (string.IsNullOrWhiteSpace(destinationArchive))
            throw new ArgumentException("حدد مسار ملف النسخة الاحتياطية.", nameof(destinationArchive));

        var stagingRoot = Path.Combine(Path.GetTempPath(), "SoftBox-Android-Backup-" + Guid.NewGuid().ToString("N"));
        var stagingDirectory = Path.Combine(stagingRoot, "device-files");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            progress?.Report("جارٍ نقل الملفات المسموح بها من مساحة Android المشتركة عبر ADB...");
            var pullArguments = new List<string> { "-s", serial, "pull", remote, stagingDirectory };
            await adb.RunAsync(pullArguments, cancellationToken, TimeSpan.FromHours(3));

            if (!Directory.Exists(stagingDirectory))
            {
                if (File.Exists(stagingDirectory))
                    throw new InvalidDataException("المسار المحدد على الهاتف ليس مجلدًا.");
                throw new IOException("لم ينشئ ADB مجلد النقل المتوقع. راجع صلاحية المسار واتصال الهاتف.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("تم النقل. جارٍ إنشاء أرشيف ZIP وحساب SHA-256 لكل ملف...");
            var serialHint = serial.Length > 4 ? "••••" + serial[^4..] : "••••";
            var manifest = await _archives.CreateArchiveAsync(
                stagingDirectory,
                destinationArchive,
                serialHint,
                remote,
                cancellationToken);

            progress?.Report($"اكتمل الأرشيف: {manifest.FileCount:N0} ملف، {manifest.TotalBytes:N0} بايت.");
            return manifest;
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                    Directory.Delete(stagingRoot, recursive: true);
            }
            catch { /* Temporary cleanup must not hide the original transfer or archive error. */ }
        }
    }

    public Task<BackupVerificationResult> VerifyArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
        => _archives.VerifyArchiveAsync(archivePath, cancellationToken);
}
