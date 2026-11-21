using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed class BoardMapCatalog
{
    private readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] ExactAccuracyLevels = ["pcb", "boardview", "manufacturer"];

    public async Task<BoardMap> LoadBestMapAsync(DeviceSnapshot device, DeviceProfile? profile)
    {
        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "boardmaps"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoftBox", "PhoneLab", "boardmaps")
        };

        var matchingMaps = new List<BoardMap>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            var files = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                .Take(5_001).ToArray();
            if (files.Length > 5_000)
                throw new InvalidDataException("BoardMap folders contain more than 5,000 files; refusing an unbounded scan.");

            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length is <= 0 or > 50_000_000)
                        continue;
                    var map = JsonSerializer.Deserialize<BoardMap>(await File.ReadAllTextAsync(file), _options);
                    if (map is not null && IsVerifiedForDevice(map, device))
                        matchingMaps.Add(map);
                }
                catch (JsonException) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        var uniqueMaps = matchingMaps
            .GroupBy(map => map.SchemaVersion >= 2
                ? map.Id + "|" + map.SourceSha256
                : map.Id + "|legacy",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (uniqueMaps.Length == 1)
            return uniqueMaps[0];

        var mapNotice = uniqueMaps.Length > 1
            ? $"توجد {uniqueMaps.Length} خرائط PCB مختلفة ومطابقة للموديل؛ لم يتم اختيار واحدة عشوائيًا."
            : $"لا توجد خريطة PCB موثقة للموديل {device.ModelNumber}/{device.DeviceCode}. المعروض مخطط عام للتوضيح فقط.";

        var genericPath = Path.Combine(AppContext.BaseDirectory, "Data", "boardmaps", "generic-android.json");
        if (File.Exists(genericPath))
        {
            try
            {
                var genericInfo = new FileInfo(genericPath);
                if (genericInfo.Length is > 0 and <= 50_000_000)
                {
                    var generic = JsonSerializer.Deserialize<BoardMap>(await File.ReadAllTextAsync(genericPath), _options);
                    if (generic is not null)
                        return new BoardMap
                        {
                            Id = generic.Id,
                            DisplayName = generic.DisplayName,
                            Manufacturer = generic.Manufacturer,
                            ModelPattern = generic.ModelPattern,
                            IsVerifiedForModel = false,
                            AccuracyLevel = "generic",
                            AccuracyNotice = mapNotice,
                            Parts = generic.Parts
                        };
                }
            }
            catch (JsonException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new BoardMap { AccuracyNotice = "لا توجد خريطة داخلية موثقة لهذا الجهاز. لم يتم اختراع مخطط بديل." };
    }

    public async Task<string> ImportVerifiedMapAsync(string sourcePath, DeviceSnapshot device)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new FileNotFoundException("Board map file not found.", sourcePath);
        if (info.Length is <= 0 or > 50_000_000)
            throw new InvalidDataException("Board map file is empty or exceeds the 50 MB import limit.");

        var json = await File.ReadAllTextAsync(sourcePath);
        var map = JsonSerializer.Deserialize<BoardMap>(json, _options)
            ?? throw new InvalidDataException("Invalid board map JSON.");
        if (!IsVerifiedForDevice(map, device))
            throw new InvalidDataException(
                "الخريطة لا تطابق رقم الموديل/كود الجهاز، أو بيانات المصدر/المراجعة ناقصة، أو إحداثيات القطع غير صالحة. لم يتم استيرادها.");

        return await SaveImportedMapAsync(map);
    }

    public async Task<string> ImportCsvMapAsync(
        string csvPath,
        DeviceSnapshot device,
        BoardMapCsvImportMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var map = await BoardMapCsvImporter.ImportAsync(csvPath, device, metadata, cancellationToken);
        if (!IsVerifiedForDevice(map, device))
            throw new InvalidDataException("CSV-derived BoardMap did not pass exact model/revision validation.");
        return await SaveImportedMapAsync(map, cancellationToken);
    }

    private async Task<string> SaveImportedMapAsync(BoardMap map, CancellationToken cancellationToken = default)
    {
        var safeId = Regex.Replace(map.Id.Trim(), @"[^A-Za-z0-9._-]", "_");
        if (safeId.Length is 0 or > 100)
            throw new InvalidDataException("Board map ID is not valid.");
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "boardmaps");
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, $"{safeId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
        var temporary = destination + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(map, _options), cancellationToken);
            File.Move(temporary, destination, overwrite: false);
            return destination;
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }

    public static bool IsVerifiedForDevice(BoardMap map, DeviceSnapshot device)
    {
        if (map is null || device is null || map.SchemaVersion is not (1 or 2) ||
            map.ModelNumbers is null || map.DeviceCodes is null || map.Parts is null ||
            string.IsNullOrWhiteSpace(map.Id) ||
            !map.IsVerifiedForModel ||
            !ExactAccuracyLevels.Contains(map.AccuracyLevel, StringComparer.OrdinalIgnoreCase))
            return false;

        var sourceValid = Uri.TryCreate(map.SourceUri, UriKind.Absolute, out var source) &&
                          source.Scheme == Uri.UriSchemeHttps &&
                          !string.IsNullOrWhiteSpace(map.SourceName) &&
                          !string.IsNullOrWhiteSpace(map.VerifiedBy) &&
                          map.VerifiedAtUtc.HasValue &&
                          (map.SchemaVersion < 2 ||
                           (!string.IsNullOrWhiteSpace(map.SourceDocumentId) &&
                            !string.IsNullOrWhiteSpace(map.SourceLicense) &&
                            map.Layers is not null && map.Nets is not null &&
                            Regex.IsMatch(map.SourceSha256 ?? "", @"^[a-f0-9]{64}$", RegexOptions.IgnoreCase) &&
                            map.CoordinateUnit == "mm" &&
                            map.ManualRevisionMatchConfirmed &&
                            double.IsFinite(map.BoardWidthMm) && map.BoardWidthMm is > 0 and <= 500 &&
                            double.IsFinite(map.BoardHeightMm) && map.BoardHeightMm is > 0 and <= 500));
        if (!sourceValid) return false;

        var exactModel = device.ModelNumber != "غير متاح" &&
                         map.ModelNumbers.Contains(device.ModelNumber, StringComparer.OrdinalIgnoreCase);
        var exactCode = device.DeviceCode != "غير متاح" &&
                        map.DeviceCodes.Contains(device.DeviceCode, StringComparer.OrdinalIgnoreCase);
        if (!exactModel && !exactCode) return false;

        if (!string.IsNullOrWhiteSpace(map.Manufacturer) &&
            device.Manufacturer != "غير متاح" &&
            !map.Manufacturer.Equals(device.Manufacturer, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(map.HardwareRevision) &&
            device.HardwareRevision != "غير متاح" &&
            !map.HardwareRevision.Equals(device.HardwareRevision, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(map.BoardRevision) &&
            device.BoardRevision != "غير متاح" &&
            !map.BoardRevision.Equals(device.BoardRevision, StringComparison.OrdinalIgnoreCase))
            return false;
        if (map.SchemaVersion >= 2 && !map.ManualRevisionMatchConfirmed)
            return false;

        if (map.Parts.Count is 0 or > 20_000) return false;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in map.Parts)
        {
            if (part is null) return false;
        }
        foreach (var part in map.Parts)
        {
            if (string.IsNullOrWhiteSpace(part.Id) || !ids.Add(part.Id) ||
                !double.IsFinite(part.RotationDegrees) || part.RotationDegrees is < 0 or >= 360 ||
                (map.SchemaVersion >= 2 && (string.IsNullOrWhiteSpace(part.Reference) ||
                    part.Side is not ("Top" or "Bottom") || part.X - part.Width / 2 < 0 ||
                    part.Y - part.Height / 2 < 0 ||
                    part.X + part.Width / 2 > map.BoardWidthMm ||
                    part.Y + part.Height / 2 > map.BoardHeightMm ||
                    part.NetNames is null || part.NetNames.Length > 256)) ||
                !double.IsFinite(part.X) || !double.IsFinite(part.Y) || !double.IsFinite(part.Z) ||
                !double.IsFinite(part.Width) || !double.IsFinite(part.Height) || !double.IsFinite(part.Depth) ||
                part.Width <= 0 || part.Height <= 0 || part.Depth <= 0 ||
                Math.Abs(part.X) > 100_000 || Math.Abs(part.Y) > 100_000 || Math.Abs(part.Z) > 100_000 ||
                part.Width > 100_000 || part.Height > 100_000 || part.Depth > 100_000)
                return false;
        }
        return true;
    }
}
