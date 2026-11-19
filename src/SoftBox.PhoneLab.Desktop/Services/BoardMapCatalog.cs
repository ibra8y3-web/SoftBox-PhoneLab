using System.IO;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed class BoardMapCatalog
{
    private readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };

    public async Task<BoardMap> LoadBestMapAsync(string manufacturer, string model)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Data", "boardmaps");
        var files = Directory.Exists(root) ? Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
        foreach (var file in files)
        {
            try
            {
                var map = JsonSerializer.Deserialize<BoardMap>(await File.ReadAllTextAsync(file), _options);
                if (map is { IsVerifiedForModel: true } &&
                    map.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(map.ModelPattern) &&
                    model.Contains(map.ModelPattern, StringComparison.OrdinalIgnoreCase))
                    return map;
            }
            catch (JsonException) { }
        }

        var genericPath = Path.Combine(root, "generic-android.json");
        if (File.Exists(genericPath))
        {
            var generic = JsonSerializer.Deserialize<BoardMap>(await File.ReadAllTextAsync(genericPath), _options);
            if (generic is not null) return generic;
        }
        return new BoardMap { AccuracyNotice = "No board-map data available. The diagram is not model-accurate." };
    }
}
