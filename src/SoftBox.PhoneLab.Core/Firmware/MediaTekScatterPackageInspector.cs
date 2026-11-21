using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SoftBox.PhoneLab.Core.Firmware;

public sealed record MediaTekScatterPartition(
    string Name,
    string FileName,
    bool IsDownload,
    string Region,
    string PartitionType,
    long LinearStartAddress,
    long PhysicalStartAddress,
    long PartitionSize,
    long? ImageSizeBytes,
    string? ImageSha256);

public sealed record MediaTekScatterInspectionReport(
    string ScatterFile,
    string ScatterSha256,
    string Platform,
    string Project,
    IReadOnlyList<MediaTekScatterPartition> Partitions,
    IReadOnlyList<string> MissingImages,
    IReadOnlyList<string> Warnings,
    long TotalImageBytes,
    bool IsStructurallyValid);

/// <summary>
/// Offline parser for MediaTek scatter text files. It validates partition records and referenced
/// image paths, but does not authenticate a Download Agent, bypass DAA/SLA, open BROM or flash.
/// Secure-Boot DA authorization remains a model/OEM-specific requirement.
/// </summary>
public static class MediaTekScatterPackageInspector
{
    private const long MaximumScatterBytes = 8L * 1024 * 1024;
    private const long MaximumImageBytes = 16L * 1024 * 1024 * 1024;
    private const int MaximumPartitions = 2000;
    private static readonly Regex HeaderPattern = new(
        @"(?m)^\s*([A-Za-z0-9_]+)\s*:\s*(.*?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<MediaTekScatterInspectionReport> InspectAsync(
        string scatterPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scatterPath) || !File.Exists(scatterPath))
            throw new FileNotFoundException("ملف MediaTek scatter غير موجود.", scatterPath);
        var fullScatter = Path.GetFullPath(scatterPath);
        var scatterInfo = new FileInfo(fullScatter);
        if (scatterInfo.Length is <= 0 or > MaximumScatterBytes)
            throw new InvalidDataException("حجم scatter فارغ أو يتجاوز 8 MiB.");

        var scatterBytes = await File.ReadAllBytesAsync(fullScatter, cancellationToken);
        if (scatterBytes.Length > MaximumScatterBytes)
            throw new InvalidDataException("حجم scatter تجاوز 8 MiB أثناء القراءة.");
        var scatterSha256 = Convert.ToHexString(SHA256.HashData(scatterBytes)).ToLowerInvariant();
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(scatterBytes).TrimStart('\uFEFF');
        if (text.Length > MaximumScatterBytes || text.Any(character => character == '\0'))
            throw new InvalidDataException("ملف scatter غير صالح أو يتجاوز الحد.");
        var root = Path.GetDirectoryName(fullScatter)!;
        var platform = ReadHeader(text, "platform");
        var project = ReadHeader(text, "project");
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(project))
            throw new InvalidDataException("ملف scatter لا يحتوي platform وproject؛ لا يمكن تحديد منصة الحزمة.");

        var partitions = new List<MediaTekScatterPartition>();
        var missing = new List<string>();
        var warnings = new List<string>();
        var sections = Regex.Split(text, @"(?m)^\s*-\s*partition_index\s*:")
            .Skip(1).ToArray();
        if (sections.Length is < 1 or > MaximumPartitions)
            throw new InvalidDataException("عدد أقسام scatter خارج الحدود المدعومة.");

        long totalBytes = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = HeaderPattern.Matches(section)
                .Cast<Match>()
                .GroupBy(match => match.Groups[1].Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Groups[2].Value.Trim(), StringComparer.OrdinalIgnoreCase);

            var name = Required(fields, "partition_name");
            var fileName = fields.TryGetValue("file_name", out var fileValue) ? fileValue : "NONE";
            var isDownload = fields.TryGetValue("is_download", out var download) &&
                             download.Equals("true", StringComparison.OrdinalIgnoreCase);
            var region = fields.TryGetValue("region", out var regionValue) ? regionValue : "UNKNOWN";
            var partitionType = fields.TryGetValue("type", out var typeValue) ? typeValue : "UNKNOWN";
            var linear = ParseAddress(fields, "linear_start_addr");
            var physical = ParseAddress(fields, "physical_start_addr");
            var size = ParseAddress(fields, "partition_size");

            if (!names.Add(name))
                throw new InvalidDataException("اسم قسم scatter مكرر: " + name);
            if (size <= 0)
                throw new InvalidDataException("حجم قسم scatter غير صالح: " + name);

            long? imageSize = null;
            string? imageHash = null;
            if (isDownload && !fileName.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                var imagePath = ResolveSafeChild(root, fileName);
                if (!File.Exists(imagePath))
                    missing.Add(fileName);
                else
                {
                    var image = new FileInfo(imagePath);
                    if (image.Length is <= 0 or > MaximumImageBytes)
                        throw new InvalidDataException("حجم صورة MediaTek فارغ أو يتجاوز 16 GiB: " + fileName);
                    imageSize = image.Length;
                    imageHash = await CalculateSha256Async(imagePath, cancellationToken);
                    totalBytes = checked(totalBytes + image.Length);
                }
            }
            else if (isDownload)
            {
                warnings.Add("القسم " + name + " محدد للتنزيل دون اسم ملف صورة.");
            }

            partitions.Add(new MediaTekScatterPartition(
                name, fileName, isDownload, region, partitionType,
                linear, physical, size, imageSize, imageHash));
        }

        if (missing.Count > 0)
            warnings.Add("واحدة أو أكثر من صور Firmware المشار إليها في scatter مفقودة.");
        if (!partitions.Any(partition => partition.IsDownload && partition.ImageSizeBytes is > 0))
            throw new InvalidDataException("لا توجد أي صورة تنزيل حقيقية مرتبطة بملف scatter.");

        return new MediaTekScatterInspectionReport(
            fullScatter,
            scatterSha256,
            platform,
            project,
            partitions,
            missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings,
            totalBytes,
            IsStructurallyValid: missing.Count == 0);
    }

    private static string ReadHeader(string text, string key)
    {
        foreach (Match match in HeaderPattern.Matches(text))
        {
            if (match.Groups[1].Value.Equals(key, StringComparison.OrdinalIgnoreCase))
                return match.Groups[2].Value.Trim();
        }
        return "";
    }

    private static string Required(IReadOnlyDictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("خاصية scatter مفقودة: " + key);
        return value;
    }

    private static long ParseAddress(IReadOnlyDictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value))
            return 0;
        var normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(normalized[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                return hex;
        }
        else if (long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return decimalValue;
        }
        throw new InvalidDataException("قيمة scatter address غير صالحة: " + key);
    }

    private static string ResolveSafeChild(string root, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(':') ||
            normalized.Any(char.IsControl) ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("اسم صورة scatter غير آمن.");
        var full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("صورة scatter تخرج من مجلد الحزمة.");
        return full;
    }

    private static async Task<string> CalculateSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
