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

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var map = JsonSerializer.Deserialize<BoardMap>(await File.ReadAllTextAsync(file), _options);
                    if (map is not null && IsVerifiedForDevice(map, device))
                        return map;
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
        }

        var genericPath = Path.Combine(AppContext.BaseDirectory, "Data", "boardmaps", "generic-android.json");
        if (File.Exists(genericPath))
        {
            try
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
                        AccuracyNotice = $"لا توجد خريطة PCB موثقة للموديل {device.ModelNumber}/{device.DeviceCode}. المعروض مخطط عام للتوضيح فقط.",
                        Parts = generic.Parts
                    };
            }
            catch (JsonException) { }
            catch (IOException) { }
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

        var safeId = Regex.Replace(map.Id.Trim(), @"[^A-Za-z0-9._-]", "_");
        if (safeId.Length is 0 or > 80)
            throw new InvalidDataException("Board map ID is not valid.");

        var destinationRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "boardmaps");
        Directory.CreateDirectory(destinationRoot);
        var destination = Path.Combine(destinationRoot, $"{safeId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
        await File.WriteAllTextAsync(destination, json);
        return destination;
    }

    public static bool IsVerifiedForDevice(BoardMap map, DeviceSnapshot device)
    {
        if (!map.IsVerifiedForModel || !ExactAccuracyLevels.Contains(map.AccuracyLevel, StringComparer.OrdinalIgnoreCase))
            return false;

        var sourceValid = Uri.TryCreate(map.SourceUri, UriKind.Absolute, out var source) &&
                          source.Scheme == Uri.UriSchemeHttps &&
                          !string.IsNullOrWhiteSpace(map.SourceName) &&
                          !string.IsNullOrWhiteSpace(map.VerifiedBy) &&
                          map.VerifiedAtUtc.HasValue;
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
            !map.HardwareRevision.Equals(device.HardwareRevision, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(map.BoardRevision) &&
            !map.BoardRevision.Equals(device.BoardRevision, StringComparison.OrdinalIgnoreCase))
            return false;

        if (map.Parts.Count is 0 or > 20_000) return false;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in map.Parts)
        {
            if (string.IsNullOrWhiteSpace(part.Id) || !ids.Add(part.Id) ||
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
