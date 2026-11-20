using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SoftBox.PhoneLab.Core.Backup;

public sealed record BackupFileEntry(string Path, long SizeBytes, string Sha256);

public sealed record BackupArchiveManifest(
    string Product,
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string SourceDevice,
    string SourcePath,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<BackupFileEntry> Files,
    string SecurityNotice);

public sealed record BackupVerificationResult(
    bool IsValid,
    int VerifiedFiles,
    long VerifiedBytes,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Creates and verifies a local ZIP backup with SHA-256 checksums for every included file.
/// It does not encrypt the archive or copy Android-private application/system data.
/// </summary>
public sealed class BackupArchiveService
{
    public const string ManifestEntryName = "softbox-backup-manifest.json";
    private const int MaximumFiles = 50_000;
    private const long MaximumTotalBytes = 20L * 1024 * 1024 * 1024;
    private const long MaximumManifestBytes = 32L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<BackupArchiveManifest> CreateArchiveAsync(
        string sourceDirectory,
        string archivePath,
        string sourceDevice,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException("مجلد الملفات المراد نسخه غير موجود.");
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("حدد مسار ملف النسخة الاحتياطية.", nameof(archivePath));
        if (string.IsNullOrWhiteSpace(sourceDevice))
            throw new ArgumentException("معرف مصدر النسخة مطلوب.", nameof(sourceDevice));
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("مسار المصدر مطلوب.", nameof(sourcePath));

        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var target = Path.GetFullPath(archivePath);
        if (target.StartsWith(sourceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("يجب حفظ ملف النسخة خارج مجلد المصدر.");

        var files = EnumerateFiles(sourceRoot);
        if (files.Count > MaximumFiles)
            throw new InvalidDataException($"عدد الملفات يتجاوز الحد الآمن ({MaximumFiles:N0}).");

        var records = new List<BackupFileEntry>(files.Count);
        long totalBytes = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var fileStream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(sourceRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                    ValidateArchivePath(relative);
                    var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                    await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                        128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using var output = entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[128 * 1024];
                    long fileBytes = 0;
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                        if (read == 0)
                            break;
                        fileBytes = checked(fileBytes + read);
                        totalBytes = checked(totalBytes + read);
                        if (totalBytes > MaximumTotalBytes)
                            throw new InvalidDataException("الحجم الإجمالي يتجاوز الحد الآمن البالغ 20 GiB.");
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                    records.Add(new BackupFileEntry(
                        relative,
                        fileBytes,
                        Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
                }

                var manifest = new BackupArchiveManifest(
                    "SoftBox",
                    1,
                    DateTimeOffset.UtcNow,
                    sourceDevice,
                    sourcePath,
                    records.Count,
                    totalBytes,
                    records,
                    "This archive is not encrypted. It contains only files accessible through the selected source path; Android-private app/system data is not included.");

                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
            }

            File.Move(temporary, target, overwrite: true);
            return new BackupArchiveManifest(
                "SoftBox", 1, DateTimeOffset.UtcNow, sourceDevice, sourcePath,
                records.Count, totalBytes, records,
                "This archive is not encrypted. It contains only files accessible through the selected source path; Android-private app/system data is not included.");
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }

    public async Task<BackupVerificationResult> VerifyArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("ملف النسخة الاحتياطية غير موجود.", archivePath);

        var errors = new List<string>();
        var warnings = new List<string>();
        var verifiedFiles = 0;
        long verifiedBytes = 0;

        using var archive = ZipFile.OpenRead(archivePath);
        var manifestEntries = archive.Entries
            .Where(entry => entry.FullName.Equals(ManifestEntryName, StringComparison.Ordinal))
            .ToArray();
        if (manifestEntries.Length != 1)
            throw new InvalidDataException("يجب أن تحتوي النسخة على Manifest واحد فقط.");

        if (manifestEntries[0].Length > MaximumManifestBytes)
            throw new InvalidDataException("Manifest النسخة الاحتياطية أكبر من الحد الآمن.");

        BackupArchiveManifest? manifest;
        await using (var stream = manifestEntries[0].Open())
            manifest = await JsonSerializer.DeserializeAsync<BackupArchiveManifest>(stream, JsonOptions, cancellationToken);
        if (manifest is null || manifest.Product != "SoftBox" || manifest.SchemaVersion != 1 ||
            manifest.Files is null || manifest.Files.Count > MaximumFiles)
            throw new InvalidDataException("Manifest النسخة الاحتياطية غير صالح أو غير مدعوم.");

        var dataEntries = archive.Entries
            .Where(entry => !entry.FullName.Equals(ManifestEntryName, StringComparison.Ordinal))
            .ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
        var expectedPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var expected in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateArchivePath(expected.Path);
            if (!expectedPaths.Add(expected.Path))
            {
                errors.Add("مسار مكرر في Manifest: " + expected.Path);
                continue;
            }

            if (!dataEntries.TryGetValue(expected.Path, out var entry))
            {
                errors.Add("ملف مفقود من النسخة: " + expected.Path);
                continue;
            }

            if (entry.Length != expected.SizeBytes)
            {
                errors.Add("حجم مختلف: " + expected.Path);
                continue;
            }

            await using var content = entry.Open();
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(hash), Convert.FromHexString(expected.Sha256)))
            {
                errors.Add("بصمة SHA-256 مختلفة: " + expected.Path);
                continue;
            }

            verifiedFiles++;
            verifiedBytes = checked(verifiedBytes + entry.Length);
        }

        foreach (var extra in dataEntries.Keys.Except(expectedPaths, StringComparer.Ordinal))
            errors.Add("ملف غير مسجل في Manifest: " + extra);

        if (manifest.FileCount != manifest.Files.Count ||
            manifest.TotalBytes != manifest.Files.Sum(file => file.SizeBytes))
            errors.Add("عدادات Manifest لا تطابق قائمة الملفات.");

        warnings.Add("التحقق يطابق محتوى الملفات مع بصمات Manifest فقط؛ لا يثبت هوية مصدر الجهاز أو ناشر النسخة.");
        if (manifest.SecurityNotice.Contains("not encrypted", StringComparison.OrdinalIgnoreCase))
            warnings.Add("النسخة غير مشفرة. احفظها في مكان خاص وآمن.");

        return new BackupVerificationResult(errors.Count == 0, verifiedFiles, verifiedBytes, errors, warnings);
    }

    private static List<string> EnumerateFiles(string sourceRoot)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(sourceRoot);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                    pending.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                    files.Add(file);
            }
        }

        return files.OrderBy(file => file, StringComparer.Ordinal).ToList();
    }

    private static void ValidateArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') ||
            path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(segment => segment is "" or "." or "..") ||
            path.Any(char.IsControl))
            throw new InvalidDataException("مسار ملف غير آمن داخل النسخة الاحتياطية.");
    }
}
