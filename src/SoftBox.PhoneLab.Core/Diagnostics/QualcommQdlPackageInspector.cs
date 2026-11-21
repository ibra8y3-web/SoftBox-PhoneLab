using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace SoftBox.PhoneLab.Core.Diagnostics;

public enum QualcommQdlStorageType
{
    Emmc,
    Ufs,
    Nand,
    Nvme,
    Spinor
}

public sealed record QualcommQdlImage(
    string RelativePath,
    long SizeBytes,
    string Sha256);

public sealed record QualcommQdlProgramEntry(
    string PartitionLabel,
    string FileName,
    int PhysicalPartition,
    long StartSector,
    long SectorCount,
    int SectorSizeBytes);

public sealed record QualcommQdlPackageReport(
    string PackageDirectory,
    string ProgrammerPath,
    long ProgrammerSizeBytes,
    string ProgrammerSha256,
    IReadOnlyList<string> RawProgramXml,
    IReadOnlyList<string> PatchXml,
    IReadOnlyList<QualcommQdlProgramEntry> ProgramEntries,
    IReadOnlyList<QualcommQdlImage> Images,
    IReadOnlyList<QualcommQdlImage> XmlFiles,
    IReadOnlyList<string> Warnings,
    bool IsStructurallyValid);

/// <summary>
/// Strict offline inspection for Qualcomm QDL/Firehose packages.
/// It parses rawprogram and patch XML with DTD/external entities disabled, resolves every
/// referenced image inside the selected package directory, and records SHA-256 hashes.
/// It does not manufacture a Firehose programmer or override device Secure Boot/EDL authorization.
/// </summary>
public static class QualcommQdlPackageInspector
{
    private const long MaximumXmlBytes = 8L * 1024 * 1024;
    private const long MaximumProgrammerBytes = 256L * 1024 * 1024;
    private const int MaximumXmlEntries = 100_000;
    private const long MaximumImageBytes = 16L * 1024 * 1024 * 1024;

    public static async Task<QualcommQdlPackageReport> InspectAsync(
        string packageDirectory,
        string programmerPath,
        IEnumerable<string> rawProgramXmlPaths,
        IEnumerable<string> patchXmlPaths,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory) || !Directory.Exists(packageDirectory))
            throw new DirectoryNotFoundException("مجلد حزمة Qualcomm غير موجود.");
        var root = Path.GetFullPath(packageDirectory);
        if (string.IsNullOrWhiteSpace(programmerPath) || !File.Exists(programmerPath))
            throw new FileNotFoundException("ملف Firehose Programmer غير موجود.", programmerPath);

        var programmer = Path.GetFullPath(programmerPath);
        var programmerName = Path.GetFileName(programmer);
        if (!programmerName.StartsWith("prog_firehose", StringComparison.OrdinalIgnoreCase) ||
            !(programmerName.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
              programmerName.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("اختر ملف prog_firehose*.elf أو prog_firehose*.mbn من حزمة OEM.");
        var programmerInfo = new FileInfo(programmer);
        if (programmerInfo.Length is <= 0 or > MaximumProgrammerBytes)
            throw new InvalidDataException("حجم Firehose Programmer فارغ أو يتجاوز 256 MiB.");

        var rawPrograms = NormalizeSelectedXml(root, rawProgramXmlPaths, "rawprogram");
        var patches = NormalizeSelectedXml(root, patchXmlPaths, "patch");
        if (rawPrograms.Count == 0)
            throw new InvalidDataException("حدد ملف rawprogram واحدًا على الأقل. ملفات patch اختيارية إذا لم توفرها حزمة OEM.");

        var programmerHash = await CalculateSha256Async(programmer, cancellationToken);
        var entries = new List<QualcommQdlProgramEntry>();
        var images = new Dictionary<string, QualcommQdlImage>(StringComparer.OrdinalIgnoreCase);
        var xmlFiles = new Dictionary<string, QualcommQdlImage>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var xmlPath in rawPrograms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xmlInfo = new FileInfo(xmlPath);
            xmlFiles[xmlPath] = new QualcommQdlImage(
                NormalizeRelative(Path.GetRelativePath(root, xmlPath)),
                xmlInfo.Length,
                await CalculateSha256Async(xmlPath, cancellationToken));
            var document = await LoadSafeXmlAsync(xmlPath, cancellationToken);
            var programNodes = document.Descendants()
                .Where(element => element.Name.LocalName.Equals("program", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (programNodes.Length == 0)
                throw new InvalidDataException("ملف rawprogram لا يحتوي عناصر program: " + Path.GetFileName(xmlPath));
            if (entries.Count + programNodes.Length > MaximumXmlEntries)
                throw new InvalidDataException("عدد مناطق البرمجة يتجاوز الحد الآمن.");

            foreach (var node in programNodes)
            {
                var label = RequiredAttribute(node, "label");
                var fileName = RequiredAttribute(node, "filename");
                var physicalPartition = ParseInteger(RequiredAttribute(node, "physical_partition_number"), "physical_partition_number");
                var startSector = ParseInteger(RequiredAttribute(node, "start_sector"), "start_sector");
                var sectorCount = ParseInteger(RequiredAttribute(node, "num_partition_sectors"), "num_partition_sectors");
                var sectorSize = ParseInteger(RequiredAttribute(node, "SECTOR_SIZE_IN_BYTES"), "SECTOR_SIZE_IN_BYTES");
                if (physicalPartition < 0 || physicalPartition > 32 || startSector < 0 ||
                    sectorCount <= 0 || sectorSize is not (512 or 4096))
                    throw new InvalidDataException("قيم منطقة Firehose غير صالحة للقطاع: " + label);
                entries.Add(new QualcommQdlProgramEntry(
                    label, fileName, checked((int)physicalPartition), startSector, sectorCount, checked((int)sectorSize)));

                if (fileName.Equals("DISK", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                    continue;

                var imagePath = ResolveSafeChild(root, fileName);
                if (!File.Exists(imagePath))
                    throw new FileNotFoundException("ملف Image مشار إليه في rawprogram غير موجود: " + fileName, imagePath);
                var info = new FileInfo(imagePath);
                if (info.Length is <= 0 or > MaximumImageBytes)
                    throw new InvalidDataException("حجم صورة Firehose فارغ أو يتجاوز 16 GiB: " + fileName);
                if (!images.ContainsKey(imagePath))
                {
                    images.Add(imagePath, new QualcommQdlImage(
                        NormalizeRelative(Path.GetRelativePath(root, imagePath)),
                        info.Length,
                        await CalculateSha256Async(imagePath, cancellationToken)));
                }
            }
        }

        foreach (var patchPath in patches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xmlInfo = new FileInfo(patchPath);
            xmlFiles[patchPath] = new QualcommQdlImage(
                NormalizeRelative(Path.GetRelativePath(root, patchPath)),
                xmlInfo.Length,
                await CalculateSha256Async(patchPath, cancellationToken));
            var document = await LoadSafeXmlAsync(patchPath, cancellationToken);
            var patchNodes = document.Descendants()
                .Where(element => element.Name.LocalName.Equals("patch", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (patchNodes.Length == 0)
                throw new InvalidDataException("ملف patch لا يحتوي عناصر patch: " + Path.GetFileName(patchPath));
            if (patchNodes.Length > MaximumXmlEntries)
                throw new InvalidDataException("عدد تعليمات patch يتجاوز الحد الآمن.");
            foreach (var node in patchNodes)
            {
                _ = RequiredAttribute(node, "physical_partition_number");
                _ = RequiredAttribute(node, "start_sector");
                _ = RequiredAttribute(node, "byte_offset");
                _ = RequiredAttribute(node, "size_in_bytes");
            }
        }

        if (entries.Count == 0)
            throw new InvalidDataException("لم يتم العثور على أي مناطق كتابة في ملفات rawprogram.");
        if (entries.GroupBy(entry => (entry.PhysicalPartition, entry.StartSector))
            .Any(group => group.Count() > 1))
            warnings.Add("توجد مناطق rawprogram تبدأ من sector مكرر؛ راجع توزيعها مع مصدر الحزمة قبل الاستخدام.");

        return new QualcommQdlPackageReport(
            root,
            programmer,
            programmerInfo.Length,
            programmerHash,
            rawPrograms,
            patches,
            entries,
            images.Values.OrderBy(image => image.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            xmlFiles.Values.OrderBy(image => image.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings,
            IsStructurallyValid: true);
    }

    public static IReadOnlyList<string> BuildFlashArguments(
        QualcommQdlPackageReport report,
        string edlSerial,
        QualcommQdlStorageType storageType,
        bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!report.IsStructurallyValid)
            throw new InvalidOperationException("حزمة Qualcomm لم تجتز فحص البنية.");
        if (string.IsNullOrWhiteSpace(edlSerial) || edlSerial.Length > 128 ||
            edlSerial.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            throw new ArgumentException("EDL serial غير صالح.", nameof(edlSerial));

        var storage = storageType switch
        {
            QualcommQdlStorageType.Emmc => "emmc",
            QualcommQdlStorageType.Ufs => "ufs",
            QualcommQdlStorageType.Nand => "nand",
            QualcommQdlStorageType.Nvme => "nvme",
            QualcommQdlStorageType.Spinor => "spinor",
            _ => throw new ArgumentOutOfRangeException(nameof(storageType))
        };
        var arguments = new List<string> { "--storage=" + storage };
        if (dryRun)
            arguments.Add("--dry-run");
        arguments.Add("--serial=" + edlSerial);
        arguments.Add(report.ProgrammerPath);
        arguments.AddRange(report.RawProgramXml);
        arguments.AddRange(report.PatchXml);
        return arguments;
    }

    private static List<string> NormalizeSelectedXml(string root, IEnumerable<string> paths, string prefix)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var selected = paths.Select(path =>
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("ملف Qualcomm XML غير موجود.", path);
            var full = Path.GetFullPath(path);
            if (!IsChildPath(root, full) ||
                !Path.GetExtension(full).Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("مسار XML لا يطابق مجلد الحزمة أو نوع الملف المتوقع: " + path);
            var length = new FileInfo(full).Length;
            if (length <= 0 || length > MaximumXmlBytes)
                throw new InvalidDataException("حجم XML فارغ أو أكبر من 8 MiB: " + Path.GetFileName(full));
            return full;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (selected.Count > 100)
            throw new InvalidDataException("عدد ملفات Qualcomm XML يتجاوز الحد 100 ملف.");
        return selected;
    }

    private static async Task<XDocument> LoadSafeXmlAsync(string path, CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            Async = true
        };
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = XmlReader.Create(stream, settings);
        return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        var value = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 || value.Any(char.IsControl))
            throw new InvalidDataException("خاصية XML المطلوبة غير صالحة: " + name);
        return value;
    }

    private static long ParseInteger(string value, string name)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                return hex;
        }
        else if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        throw new InvalidDataException("قيمة XML رقمية غير صالحة: " + name);
    }

    private static string ResolveSafeChild(string root, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(':') || normalized.Any(char.IsControl) ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("اسم صورة rawprogram غير آمن.");
        var full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsChildPath(root, full))
            throw new InvalidDataException("ملف صورة rawprogram يخرج من مجلد الحزمة.");
        return full;
    }

    private static bool IsChildPath(string parent, string child)
    {
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
        return Path.GetFullPath(child).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/');

    private static async Task<string> CalculateSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
