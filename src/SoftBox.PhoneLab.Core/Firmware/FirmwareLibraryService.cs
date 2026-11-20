using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Firmware;

public sealed record FirmwareLibraryItem(
    Guid Id,
    string Manufacturer,
    string Model,
    string Region,
    string Version,
    string FileName,
    string RelativePath,
    long SizeBytes,
    string Sha256,
    DateTimeOffset ImportedAtUtc,
    string SourceDescription,
    string Notes);

public sealed record FirmwareIntegrityResult(
    Guid ItemId,
    string FileName,
    bool FileExists,
    bool SizeMatches,
    bool Sha256Matches,
    long? ActualSizeBytes,
    string? ActualSha256,
    string Message);

public sealed class FirmwareLibraryService
{
    private const int SchemaVersion = 1;
    private const int MaximumCatalogItems = 50_000;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".img", ".bin", ".tar", ".md5", ".ofp", ".ozip", ".payload"
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _root;
    private readonly string _indexPath;
    private List<FirmwareLibraryItem>? _items;

    public string RootDirectory => _root;

    public FirmwareLibraryService(string? rootDirectory = null)
    {
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "FirmwareLibrary"));
        _indexPath = Path.Combine(_root, "library.json");
        Directory.CreateDirectory(_root);
    }

    public async Task<IReadOnlyList<FirmwareLibraryItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            return _items!.OrderByDescending(item => item.ImportedAtUtc).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<FirmwareLibraryItem> ImportAsync(
        string sourceFilePath,
        string manufacturer,
        string model,
        string region,
        string version,
        string sourceDescription,
        string notes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            throw new FileNotFoundException("ملف Firmware المحدد غير موجود.", sourceFilePath);
        var source = Path.GetFullPath(sourceFilePath);
        var extension = Path.GetExtension(source);
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidDataException("امتداد الملف غير مدعوم في مكتبة Firmware المحلية.");

        manufacturer = NormalizeField(manufacturer, "الشركة", 80);
        model = NormalizeField(model, "الموديل", 120);
        region = NormalizeOptionalField(region, 40);
        version = NormalizeOptionalField(version, 80);
        sourceDescription = NormalizeOptionalField(sourceDescription, 200);
        notes = NormalizeOptionalField(notes, 1000);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            if (_items!.Count >= MaximumCatalogItems)
                throw new InvalidOperationException("وصلت مكتبة Firmware إلى حد العناصر المسموح.");

            var id = Guid.NewGuid();
            var relativeDirectory = Path.Combine(
                SafeSegment(manufacturer),
                SafeSegment(model),
                SafeSegment(string.IsNullOrWhiteSpace(region) ? "Unspecified" : region));
            var relativePath = Path.Combine(relativeDirectory, id.ToString("N") + extension.ToLowerInvariant());
            var destination = Path.GetFullPath(Path.Combine(_root, relativePath));
            EnsureInsideRoot(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            var temporary = destination + ".tmp";
            long size = 0;
            string hash;
            try
            {
                using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                    128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[128 * 1024];
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                        if (read == 0)
                            break;
                        size = checked(size + read);
                        incremental.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                    await output.FlushAsync(cancellationToken);
                }

                hash = Convert.ToHexString(incremental.GetHashAndReset()).ToLowerInvariant();
                File.Move(temporary, destination, overwrite: false);
            }
            catch
            {
                try { File.Delete(temporary); } catch { }
                throw;
            }

            var item = new FirmwareLibraryItem(
                id,
                manufacturer,
                model,
                region,
                version,
                Path.GetFileName(source),
                relativePath,
                size,
                hash,
                DateTimeOffset.UtcNow,
                sourceDescription,
                notes);

            _items.Add(item);
            try
            {
                await SaveLockedAsync(cancellationToken);
            }
            catch
            {
                _items.Remove(item);
                try { File.Delete(destination); } catch { }
                throw;
            }
            return item;
        }
        finally { _gate.Release(); }
    }

    public async Task<FirmwareIntegrityResult> VerifyAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        FirmwareLibraryItem item;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            item = _items!.FirstOrDefault(candidate => candidate.Id == itemId)
                ?? throw new KeyNotFoundException("ملف Firmware غير موجود في الفهرس.");
        }
        finally { _gate.Release(); }

        var path = ResolveItemPath(item);
        if (!File.Exists(path))
            return new FirmwareIntegrityResult(item.Id, item.FileName, false, false, false, null, null,
                "الملف غير موجود في مسار المكتبة المتوقع.");

        var info = new FileInfo(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        var sizeMatches = info.Length == item.SizeBytes;
        var hashMatches = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actualHash), Convert.FromHexString(item.Sha256));

        return new FirmwareIntegrityResult(
            item.Id,
            item.FileName,
            true,
            sizeMatches,
            hashMatches,
            info.Length,
            actualHash,
            sizeMatches && hashMatches
                ? "تطابقت البصمة والحجم مع سجل الاستيراد المحلي. هذا لا يثبت المصدر الأصلي أو توافق الجهاز."
                : "اختلف الحجم أو SHA-256 عن سجل الاستيراد؛ لا تستخدم الملف قبل معرفة سبب الاختلاف.");
    }

    public string GetAbsolutePath(FirmwareLibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ResolveItemPath(item);
    }

    private async Task EnsureLoadedLockedAsync(CancellationToken cancellationToken)
    {
        if (_items is not null)
            return;
        if (!File.Exists(_indexPath))
        {
            _items = [];
            return;
        }

        var info = new FileInfo(_indexPath);
        if (info.Length > 64 * 1024 * 1024)
            throw new InvalidDataException("فهرس مكتبة Firmware أكبر من الحد الآمن.");

        await using var stream = new FileStream(_indexPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<FirmwareLibraryDocument>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("فهرس مكتبة Firmware غير صالح.");

        if (document.SchemaVersion != SchemaVersion || document.Items is null ||
            document.Items.Count > MaximumCatalogItems)
            throw new InvalidDataException("مخطط أو حجم مكتبة Firmware غير مدعوم.");

        foreach (var item in document.Items)
            _ = ResolveItemPath(item);
        _items = document.Items.ToList();
    }

    private async Task SaveLockedAsync(CancellationToken cancellationToken)
    {
        var document = new FirmwareLibraryDocument(SchemaVersion, DateTimeOffset.UtcNow, _items!);
        var temporary = _indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                32 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, _indexPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }

    private string ResolveItemPath(FirmwareLibraryItem item)
    {
        if (item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.RelativePath) ||
            Path.IsPathRooted(item.RelativePath) ||
            item.RelativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
                .Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("مسار ملف في فهرس Firmware غير آمن.");

        var resolved = Path.GetFullPath(Path.Combine(_root, item.RelativePath));
        EnsureInsideRoot(resolved);
        return resolved;
    }

    private void EnsureInsideRoot(string path)
    {
        var rootPrefix = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("مسار ملف Firmware يخرج عن مجلد المكتبة.");
    }

    private static string NormalizeField(string? value, string field, int maximum)
    {
        var normalized = NormalizeOptionalField(value, maximum);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException(field + " مطلوب.");
        return normalized;
    }

    private static string NormalizeOptionalField(string? value, int maximum)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length > maximum || normalized.Any(char.IsControl))
            throw new ArgumentException("إحدى بيانات Firmware طويلة جدًا أو تحتوي على محارف تحكم.");
        return normalized;
    }

    private static string SafeSegment(string value)
    {
        var segment = Regex.Replace(value.Trim(), @"[^\p{L}\p{N}._-]+", "_").Trim('.', '_');
        if (string.IsNullOrWhiteSpace(segment))
            segment = "Unspecified";
        if (segment.Length > 80)
            segment = segment[..80];
        if (segment is "CON" or "PRN" or "AUX" or "NUL" ||
            Regex.IsMatch(segment, @"^(COM|LPT)[1-9]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            segment = "_" + segment;
        return segment;
    }

    private sealed record FirmwareLibraryDocument(
        int SchemaVersion,
        DateTimeOffset UpdatedAtUtc,
        IReadOnlyList<FirmwareLibraryItem> Items);
}
