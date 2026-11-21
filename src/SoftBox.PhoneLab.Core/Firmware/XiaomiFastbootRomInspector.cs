using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Firmware;

public enum XiaomiRomProductMatchState
{
    Match,
    Mismatch,
    Unknown
}

public sealed record XiaomiFastbootRomInspectionResult(
    string FileName,
    long ArchiveBytes,
    string ArchiveSha256,
    IReadOnlyList<string> RequiredProducts,
    IReadOnlyList<string> ImageFiles,
    IReadOnlyList<string> ScriptFiles,
    IReadOnlyList<string> FlashPartitions,
    bool ContainsEraseOrWipeCommands,
    bool ContainsUnlockCommands,
    string? SelectedDeviceProduct,
    XiaomiRomProductMatchState ProductMatch,
    bool FlashAllowed,
    bool UnlockAllowed,
    IReadOnlyList<string> Findings);

/// <summary>
/// Inspects Xiaomi/Redmi/POCO Fastboot ROM TAR/TGZ packages without extracting or executing scripts.
/// It reports product requirements and commands found in the included scripts. It never flashes,
/// unlocks, erases, formats, or claims that a package publisher is trusted.
/// </summary>
public static class XiaomiFastbootRomInspector
{
    private const long MaximumArchiveBytes = 8L * 1024 * 1024 * 1024;
    private const long MaximumExpandedBytes = 24L * 1024 * 1024 * 1024;
    private const int MaximumEntries = 20_000;
    private const int MaximumTextFileBytes = 8 * 1024 * 1024;
    private static readonly Regex ProductPattern = new(
        @"(?im)^\s*require\s+product\s*=\s*([A-Za-z0-9._,-]+)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FlashPattern = new(
        @"(?i)\bfastboot(?:\.exe)?\s+(?:-[A-Za-z-]+\s+\S+\s+)*flash\s+([A-Za-z0-9_.-]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ErasePattern = new(
        @"(?i)\bfastboot(?:\.exe)?\s+(?:-[A-Za-z-]+\s+\S+\s+)*(?:erase|format)\b|\bfastboot(?:\.exe)?\s+-w\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex UnlockPattern = new(
        @"(?i)\bfastboot(?:\.exe)?\s+(?:flashing\s+unlock|oem\s+unlock|flashing\s+lock|oem\s+lock)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<XiaomiFastbootRomInspectionResult> InspectAsync(
        string archivePath,
        string? selectedDeviceProduct = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            throw new FileNotFoundException("حزمة Xiaomi Fastboot ROM غير موجودة.", archivePath);

        var fullPath = Path.GetFullPath(archivePath);
        var extension = Path.GetExtension(fullPath);
        var isGzip = extension.Equals(".tgz", StringComparison.OrdinalIgnoreCase) ||
                     fullPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
        if (!isGzip && !extension.Equals(".tar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("الصيغ المدعومة لفحص Xiaomi Fastboot ROM هي .tgz و.tar و.tar.gz فقط.");

        var info = new FileInfo(fullPath);
        if (info.Length is <= 0 or > MaximumArchiveBytes)
            throw new InvalidDataException("حجم حزمة Firmware فارغ أو يتجاوز 8 GiB.");

        string archiveHash;
        await using (var hashInput = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            archiveHash = Convert.ToHexString(await SHA256.HashDataAsync(hashInput, cancellationToken))
                .ToLowerInvariant();
        }

        var requiredProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imageFiles = new List<string>();
        var scriptFiles = new List<string>();
        var flashPartitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var containsErase = false;
        var containsUnlock = false;
        var findings = new List<string>();
        long expandedBytes = 0;
        var entryCount = 0;

        await using var archiveInput = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        Stream tarStream = isGzip
            ? new GZipStream(archiveInput, CompressionMode.Decompress, leaveOpen: false)
            : archiveInput;
        using (tarStream)
        using (var reader = new TarReader(tarStream, leaveOpen: false))
        {
            TarEntry? entry;
            while ((entry = reader.GetNextEntry(copyData: false)) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entryCount++;
                if (entryCount > MaximumEntries)
                    throw new InvalidDataException("عدد ملفات حزمة Fastboot ROM يتجاوز 20,000 مدخل.");

                var entryName = NormalizeEntryName(entry.Name);
                if (entry.EntryType == TarEntryType.Directory)
                    continue;
                if (entry.EntryType != TarEntryType.RegularFile)
                    throw new InvalidDataException("حزمة Fastboot ROM تحتوي رابطًا أو نوع ملف TAR غير مسموح: " + entryName);

                expandedBytes += entry.Length;
                if (entry.Length < 0 || expandedBytes > MaximumExpandedBytes)
                    throw new InvalidDataException("الحجم المفكوك لحزمة Fastboot ROM يتجاوز الحد الآمن.");

                if (entryName.StartsWith("images/", StringComparison.OrdinalIgnoreCase) &&
                    IsImageFile(entryName))
                {
                    imageFiles.Add(entryName);
                    if (imageFiles.Count > 10_000)
                        throw new InvalidDataException("حزمة Fastboot ROM تحتوي عددًا غير مدعوم من ملفات الصور.");
                }

                var baseName = Path.GetFileName(entryName);
                var isAndroidInfo = baseName.Equals("android-info.txt", StringComparison.OrdinalIgnoreCase);
                var isFlashScript = baseName.StartsWith("flash_all", StringComparison.OrdinalIgnoreCase) &&
                                    (baseName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) ||
                                     baseName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                                     baseName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));
                if (!isAndroidInfo && !isFlashScript)
                    continue;
                if (entry.Length > MaximumTextFileBytes)
                    throw new InvalidDataException("ملف تعليمات Firmware أكبر من 8 MiB: " + entryName);

                var text = await ReadEntryTextAsync(entry, cancellationToken);
                if (isAndroidInfo)
                {
                    foreach (Match match in ProductPattern.Matches(text))
                        requiredProducts.Add(match.Groups[1].Value.Trim());
                }
                if (isFlashScript)
                {
                    scriptFiles.Add(entryName);
                    foreach (Match match in FlashPattern.Matches(StripComments(text)))
                        flashPartitions.Add(match.Groups[1].Value);
                    var script = StripComments(text);
                    containsErase |= ErasePattern.IsMatch(script);
                    containsUnlock |= UnlockPattern.IsMatch(script);
                }
            }
        }

        var deviceProduct = string.IsNullOrWhiteSpace(selectedDeviceProduct)
            ? null
            : selectedDeviceProduct.Trim();
        var productMatch = requiredProducts.Count == 0 || deviceProduct is null
            ? XiaomiRomProductMatchState.Unknown
            : requiredProducts.Contains(deviceProduct)
                ? XiaomiRomProductMatchState.Match
                : XiaomiRomProductMatchState.Mismatch;

        if (requiredProducts.Count == 0)
            findings.Add("لم يتم العثور على require product داخل android-info.txt؛ توافق المنتج غير معروف.");
        else if (deviceProduct is null)
            findings.Add("لم يتم تقديم Product مقروء من Fastboot للجهاز الحالي؛ لم تتم مطابقة الموديل.");
        else if (productMatch == XiaomiRomProductMatchState.Match)
            findings.Add("Product الخاص بالجهاز يطابق أحد متطلبات الحزمة نصيًا؛ هذا لا يثبت CSC أو اللوحة أو المصدر أو Anti-Rollback.");
        else
            findings.Add("Product الخاص بالجهاز لا يطابق متطلبات الحزمة؛ اعتبر الملف غير متوافق ولا تستخدمه.");

        if (imageFiles.Count == 0)
            findings.Add("لم يتم العثور على ملفات صور داخل مجلد images/.");
        if (scriptFiles.Count == 0)
            findings.Add("لم يتم العثور على سكربت flash_all معروف؛ قد تكون الحزمة ناقصة أو بتنسيق مختلف.");
        if (containsErase)
            findings.Add("تم العثور على أوامر erase/format/-w في سكربت الحزمة؛ لم يتم تنفيذها.");
        if (containsUnlock)
            findings.Add("تم العثور على أوامر unlock/lock في سكربت الحزمة؛ لم يتم تنفيذها.");
        findings.Add("تمت قراءة أسماء الملفات وandroid-info وسكربتات الحزمة فقط؛ لم يتم استخراج الملفات أو تشغيل أي سكربت.");
        findings.Add("SHA-256 يحدد بايتات الملف ولا يثبت ناشر Firmware أو التوقيع أو التوافق الرسمي.");
        findings.Add("هذا الفاحص لا يسمح بالتفليش أو فتح Bootloader مطلقًا.");

        return new XiaomiFastbootRomInspectionResult(
            Path.GetFileName(fullPath),
            info.Length,
            archiveHash,
            requiredProducts.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            imageFiles.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            scriptFiles.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            flashPartitions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            containsErase,
            containsUnlock,
            deviceProduct,
            productMatch,
            FlashAllowed: false,
            UnlockAllowed: false,
            findings);
    }

    private static string NormalizeEntryName(string name)
    {
        var normalized = (name ?? "").Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(':') ||
            normalized.Split('/').Any(segment => segment is ".." or "." ) ||
            normalized.Any(char.IsControl))
            throw new InvalidDataException("حزمة Fastboot ROM تحتوي مسار TAR غير آمن.");
        return normalized;
    }

    private static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".img", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mbn", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".elf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".lz4", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadEntryTextAsync(TarEntry entry, CancellationToken cancellationToken)
    {
        var stream = entry.DataStream
            ?? throw new InvalidDataException("تعذر قراءة محتوى ملف تعليمات TAR.");
        await using (stream)
        using (var output = new MemoryStream())
        {
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0) break;
                if (output.Length + read > MaximumTextFileBytes)
                    throw new InvalidDataException("ملف تعليمات TAR تجاوز الحد الأقصى.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
                .GetString(output.ToArray());
        }
    }

    private static string StripComments(string text)
        => string.Join('\n', text.Split(['\r', '\n'])
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal)));
}
