using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Backup;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Copies files from Android shared storage through authorized ADB and builds a SHA-256 indexed archive.
/// The operation does not access app-private data or perform a device restore.
/// </summary>
public sealed record BackupRestoreResult(
    string RemoteRestoreDirectory,
    int TotalFiles,
    int RestoredFiles,
    long RestoredBytes,
    IReadOnlyList<string> Errors,
    bool WasEncrypted);

public sealed class AndroidBackupService(AdbClient adb)
{
    private readonly BackupArchiveService _archives = new();
    private readonly EncryptedBackupService _encrypted = new();

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
        Directory.CreateDirectory(stagingDirectory);

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

    public async Task<BackupArchiveManifest> BackupSharedDirectoryEncryptedAsync(
        string serial,
        string remoteDirectory,
        string destinationEncryptedPath,
        string password,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EncryptedBackupService.ValidatePassword(password);
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        var remote = AndroidDeviceToolsService.ValidatePublicStoragePath(remoteDirectory);
        if (string.IsNullOrWhiteSpace(destinationEncryptedPath))
            throw new ArgumentException("حدد مسار النسخة المشفرة.", nameof(destinationEncryptedPath));

        var stagingRoot = Path.Combine(Path.GetTempPath(), "SoftBox-Android-Encrypted-Backup-" + Guid.NewGuid().ToString("N"));
        var stagingDirectory = Path.Combine(stagingRoot, "device-files");
        var temporaryZip = Path.Combine(stagingRoot, "unencrypted-staging.zip");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            progress?.Report("جارٍ نقل الملفات المسموح بها من مساحة Android المشتركة عبر ADB...");
            await adb.RunAsync(["-s", serial, "pull", remote, stagingDirectory],
                cancellationToken, TimeSpan.FromHours(3));

            if (!Directory.Exists(stagingDirectory))
                throw new IOException("لم ينشئ ADB مجلد النقل المتوقع. راجع صلاحية المسار واتصال الهاتف.");

            cancellationToken.ThrowIfCancellationRequested();
            var serialHint = serial.Length > 4 ? "••••" + serial[^4..] : "••••";
            progress?.Report("جارٍ بناء ZIP مؤقت وحساب SHA-256 لكل ملف...");
            var manifest = await _archives.CreateArchiveAsync(
                stagingDirectory, temporaryZip, serialHint, remote, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("جارٍ تشفير النسخة المؤقتة باستخدام AES-256 والتحقق بالمصادقة...");
            await _encrypted.EncryptAsync(temporaryZip, destinationEncryptedPath, password, cancellationToken);
            progress?.Report($"اكتمل ملف SBX المشفر: {manifest.FileCount:N0} ملف، {manifest.TotalBytes:N0} بايت.");
            return manifest;
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                    Directory.Delete(stagingRoot, recursive: true);
            }
            catch { }
        }
    }

    public async Task<BackupRestoreResult> RestoreToNewAndroidDirectoryAsync(
        string serial,
        string backupPath,
        string remoteParentDirectory,
        string? password = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            throw new FileNotFoundException("ملف النسخة الاحتياطية غير موجود.", backupPath);

        var remoteParent = AndroidDeviceToolsService.ValidatePublicStoragePath(remoteParentDirectory);
        var encrypted = Path.GetExtension(backupPath).Equals(".sbx", StringComparison.OrdinalIgnoreCase);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "SoftBox-Restore-" + Guid.NewGuid().ToString("N"));
        var temporaryZip = Path.Combine(temporaryRoot, "verified-backup.zip");
        var extractionRoot = Path.Combine(temporaryRoot, "files");
        Directory.CreateDirectory(extractionRoot);

        try
        {
            if (encrypted)
            {
                EncryptedBackupService.ValidatePassword(password);
                progress?.Report("جارٍ التحقق من مصادقة SBX قبل فك محتواه...");
                await _encrypted.DecryptAsync(backupPath, temporaryZip, password!, cancellationToken);
            }
            else
            {
                temporaryZip = Path.GetFullPath(backupPath);
            }

            progress?.Report("جارٍ التحقق من Manifest وبصمات SHA-256 لكل الملفات...");
            var verification = await _archives.VerifyArchiveAsync(temporaryZip, cancellationToken);
            if (!verification.IsValid)
                throw new InvalidDataException(
                    "تم إيقاف الاستعادة لأن النسخة غير سليمة: " + string.Join(" | ", verification.Errors.Take(10)));

            using var archive = ZipFile.OpenRead(temporaryZip);
            var manifestEntry = archive.Entries.SingleOrDefault(entry =>
                entry.FullName.Equals(BackupArchiveService.ManifestEntryName, StringComparison.Ordinal))
                ?? throw new InvalidDataException("Manifest النسخة الاحتياطية غير موجود.");

            BackupArchiveManifest? manifest;
            await using (var manifestStream = manifestEntry.Open())
                manifest = await JsonSerializer.DeserializeAsync<BackupArchiveManifest>(
                    manifestStream, cancellationToken: cancellationToken);
            if (manifest is null || manifest.Files is null || manifest.Files.Count != verification.VerifiedFiles)
                throw new InvalidDataException("قائمة ملفات النسخة لا تطابق نتيجة التحقق.");

            var restoreFolder = "SoftBox-Restore-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") +
                                "-" + Guid.NewGuid().ToString("N")[..8];
            var remoteRestoreRoot = AndroidDeviceToolsService.ValidatePublicStoragePath(
                remoteParent.TrimEnd('/') + "/" + restoreFolder);

            // Create a unique destination directory; never restore into the original source directory
            // and never delete/overwrite any pre-existing phone files.
            var tools = new AndroidDeviceToolsService(adb);
            await tools.CreateDirectoryAsync(serial, remoteRestoreRoot, cancellationToken);
            progress?.Report("تم إنشاء مجلد استعادة جديد لا يحتوي على ملفات سابقة: " + remoteRestoreRoot);

            var dataEntries = archive.Entries
                .Where(entry => !entry.FullName.Equals(BackupArchiveService.ManifestEntryName, StringComparison.Ordinal))
                .ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
            var errors = new List<string>();
            var restored = 0;
            long restoredBytes = 0;

            for (var index = 0; index < manifest.Files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = manifest.Files[index];
                try
                {
                    ValidateRestoreRelativePath(item.Path);
                    if (!dataEntries.TryGetValue(item.Path, out var entry))
                        throw new InvalidDataException("الملف غير موجود داخل الأرشيف بعد التحقق.");

                    var localPath = Path.GetFullPath(Path.Combine(
                        extractionRoot,
                        item.Path.Replace('/', Path.DirectorySeparatorChar)));
                    EnsureInsideDirectory(extractionRoot, localPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                    await using (var source = entry.Open())
                    await using (var destination = new FileStream(
                        localPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
                        await destination.FlushAsync(cancellationToken);
                    }

                    var relativeDirectory = Path.GetDirectoryName(item.Path.Replace('/', Path.DirectorySeparatorChar));
                    var remoteDirectory = string.IsNullOrEmpty(relativeDirectory)
                        ? remoteRestoreRoot
                        : AndroidDeviceToolsService.ValidatePublicStoragePath(
                            remoteRestoreRoot + "/" + relativeDirectory.Replace(Path.DirectorySeparatorChar, '/'));

                    await tools.CreateDirectoryAsync(serial, remoteDirectory, cancellationToken);
                    await tools.PushFileAsync(serial, localPath, remoteDirectory, cancellationToken);
                    restored++;
                    restoredBytes = checked(restoredBytes + item.SizeBytes);
                    progress?.Report($"تمت استعادة {index + 1}/{manifest.Files.Count}: {item.Path}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    errors.Add(item.Path + ": " + ex.Message);
                    progress?.Report($"تعذر استعادة {item.Path}: {ex.Message}");
                }
            }
            return new BackupRestoreResult(
                remoteRestoreRoot,
                manifest.Files.Count,
                restored,
                restoredBytes,
                errors,
                encrypted);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, recursive: true);
            }
            catch { }
        }
    }

    private static void ValidateRestoreRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl) ||
            path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("مسار داخل النسخة غير صالح للاستعادة.");

        foreach (var segment in path.Split('/'))
        {
            if (segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                segment.Length > 180 ||
                segment.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(
                    segment, @"^(COM|LPT)[1-9]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new InvalidDataException("اسم ملف غير مدعوم على Windows: " + segment);
        }
    }

    private static void EnsureInsideDirectory(string root, string path)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("مسار الاستخراج يخرج عن مجلد العمل المؤقت.");
    }

    public Task<BackupVerificationResult> VerifyEncryptedBackupAsync(
        string encryptedPath,
        string password,
        CancellationToken cancellationToken = default)
        => _encrypted.VerifyEncryptedBackupAsync(encryptedPath, password, cancellationToken);

    public Task<BackupVerificationResult> VerifyArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
        => _archives.VerifyArchiveAsync(archivePath, cancellationToken);
}
