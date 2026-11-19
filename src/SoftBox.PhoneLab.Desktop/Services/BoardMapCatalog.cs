using System.IO;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed class BoardMapCatalog
{
    private readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };

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
                    if (map is not { IsVerifiedForModel: true } || map.AccuracyLevel is not ("pcb" or "boardview" or "manufacturer"))
                        continue;
                    var exactModel = map.ModelNumbers.Contains(device.ModelNumber, StringComparer.OrdinalIgnoreCase);
                    var exactCode = map.DeviceCodes.Contains(device.DeviceCode, StringComparer.OrdinalIgnoreCase);
                    var exactProfile = profile is not null && !string.IsNullOrWhiteSpace(profile.BoardMapId) &&
                                       map.Id.Equals(profile.BoardMapId, StringComparison.OrdinalIgnoreCase);
                    var provenanceOk = Uri.TryCreate(map.SourceUri, UriKind.Absolute, out var source) &&
                                       source.Scheme == Uri.UriSchemeHttps &&
                                       !string.IsNullOrWhiteSpace(map.SourceName) &&
                                       !string.IsNullOrWhiteSpace(map.VerifiedBy) &&
                                       map.VerifiedAtUtc.HasValue;
                    if ((exactModel || exactCode || exactProfile) && provenanceOk)
                        return map;
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
        }

        var genericPath = Path.Combine(AppContext.BaseDirectory, "Data", "boardmaps", "generic-android.json");
        if (File.Exists(genericPath))
        {
            var generic = JsonSerializer.Deserialize<BoardMap>(await File.ReadAllTextAsync(genericPath), _options);
            if (generic is not null)
                return new BoardMap
                {
                    Id = generic.Id, DisplayName = generic.DisplayName, Manufacturer = generic.Manufacturer,
                    ModelPattern = generic.ModelPattern, IsVerifiedForModel = false, AccuracyLevel = "generic",
                    AccuracyNotice = $"لا توجد خريطة PCB موثقة للموديل {device.ModelNumber}/{device.DeviceCode}. المعروض مخطط عام للتوضيح فقط.",
                    Parts = generic.Parts
                };
        }
        return new BoardMap { AccuracyNotice = "لا توجد خريطة داخلية موثقة لهذا الجهاز. لم يتم اختراع مخطط بديل." };
    }
}
