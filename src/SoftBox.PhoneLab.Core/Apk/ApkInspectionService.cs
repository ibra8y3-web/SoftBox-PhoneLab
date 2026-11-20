using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SoftBox.PhoneLab.Core.Apk;

public enum ApkManifestFormat
{
    Unavailable,
    PlainXml,
    AndroidBinaryXml
}

/// <summary>
/// Results of a local, read-only APK archive inspection.
/// Signature metadata is discovery only; it is not a cryptographic trust verdict.
/// </summary>
public sealed record ApkInspectionResult(
    string FileName,
    long FileSizeBytes,
    string Sha256,
    int EntryCount,
    IReadOnlyList<string> Entries,
    string? PackageName,
    string? ApplicationLabel,
    string? VersionName,
    long? VersionCode,
    int? MinSdkVersion,
    int? TargetSdkVersion,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> SignatureMetadataFiles,
    bool ApkSigningBlockDetected,
    bool ArchiveEntriesReadable,
    ApkManifestFormat ManifestFormat,
    IReadOnlyList<string> Warnings)
{
    public bool HasV1SignatureFiles => SignatureMetadataFiles.Any(path =>
        path.EndsWith(".RSA", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".DSA", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".EC", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Inspects APK files without installing them or communicating with a connected device.
/// It validates ZIP entry readability, computes SHA-256 and reads text or Android binary XML manifests.
/// </summary>
public sealed class ApkInspectionService
{
    private const long MaximumExpandedArchiveBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumManifestBytes = 8 * 1024 * 1024;
    private const int MaximumDisplayedEntries = 500;
    private const ushort XmlResourceMapChunk = 0x0180;
    private const ushort XmlStringPoolChunk = 0x0001;
    private const ushort XmlStartElementChunk = 0x0102;
    private const ushort XmlDocumentChunk = 0x0003;

    public async Task<ApkInspectionResult> InspectAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("حدد مسار ملف APK.", nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);
        if (!Path.GetExtension(fullPath).Equals(".apk", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("يجب اختيار ملف بامتداد .apk.", nameof(filePath));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("ملف APK غير موجود.", fullPath);

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length == 0)
            throw new InvalidDataException("ملف APK فارغ.");

        string sha256;
        await using (var hashStream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var hash = await SHA256.HashDataAsync(hashStream, cancellationToken);
            sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        }

        var displayedEntries = new List<string>();
        var signatureEntries = new List<string>();
        byte[]? manifestBytes = null;
        long expandedBytes = 0;
        var entryCount = 0;

        await using (var archiveStream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false))
        {
            entryCount = archive.Entries.Count;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (displayedEntries.Count < MaximumDisplayedEntries)
                    displayedEntries.Add(entry.FullName);

                if (IsSignatureMetadata(entry.FullName) && signatureEntries.Count < 100)
                    signatureEntries.Add(entry.FullName);

                var isManifest = entry.FullName.Equals("AndroidManifest.xml", StringComparison.OrdinalIgnoreCase);
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    continue;

                if (isManifest && entry.Length > MaximumManifestBytes)
                    throw new InvalidDataException("حجم AndroidManifest.xml يتجاوز الحد الآمن للتحليل.");

                using var capturedManifest = isManifest ? new MemoryStream() : null;
                await using var entryStream = entry.Open();
                var buffer = new byte[64 * 1024];
                long actualEntryBytes = 0;

                while (true)
                {
                    var read = await entryStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0)
                        break;

                    actualEntryBytes += read;
                    expandedBytes += read;
                    if (expandedBytes > MaximumExpandedArchiveBytes)
                        throw new InvalidDataException("الحجم المفكوك للأرشيف يتجاوز الحد الآمن البالغ 2 GiB.");

                    if (capturedManifest is not null)
                    {
                        if (capturedManifest.Length + read > MaximumManifestBytes)
                            throw new InvalidDataException("حجم AndroidManifest.xml يتجاوز الحد الآمن للتحليل.");
                        await capturedManifest.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }

                if (actualEntryBytes != entry.Length)
                    throw new InvalidDataException($"حجم مدخل ZIP لا يطابق بياناته الوصفية: {entry.FullName}");

                if (capturedManifest is not null && manifestBytes is null)
                    manifestBytes = capturedManifest.ToArray();
            }
        }

        var warnings = new List<string>
        {
            "فحص SHA-256 وسلامة قراءة ZIP لا يثبتان أن التطبيق آمن أو صادر عن ناشر موثوق."
        };

        var manifestInfo = new ManifestInfo();
        var manifestFormat = ApkManifestFormat.Unavailable;
        if (manifestBytes is null)
        {
            warnings.Add("لم يتم العثور على AndroidManifest.xml داخل الأرشيف.");
        }
        else
        {
            try
            {
                (manifestInfo, manifestFormat) = ParseManifest(manifestBytes);
            }
            catch (Exception ex) when (ex is InvalidDataException or XmlException or FormatException or ArgumentException)
            {
                warnings.Add("تعذر تحليل AndroidManifest.xml: " + ex.Message);
            }
        }

        var signingBlockDetected = DetectApkSigningBlock(fullPath);
        if (!signatureEntries.Any(path =>
                path.EndsWith(".RSA", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".DSA", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".EC", StringComparison.OrdinalIgnoreCase)) &&
            !signingBlockDetected)
        {
            warnings.Add("لم يتم رصد بيانات توقيع APK v1 أو APK Signing Block. لم يتم إصدار حكم تشفيري على التوقيع.");
        }
        else
        {
            warnings.Add("تم رصد بيانات أو بنية توقيع فقط؛ لم يتم التحقق تشفيريًا من الشهادة أو سلسلة الثقة أو صلاحية التوقيع.");
        }

        if (displayedEntries.Count < entryCount)
            warnings.Add($"تم عرض أول {MaximumDisplayedEntries} مدخلًا فقط من أصل {entryCount} مدخلًا.");

        return new ApkInspectionResult(
            Path.GetFileName(fullPath),
            fileInfo.Length,
            sha256,
            entryCount,
            displayedEntries,
            manifestInfo.PackageName,
            manifestInfo.ApplicationLabel,
            manifestInfo.VersionName,
            manifestInfo.VersionCode,
            manifestInfo.MinSdkVersion,
            manifestInfo.TargetSdkVersion,
            manifestInfo.Permissions.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            signatureEntries,
            signingBlockDetected,
            ArchiveEntriesReadable: true,
            manifestFormat,
            warnings);
    }

    private static bool IsSignatureMetadata(string path)
    {
        if (!path.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            return false;

        var extension = Path.GetExtension(path);
        return extension.Equals(".SF", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".RSA", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".DSA", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".EC", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).Equals("MANIFEST.MF", StringComparison.OrdinalIgnoreCase);
    }

    private static (ManifestInfo Info, ApkManifestFormat Format) ParseManifest(byte[] bytes)
    {
        var first = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            first = 3;
        while (first < bytes.Length && char.IsWhiteSpace((char)bytes[first]))
            first++;

        if (first < bytes.Length && bytes[first] == (byte)'<')
            return (ParseTextManifest(bytes), ApkManifestFormat.PlainXml);

        if (bytes.Length >= 8 && ReadUInt16(bytes, 0) == XmlDocumentChunk)
            return (ParseBinaryManifest(bytes), ApkManifestFormat.AndroidBinaryXml);

        throw new InvalidDataException("صيغة Manifest ليست XML نصيًا أو Android Binary XML معروفة.");
    }

    private static ManifestInfo ParseTextManifest(byte[] bytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumManifestBytes,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root ?? throw new InvalidDataException("عنصر Manifest الرئيسي غير موجود.");
        var info = new ManifestInfo();

        info.ApplyElement(root.Name.LocalName, GetAttributes(root));
        foreach (var element in root.Elements())
        {
            var attributes = GetAttributes(element);
            info.ApplyElement(element.Name.LocalName, attributes);
        }

        return info;
    }

    private static Dictionary<string, string?> GetAttributes(XElement element)
        => element.Attributes()
            .GroupBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (string?)group.Last().Value, StringComparer.Ordinal);

    private static ManifestInfo ParseBinaryManifest(byte[] bytes)
    {
        if (bytes.Length < 8)
            throw new InvalidDataException("ترويسة Android Binary XML غير مكتملة.");

        var declaredSize = ReadUInt32(bytes, 4);
        if (declaredSize < 8 || declaredSize > bytes.Length)
            throw new InvalidDataException("حجم Android Binary XML غير صالح.");

        var documentLength = checked((int)declaredSize);
        var offset = (int)ReadUInt16(bytes, 2);
        if (offset < 8 || offset > documentLength)
            throw new InvalidDataException("حجم ترويسة Android Binary XML غير صالح.");

        string[]? strings = null;
        var info = new ManifestInfo();

        while (offset < documentLength)
        {
            EnsureRange(offset, 8, documentLength);
            var chunkType = ReadUInt16(bytes, offset);
            var headerSize = ReadUInt16(bytes, offset + 2);
            var chunkSizeRaw = ReadUInt32(bytes, offset + 4);
            if (headerSize < 8 || chunkSizeRaw < headerSize || chunkSizeRaw > int.MaxValue)
                throw new InvalidDataException("بنية أحد مقاطع Android Binary XML غير صالحة.");

            var chunkSize = (int)chunkSizeRaw;
            EnsureRange(offset, chunkSize, documentLength);

            if (chunkType == XmlStringPoolChunk)
                strings = ReadStringPool(bytes, offset, headerSize, chunkSize);
            else if (chunkType == XmlStartElementChunk)
            {
                if (strings is null)
                    throw new InvalidDataException("مقطع String Pool يسبق عناصر Manifest بشكل غير صالح.");
                ParseStartElement(bytes, offset, headerSize, chunkSize, strings, info);
            }
            else if (chunkType == XmlResourceMapChunk)
            {
                // Resource IDs are retained as unresolved references; no resource table is inferred.
            }

            offset += chunkSize;
        }

        return info;
    }

    private static string[] ReadStringPool(byte[] bytes, int chunkOffset, int headerSize, int chunkSize)
    {
        if (headerSize < 28 || chunkSize < headerSize)
            throw new InvalidDataException("ترويسة String Pool غير صالحة.");

        var stringCount = ReadUInt32(bytes, chunkOffset + 8);
        var flags = ReadUInt32(bytes, chunkOffset + 16);
        var stringsStart = ReadUInt32(bytes, chunkOffset + 20);
        if (stringCount > 100_000 || stringsStart > chunkSize)
            throw new InvalidDataException("بيانات String Pool تتجاوز الحدود المسموح بها.");

        var offsetsStart = checked(chunkOffset + headerSize);
        var offsetsBytes = checked((int)stringCount * 4);
        EnsureRange(offsetsStart, offsetsBytes, chunkOffset + chunkSize);

        var stringDataStart = checked(chunkOffset + (int)stringsStart);
        var chunkEnd = checked(chunkOffset + chunkSize);
        var isUtf8 = (flags & 0x00000100) != 0;
        var values = new string[(int)stringCount];

        for (var i = 0; i < values.Length; i++)
        {
            var relativeOffset = ReadUInt32(bytes, offsetsStart + (i * 4));
            if (relativeOffset > int.MaxValue)
                throw new InvalidDataException("إزاحة نص في String Pool غير صالحة.");

            var position = checked(stringDataStart + (int)relativeOffset);
            if (position < stringDataStart || position >= chunkEnd)
                throw new InvalidDataException("نص في String Pool خارج حدود المقطع.");

            if (isUtf8)
            {
                _ = ReadLength8(bytes, ref position, chunkEnd);
                var byteLength = ReadLength8(bytes, ref position, chunkEnd);
                EnsureRange(position, byteLength, chunkEnd);
                values[i] = Encoding.UTF8.GetString(bytes, position, byteLength);
            }
            else
            {
                var characterLength = ReadLength16(bytes, ref position, chunkEnd);
                var byteLength = checked(characterLength * 2);
                EnsureRange(position, byteLength, chunkEnd);
                values[i] = Encoding.Unicode.GetString(bytes, position, byteLength);
            }
        }

        return values;
    }

    private static int ReadLength8(byte[] bytes, ref int position, int limit)
    {
        EnsureRange(position, 1, limit);
        var first = bytes[position++];
        if ((first & 0x80) == 0)
            return first;

        EnsureRange(position, 1, limit);
        return ((first & 0x7F) << 8) | bytes[position++];
    }

    private static int ReadLength16(byte[] bytes, ref int position, int limit)
    {
        EnsureRange(position, 2, limit);
        var first = ReadUInt16(bytes, position);
        position += 2;
        if ((first & 0x8000) == 0)
            return first;

        EnsureRange(position, 2, limit);
        var second = ReadUInt16(bytes, position);
        position += 2;
        return ((first & 0x7FFF) << 16) | second;
    }

    private static void ParseStartElement(
        byte[] bytes,
        int chunkOffset,
        int headerSize,
        int chunkSize,
        string[] strings,
        ManifestInfo info)
    {
        var extension = checked(chunkOffset + headerSize);
        var chunkEnd = checked(chunkOffset + chunkSize);
        EnsureRange(extension, 20, chunkEnd);

        var nameIndex = ReadUInt32(bytes, extension + 4);
        var elementName = GetPoolString(strings, nameIndex);
        if (string.IsNullOrWhiteSpace(elementName))
            return;

        var attributeStart = ReadUInt16(bytes, extension + 8);
        var attributeSize = ReadUInt16(bytes, extension + 10);
        var attributeCount = ReadUInt16(bytes, extension + 12);
        if (attributeStart < 20 || attributeSize < 20 || attributeCount > 4096)
            throw new InvalidDataException("بنية خصائص عنصر Manifest غير صالحة.");

        var attributesOffset = checked(extension + attributeStart);
        var attributesBytes = checked(attributeCount * attributeSize);
        EnsureRange(attributesOffset, attributesBytes, chunkEnd);

        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < attributeCount; i++)
        {
            var attributeOffset = checked(attributesOffset + (i * attributeSize));
            var attributeName = GetPoolString(strings, ReadUInt32(bytes, attributeOffset + 4));
            if (string.IsNullOrWhiteSpace(attributeName))
                continue;

            var rawValueIndex = ReadUInt32(bytes, attributeOffset + 8);
            var rawValue = rawValueIndex == uint.MaxValue ? null : GetPoolString(strings, rawValueIndex);
            var dataType = bytes[attributeOffset + 15];
            var data = ReadUInt32(bytes, attributeOffset + 16);
            attributes[attributeName] = rawValue ?? DecodeTypedValue(dataType, data, strings);
        }

        info.ApplyElement(elementName, attributes);
    }

    private static string? DecodeTypedValue(byte dataType, uint data, string[] strings)
        => dataType switch
        {
            0x01 => $"@0x{data:X8}",
            0x02 => $"?0x{data:X8}",
            0x03 => GetPoolString(strings, data),
            0x10 => data.ToString(CultureInfo.InvariantCulture),
            0x11 => $"0x{data:X8}",
            0x12 => data == 0 ? "false" : "true",
            _ => null
        };

    private static string? GetPoolString(string[] strings, uint index)
        => index == uint.MaxValue || index > int.MaxValue || index >= strings.Length
            ? null
            : strings[(int)index];

    private static bool DetectApkSigningBlock(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 22)
                return false;

            var tailLength = (int)Math.Min(stream.Length, 65_557);
            var tail = new byte[tailLength];
            stream.Position = stream.Length - tailLength;
            stream.ReadExactly(tail);

            var eocdOffset = -1;
            for (var i = tail.Length - 22; i >= 0; i--)
            {
                if (ReadUInt32(tail, i) != 0x06054b50)
                    continue;

                var commentLength = ReadUInt16(tail, i + 20);
                if (i + 22 + commentLength == tail.Length)
                {
                    eocdOffset = i;
                    break;
                }
            }

            if (eocdOffset < 0)
                return false;

            var centralDirectoryOffset = ReadUInt32(tail, eocdOffset + 16);
            if (centralDirectoryOffset == uint.MaxValue || centralDirectoryOffset < 24 ||
                centralDirectoryOffset > stream.Length - 24)
                return false;

            stream.Position = centralDirectoryOffset - 24;
            Span<byte> trailer = stackalloc byte[24];
            stream.ReadExactly(trailer);
            if (!Encoding.ASCII.GetString(trailer[8..]) .Equals("APK Sig Block 42", StringComparison.Ordinal))
                return false;

            var blockSize = BinaryPrimitives.ReadUInt64LittleEndian(trailer[..8]);
            if (blockSize < 24 || blockSize > (ulong)(centralDirectoryOffset - 8))
                return false;

            var blockStart = centralDirectoryOffset - checked((long)blockSize) - 8;
            if (blockStart < 0)
                return false;

            stream.Position = blockStart;
            Span<byte> firstSize = stackalloc byte[8];
            stream.ReadExactly(firstSize);
            return BinaryPrimitives.ReadUInt64LittleEndian(firstSize) == blockSize;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            return false;
        }
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        EnsureRange(offset, 2, bytes.Length);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        EnsureRange(offset, 4, bytes.Length);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    }

    private static void EnsureRange(int offset, int length, int limit)
    {
        if (offset < 0 || length < 0 || offset > limit - length)
            throw new InvalidDataException("بيانات Android Manifest غير مكتملة أو خارج حدود الملف.");
    }

    private sealed class ManifestInfo
    {
        public string? PackageName { get; private set; }
        public string? ApplicationLabel { get; private set; }
        public string? VersionName { get; private set; }
        public long? VersionCode { get; private set; }
        public int? MinSdkVersion { get; private set; }
        public int? TargetSdkVersion { get; private set; }
        public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);

        public void ApplyElement(string elementName, IReadOnlyDictionary<string, string?> attributes)
        {
            switch (elementName)
            {
                case "manifest":
                    PackageName = Get(attributes, "package") ?? PackageName;
                    VersionName = Get(attributes, "versionName") ?? VersionName;
                    var low = ParseUInt32(Get(attributes, "versionCode"));
                    var high = ParseUInt32(Get(attributes, "versionCodeMajor"));
                    if (low.HasValue || high.HasValue)
                        VersionCode = ((long)(high ?? 0) << 32) | (low ?? 0);
                    break;

                case "uses-sdk":
                    MinSdkVersion = ParseInt32(Get(attributes, "minSdkVersion")) ?? MinSdkVersion;
                    TargetSdkVersion = ParseInt32(Get(attributes, "targetSdkVersion")) ?? TargetSdkVersion;
                    break;

                case "application":
                    ApplicationLabel = Get(attributes, "label") ?? ApplicationLabel;
                    break;

                case "uses-permission":
                case "uses-permission-sdk-23":
                case "uses-permission-sdk-m":
                case "uses-permission-sdk-33":
                    var permission = Get(attributes, "name");
                    if (!string.IsNullOrWhiteSpace(permission))
                        Permissions.Add(permission);
                    break;
            }
        }

        private static string? Get(IReadOnlyDictionary<string, string?> attributes, string name)
            => attributes.TryGetValue(name, out var value) ? value : null;

        private static uint? ParseUInt32(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                return hex;

            return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
        }

        private static int? ParseInt32(string? value)
        {
            var parsed = ParseUInt32(value);
            return parsed.HasValue && parsed.Value <= int.MaxValue ? (int)parsed.Value : null;
        }
    }
}
