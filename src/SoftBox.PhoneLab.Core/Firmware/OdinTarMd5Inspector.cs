using System.Formats.Tar;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Firmware;

public sealed record OdinTarMd5Inspection(
    string FileName,
    long SizeBytes,
    bool IsTarReadable,
    bool HasAppendedMd5,
    bool AppendedMd5Valid,
    string? ExpectedMd5,
    string? ActualMd5,
    IReadOnlyList<string> EntryNames,
    IReadOnlyList<string> PartitionPayloads,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => IsTarReadable && Errors.Count == 0 &&
        (!HasAppendedMd5 || AppendedMd5Valid);
}

/// <summary>
/// Read-only Samsung Odin TAR/TAR.MD5 inspection. It verifies the appended MD5
/// when present and reads TAR entry names without extracting or modifying data.
/// This is package-integrity inspection, not publisher authentication or device compatibility.
/// </summary>
public static class OdinTarMd5Inspector
{
    private const long MaximumPackageBytes = 16L * 1024 * 1024 * 1024;
    private const int MaximumTailBytes = 256;
    private const int MaximumEntries = 100_000;
    private static readonly Regex HashSuffix = new(@"([0-9a-fA-F]{32})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<OdinTarMd5Inspection> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("ملف Odin المحدد غير موجود.", path);

        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (info.Length <= 0 || info.Length > MaximumPackageBytes)
            throw new InvalidDataException("حجم حزمة Odin فارغ أو يتجاوز الحد 16 GiB.");

        var errors = new List<string>();
        var warnings = new List<string>();
        var expectedMd5 = default(string);
        var actualMd5 = default(string);
        var hasMd5 = Path.GetFileName(fullPath).EndsWith(".tar.md5", StringComparison.OrdinalIgnoreCase);
        var tarLength = info.Length;
        var md5Valid = false;

        await using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess))
        {
            if (hasMd5)
            {
                var suffix = await ReadMd5SuffixAsync(stream, cancellationToken);
                if (suffix is null)
                {
                    errors.Add("ملف TAR.MD5 لا يحتوي على بصمة MD5 سداسية عشرية صالحة في نهايته.");
                }
                else
                {
                    expectedMd5 = suffix.Value.Hash;
                    tarLength = suffix.Value.PayloadLength;
                    actualMd5 = await CalculateMd5Async(stream, tarLength, cancellationToken);
                    md5Valid = CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(actualMd5),
                        Convert.FromHexString(expectedMd5));
                    if (!md5Valid)
                        errors.Add("بصمة MD5 الملحقة لا تطابق بايتات TAR الموجودة قبلها.");
                }
            }
            else
            {
                warnings.Add("امتداد TAR عادي: لا توجد بصمة MD5 ملحقة للتحقق منها.");
            }
        }

        var entries = new List<string>();
        var partitions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var tarReadable = false;
        if (errors.Count == 0)
        {
            try
            {
                await using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var bounded = new BoundedReadStream(file, tarLength);
                using var reader = new TarReader(bounded, leaveOpen: true);
                while (reader.GetNextEntry(copyData: false) is { } entry)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entries.Count >= MaximumEntries)
                        throw new InvalidDataException("عدد عناصر TAR يتجاوز الحد 100,000.");
                    ValidateEntryName(entry.Name);
                    if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                        throw new InvalidDataException("حزمة Odin تحتوي رابطًا رمزيًا أو صلبًا غير مسموح: " + entry.Name);

                    entries.Add(entry.Name);
                    if (entry.EntryType is not TarEntryType.Directory &&
                        IsPartitionPayload(entry.Name))
                        partitions.Add(Path.GetFileName(entry.Name));
                }

                if (entries.Count == 0)
                    throw new InvalidDataException("أرشيف TAR لا يحتوي على أي عناصر.");
                tarReadable = true;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            {
                errors.Add("تعذر قراءة بنية TAR بأمان: " + ex.Message);
            }
        }

        if (hasMd5 && !md5Valid && expectedMd5 is null)
            warnings.Add("لم يتم تحليل محتويات TAR بسبب غياب بصمة MD5 الملحقة.");
        if (tarReadable && partitions.Count == 0)
            warnings.Add("لم يتم التعرف على أسماء صور أقسام شائعة؛ قد تكون الحزمة من نوع آخر أو تستخدم أسماء غير مألوفة.");

        return new OdinTarMd5Inspection(
            Path.GetFileName(fullPath),
            info.Length,
            tarReadable,
            hasMd5,
            md5Valid,
            expectedMd5,
            actualMd5,
            entries,
            partitions.ToArray(),
            warnings,
            errors);
    }

    private static async Task<(string Hash, long PayloadLength)?> ReadMd5SuffixAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        var length = (int)Math.Min(MaximumTailBytes, stream.Length);
        var tail = new byte[length];
        stream.Position = stream.Length - length;
        await stream.ReadExactlyAsync(tail, cancellationToken);

        var text = Encoding.ASCII.GetString(tail).TrimEnd('\r', '\n', ' ', '\t', '\0');
        var match = HashSuffix.Match(text);
        if (!match.Success)
            return null;

        var absoluteStart = stream.Length - length + match.Index;
        var payloadLength = absoluteStart;
        while (payloadLength > 0)
        {
            stream.Position = payloadLength - 1;
            var value = stream.ReadByte();
            if (value is not (10 or 13))
                break;
            payloadLength--;
        }

        if (payloadLength <= 0)
            return null;
        return (match.Groups[1].Value.ToLowerInvariant(), payloadLength);
    }

    private static async Task<string> CalculateMd5Async(
        FileStream stream,
        long length,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[128 * 1024];
        long remaining = length;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("انتهى ملف Odin قبل نهاية بيانات TAR.");
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void ValidateEntryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 1024 ||
            name.StartsWith("/", StringComparison.Ordinal) ||
            name.Contains('\\') || name.Contains(':') ||
            name.Any(char.IsControl) ||
            name.Split('/').Any(segment => segment is "." or ".."))
            throw new InvalidDataException("اسم عنصر TAR غير آمن.");
    }

    private static bool IsPartitionPayload(string name)
    {
        var value = Path.GetFileName(name);
        return value.EndsWith(".img", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".img.lz4", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("vbmeta", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("super", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".pit", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BoundedReadStream(Stream inner, long length) : Stream
    {
        private long _position;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var permitted = (int)Math.Min(count, length - _position);
            if (permitted <= 0) return 0;
            var read = inner.Read(buffer, offset, permitted);
            _position += read;
            return read;
        }
        public override int Read(Span<byte> buffer)
        {
            var permitted = (int)Math.Min(buffer.Length, length - _position);
            if (permitted <= 0) return 0;
            var read = inner.Read(buffer[..permitted]);
            _position += read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var permitted = (int)Math.Min(buffer.Length, length - _position);
            if (permitted <= 0) return 0;
            var read = await inner.ReadAsync(buffer[..permitted], cancellationToken);
            _position += read;
            return read;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            // The owning method controls the underlying FileStream lifetime.
            base.Dispose(disposing);
        }
    }
}
