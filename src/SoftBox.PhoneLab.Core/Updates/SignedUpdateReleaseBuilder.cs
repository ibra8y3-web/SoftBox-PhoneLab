using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SoftBox.PhoneLab.Core.Plugins;

namespace SoftBox.PhoneLab.Core.Updates;

public sealed record SignedUpdateReleaseBuildResult(
    string Version,
    string ArchivePath,
    string PublisherKeyId,
    int FileCount,
    long ArchiveBytes,
    string ArchiveSha256);

/// <summary>
/// Produces the exact signed update package consumed by SignedUpdatePackageService.
/// It inventories the real published output, creates a manifest with actual byte lengths and
/// SHA-256 hashes, then signs the archive with a publisher-owned RSA private key. The key is
/// provided by the release operator at build time and is never written into the package.
/// </summary>
public static class SignedUpdateReleaseBuilder
{
    public const string PublisherName = "SoftBox Release Publisher";
    public const string ManifestEntryName = SignedUpdatePackageService.ManifestEntryName;
    private const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const long MaximumArchiveBytes = 1024L * 1024 * 1024;
    private const long MaximumFileBytes = 512L * 1024 * 1024;
    private const int MaximumFiles = 2000;
    private static readonly Regex VersionPattern = new(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<SignedUpdateReleaseBuildResult> BuildAsync(
        string publishDirectory,
        string outputArchivePath,
        string version,
        string minimumSupportedVersion,
        string mainExecutableRelativePath,
        string releaseNotes,
        string publisherPrivateKeyPem,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publishDirectory) || !Directory.Exists(publishDirectory))
            throw new DirectoryNotFoundException("مجلد نشر SoftBox غير موجود.");
        if (!TryVersion(version) || !TryVersion(minimumSupportedVersion))
            throw new ArgumentException("يجب تقديم إصدار SemVer صالح وإصدار أدنى صالح.");
        if (string.IsNullOrWhiteSpace(publisherPrivateKeyPem) || publisherPrivateKeyPem.Length > 32 * 1024 ||
            !publisherPrivateKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            throw new ArgumentException("مفتاح RSA الخاص بالناشر غير موجود أو غير صالح.", nameof(publisherPrivateKeyPem));

        var root = Path.GetFullPath(publishDirectory);
        var output = Path.GetFullPath(outputArchivePath);
        var mainPath = NormalizeRelativePath(mainExecutableRelativePath);
        if (!mainPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ملف التشغيل الرئيسي يجب أن يكون EXE.");
        var files = EnumerateFilesWithoutReparsePoints(root);
        if (files.Count is < 1 or > MaximumFiles)
            throw new InvalidDataException("مجلد النشر يجب أن يحتوي من 1 إلى 2,000 ملف.");

        long expanded = 0;
        var manifestFiles = new List<SignedUpdateFile>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeRelativePath(Path.GetRelativePath(root, file.FullName));
            if (relative.Equals(ManifestEntryName, StringComparison.OrdinalIgnoreCase) ||
                relative.Equals(PluginSignaturePayload.SignatureEntryName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("مجلد النشر يحتوي ملفًا محجوزًا لبيانات توقيع SoftBox: " + relative);
            if (file.Length is < 0 or > MaximumFileBytes)
                throw new InvalidDataException("أحد ملفات النشر يتجاوز 512 MiB: " + relative);
            expanded = checked(expanded + file.Length);
            if (expanded > MaximumExpandedBytes)
                throw new InvalidDataException("الحجم الإجمالي المنشور يتجاوز 2 GiB.");
            await using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant();
            manifestFiles.Add(new SignedUpdateFile(relative, file.Length, digest));
        }

        if (!manifestFiles.Any(file => file.Path.Equals(mainPath, StringComparison.OrdinalIgnoreCase)))
            throw new FileNotFoundException("ملف التشغيل الرئيسي غير موجود ضمن مجلد النشر.", Path.Combine(root, mainPath));

        var outputDirectory = Path.GetDirectoryName(output)
            ?? throw new InvalidDataException("مسار ملف التحديث لا يحتوي مجلدًا صالحًا.");
        Directory.CreateDirectory(outputDirectory);
        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException("ملف حزمة التحديث موجود بالفعل؛ لن يتم استبداله تلقائيًا.");
        var tempArchive = output + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            var manifest = new SignedUpdateManifest(
                1,
                NormalizeVersion(version),
                NormalizeVersion(minimumSupportedVersion),
                PublisherName,
                mainPath,
                manifestFiles,
                releaseNotes ?? "");

            using (var archive = ZipFile.Open(tempArchive, ZipArchiveMode.Create))
            {
                foreach (var item in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = NormalizeRelativePath(Path.GetRelativePath(root, item.FullName));
                    var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                    await using var source = new FileStream(item.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                        128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using var destination = entry.Open();
                    await source.CopyToAsync(destination, cancellationToken);
                }

                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest,
                    new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
            }

            var keyId = await PluginPackageSigner.SignPackageAsync(tempArchive, publisherPrivateKeyPem, cancellationToken);
            var archiveInfo = new FileInfo(tempArchive);
            if (archiveInfo.Length is <= 0 or > MaximumArchiveBytes)
                throw new InvalidDataException("حزمة التحديث الناتجة فارغة أو تتجاوز 1 GiB.");
            string archiveHash;
            await using (var archiveInput = new FileStream(tempArchive, FileMode.Open, FileAccess.Read, FileShare.Read,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                archiveHash = Convert.ToHexString(await SHA256.HashDataAsync(archiveInput, cancellationToken))
                    .ToLowerInvariant();
            }
            File.Move(tempArchive, output, overwrite: false);
            return new SignedUpdateReleaseBuildResult(
                NormalizeVersion(version), output, keyId, manifestFiles.Count, archiveInfo.Length, archiveHash);
        }
        catch
        {
            try { File.Delete(tempArchive); } catch { }
            throw;
        }
    }

    private static List<FileInfo> EnumerateFilesWithoutReparsePoints(string root)
    {
        var result = new List<FileInfo>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("مجلد النشر يحتوي Link/Junction؛ تم إيقاف توقيع الحزمة: " + path);
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(path);
                else
                    result.Add(new FileInfo(path));
                if (result.Count > MaximumFiles)
                    throw new InvalidDataException("مجلد النشر يتجاوز 2,000 ملف.");
            }
        }
        return result.OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = (path ?? "").Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(':') ||
            normalized.Contains('\\') ||
            normalized.Any(char.IsControl) ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("مسار ملف نشر غير آمن: " + path);
        return normalized;
    }

    private static bool TryVersion(string value)
        => !string.IsNullOrWhiteSpace(value) && VersionPattern.IsMatch(value.Trim());

    private static string NormalizeVersion(string value) => value.Trim().TrimStart('v', 'V');
}
