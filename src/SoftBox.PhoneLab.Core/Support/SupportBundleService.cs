using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Support;

public sealed record SupportBundleFile(string Name, long OriginalSizeBytes, long RedactedSizeBytes, string Sha256);
public sealed record SupportBundleResult(
    string ArchivePath,
    int IncludedFileCount,
    long IncludedBytes,
    IReadOnlyList<SupportBundleFile> Files,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Creates a local-only support archive with system/runtime metadata and user-selected text logs.
/// Secrets are redacted before storage. This service never uploads or transmits the archive.
/// </summary>
public sealed class SupportBundleService
{
    private const int MaximumSelectedFiles = 20;
    private const long MaximumInputFileBytes = 10 * 1024 * 1024;
    private const long MaximumTotalRedactedBytes = 50 * 1024 * 1024;
    private const int MaximumNotesCharacters = 4_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    // JSONL/JSON logs quote both property names and values, so redact those before applying plain-text patterns.
    private static readonly Regex JsonSecretProperty = new(
        @""""(?<key>password|passwd|pwd|token|access[_-]?token|refresh[_-]?token|api[_-]?key|secret|authorization|client[_-]?secret)""""(\\s*:\\s*)""""(?:\\\\.|[^""""\\\\])*""""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex JsonDeviceIdentifierProperty = new(
        @""""(?<key>serial|device[_-]?serial|adb[_-]?serial|adb[_-]?id|device[_-]?id|android[_-]?id|imei|imsi|iccid)""""(\\s*:\\s*)""""(?:\\\\.|[^""""\\\\])*""""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SecretAssignment = new(
        @"(?im)\b(password|passwd|pwd|token|access[_-]?token|refresh[_-]?token|api[_-]?key|secret|authorization)\b(\s*[:=]\s*)([^\s,;]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AuthorizationHeader = new(
        @"(?im)(\b(?:authorization|proxy-authorization)\b\s*[:=]\s*)(?:Bearer|Basic)\s+[^\r\n,;]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BearerSecret = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{8,}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DeviceSerial = new(
        @"(?im)\b(serial|device[_-]?serial|adb[_-]?serial|adb[_-]?id|device[_-]?id|android[_-]?id)\b(\s*[:=]\s*)([A-Za-z0-9._:-]{5,128})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<SupportBundleResult> CreateAsync(
        string destinationArchive,
        IEnumerable<string>? selectedLogFiles = null,
        string? userNotes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationArchive))
            throw new ArgumentException("حدد مكان حفظ حزمة الدعم.", nameof(destinationArchive));
        if (userNotes is { Length: > MaximumNotesCharacters })
            throw new ArgumentException("ملاحظات الدعم أطول من الحد المسموح.", nameof(userNotes));

        var inputs = (selectedLogFiles ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (inputs.Length > MaximumSelectedFiles)
            throw new InvalidDataException($"يمكن تضمين {MaximumSelectedFiles} ملف سجل كحد أقصى.");

        var archivePath = Path.GetFullPath(destinationArchive);
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        var temporary = archivePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var included = new List<SupportBundleFile>();
        var warnings = new List<string>
        {
            "حزمة دعم محلية. لا يتم رفع الملفات أو إرسالها إلى SoftBox أو أي خادم.",
            "تم تطبيق تنقيح تلقائي للحقول الحساسة الشائعة؛ راجع محتوى الحزمة قبل مشاركتها."
        };
        long totalBytes = 0;

        try
        {
            await using (var fileStream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var diagnostics = BuildSystemDiagnostics(userNotes);
                await WriteTextEntryAsync(archive, "system-diagnostics.txt", diagnostics, cancellationToken);

                foreach (var source in inputs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(source))
                        throw new FileNotFoundException("أحد ملفات السجل المختارة غير موجود.", source);

                    var info = new FileInfo(source);
                    if (info.Length > MaximumInputFileBytes)
                        throw new InvalidDataException($"ملف السجل يتجاوز 10 MiB: {info.Name}");

                    var extension = Path.GetExtension(source);
                    if (!new[] { ".txt", ".log", ".json", ".jsonl" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidDataException("تقتصر ملفات الدعم على TXT وLOG وJSON.");

                    var content = await File.ReadAllTextAsync(source, Encoding.UTF8, cancellationToken);
                    var redacted = RedactSensitiveData(content);
                    var safeName = MakeSafeEntryName(info.Name, included.Count + 1);
                    var bytes = Encoding.UTF8.GetBytes(redacted);
                    totalBytes = checked(totalBytes + bytes.Length);
                    if (totalBytes > MaximumTotalRedactedBytes)
                        throw new InvalidDataException("الحجم الإجمالي للملفات المنقحة يتجاوز 50 MiB.");

                    await WriteBytesEntryAsync(archive, "user-logs/" + safeName, bytes, cancellationToken);
                    var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    included.Add(new SupportBundleFile(
                        safeName,
                        info.Length,
                        bytes.LongLength,
                        hash));
                }

                var manifest = new SupportBundleManifest(
                    "SoftBox",
                    1,
                    DateTimeOffset.UtcNow,
                    Environment.OSVersion.ToString(),
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.OSArchitecture.ToString(),
                    RuntimeInformation.ProcessArchitecture.ToString(),
                    Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown",
                    included.Count,
                    included,
                    warnings);

                await WriteTextEntryAsync(
                    archive,
                    "support-manifest.json",
                    JsonSerializer.Serialize(manifest, JsonOptions),
                    cancellationToken);
            }

            File.Move(temporary, archivePath, overwrite: true);
            return new SupportBundleResult(
                archivePath,
                included.Count,
                totalBytes,
                included,
                warnings);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }

    public static string RedactSensitiveData(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var redacted = JsonSecretProperty.Replace(content, match =>
            match.Value[..(match.Value.IndexOf(':') + 1)] + "\"[REDACTED]\"");
        redacted = JsonDeviceIdentifierProperty.Replace(redacted, match =>
            match.Value[..(match.Value.IndexOf(':') + 1)] + "\"[DEVICE-ID-REDACTED]\"");
        redacted = AuthorizationHeader.Replace(redacted, "$1[REDACTED]");
        redacted = SecretAssignment.Replace(redacted, match =>
            match.Groups[1].Value + match.Groups[2].Value + "[REDACTED]");
        redacted = BearerSecret.Replace(redacted, "Bearer [REDACTED]");
        redacted = DeviceSerial.Replace(redacted, match =>
            match.Groups[1].Value + match.Groups[2].Value + "[DEVICE-ID-REDACTED]");

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            redacted = redacted.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

        return redacted;
    }

    private static string BuildSystemDiagnostics(string? notes)
    {
        var text = new StringBuilder();
        text.AppendLine("SoftBox Support Diagnostics");
        text.AppendLine("Generated UTC: " + DateTimeOffset.UtcNow.ToString("O"));
        text.AppendLine("Product: SoftBox — Universal Smart Device Service & Repair Platform");
        text.AppendLine("OS: " + Environment.OSVersion);
        text.AppendLine("Runtime: " + RuntimeInformation.FrameworkDescription);
        text.AppendLine("OS Architecture: " + RuntimeInformation.OSArchitecture);
        text.AppendLine("Process Architecture: " + RuntimeInformation.ProcessArchitecture);
        text.AppendLine("64-bit OS: " + Environment.Is64BitOperatingSystem);
        text.AppendLine("64-bit Process: " + Environment.Is64BitProcess);
        text.AppendLine("Entry Assembly Version: " + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown"));
        text.AppendLine();
        text.AppendLine("User Notes:");
        text.AppendLine(RedactSensitiveData(notes ?? ""));
        text.AppendLine();
        text.AppendLine("No account password, remote API token or raw ADB device serial is intentionally collected by this diagnostics summary.");
        return RedactSensitiveData(text.ToString());
    }

    private static string MakeSafeEntryName(string name, int index)
    {
        var safe = Regex.Replace(Path.GetFileNameWithoutExtension(name), @"[^\p{L}\p{N}._-]+", "_");
        if (string.IsNullOrWhiteSpace(safe))
            safe = "log";
        if (safe.Length > 80)
            safe = safe[..80];
        return $"{index:00}-{safe}{Path.GetExtension(name).ToLowerInvariant()}";
    }

    private static async Task WriteTextEntryAsync(
        ZipArchive archive,
        string name,
        string content,
        CancellationToken cancellationToken)
        => await WriteBytesEntryAsync(archive, name, Encoding.UTF8.GetBytes(content), cancellationToken);

    private static async Task WriteBytesEntryAsync(
        ZipArchive archive,
        string name,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(content, cancellationToken);
    }

    private sealed record SupportBundleManifest(
        string Product,
        int SchemaVersion,
        DateTimeOffset CreatedAtUtc,
        string OperatingSystem,
        string Runtime,
        string OSArchitecture,
        string ProcessArchitecture,
        string ApplicationVersion,
        int IncludedLogCount,
        IReadOnlyList<SupportBundleFile> Files,
        IReadOnlyList<string> Warnings);
}
