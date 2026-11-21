using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record BoardMapCsvImportMetadata(
    string SourceName,
    string SourceUri,
    string SourceDocumentId,
    string SourceLicense,
    string VerifiedBy,
    string HardwareRevision,
    string BoardRevision,
    double BoardWidthMm,
    double BoardHeightMm,
    string AccuracyLevel,
    bool RevisionMatchConfirmed);

/// <summary>
/// Converts a documented PhoneLab CSV export into a normalized, revision-bound component placement map.
/// It does not decode proprietary BRD/BDV/BVR binary formats and never fabricates electrical connectivity.
/// </summary>
public static class BoardMapCsvImporter
{
    private const int MaximumRows = 20_000;
    private const int MaximumColumns = 32;
    private static readonly Regex ReferencePattern = new(
        @"^[A-Za-z][A-Za-z0-9._-]{0,39}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new(
        @"^[a-f0-9]{64}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] RequiredColumns =
    [
        "Reference", "Name", "Xmm", "Ymm", "WidthMm", "HeightMm",
        "Side", "RotationDeg", "Layer", "Footprint", "Value", "Nets"
    ];

    public static async Task<BoardMap> ImportAsync(
        string csvPath,
        DeviceSnapshot device,
        BoardMapCsvImportMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(csvPath))
            throw new ArgumentException("Board map CSV path is required.", nameof(csvPath));
        var info = new FileInfo(csvPath);
        if (!info.Exists)
            throw new FileNotFoundException("Board map CSV does not exist.", csvPath);
        if (info.Length is <= 0 or > 50_000_000)
            throw new InvalidDataException("Board map CSV must be non-empty and no larger than 50 MB.");

        await using var input = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant();
        input.Position = 0;
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var csv = await reader.ReadToEndAsync(cancellationToken);
        if (csv.Length > 50_000_000)
            throw new InvalidDataException("Board map CSV exceeds the 50 MB decoded-text limit.");

        return Parse(csv, digest, device, metadata);
    }

    public static BoardMap Parse(
        string csv,
        string sourceSha256,
        DeviceSnapshot device,
        BoardMapCsvImportMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(csv) || csv.Length > 50_000_000)
            throw new InvalidDataException("Board map CSV content is empty or too large.");
        if (!Sha256Pattern.IsMatch(sourceSha256 ?? ""))
            throw new InvalidDataException("Imported BoardView CSV SHA-256 must be a complete 64-character digest.");

        ValidateMetadata(device, metadata);
        var rows = ParseCsv(csv);
        if (rows.Count < 2 || rows.Count > MaximumRows + 1)
            throw new InvalidDataException($"Board map CSV must contain 1 to {MaximumRows} component rows.");

        var headers = rows[0].Select((value, index) => (Name: value.Trim(), Index: index))
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);
        foreach (var required in RequiredColumns)
            if (!headers.ContainsKey(required))
                throw new InvalidDataException("Required BoardView CSV column is missing: " + required);
        if (headers.Count > MaximumColumns)
            throw new InvalidDataException("BoardView CSV contains more than 32 columns.");

        var parts = new List<BoardPart>(rows.Count - 1);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var netReferences = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var layerSet = new HashSet<(string Name, string Side)>();

        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;
            if (row.Count != rows[0].Count)
                throw new InvalidDataException($"CSV row {rowIndex + 1} has {row.Count} cells; expected {rows[0].Count}.");

            string Get(string key) => row[headers[key]].Trim();
            var reference = Get("Reference");
            if (!ReferencePattern.IsMatch(reference) || !seen.Add(reference))
                throw new InvalidDataException($"CSV row {rowIndex + 1} has an invalid or duplicate reference designator: {reference}");

            var name = Get("Name");
            var side = NormalizeSide(Get("Side"), rowIndex + 1);
            var x = Number(Get("Xmm"), "Xmm", rowIndex + 1);
            var y = Number(Get("Ymm"), "Ymm", rowIndex + 1);
            var width = Number(Get("WidthMm"), "WidthMm", rowIndex + 1);
            var height = Number(Get("HeightMm"), "HeightMm", rowIndex + 1);
            var rotation = Number(Get("RotationDeg"), "RotationDeg", rowIndex + 1);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
                throw new InvalidDataException($"CSV row {rowIndex + 1} has an invalid component name.");
            if (width is <= 0 or > 100 || height is <= 0 or > 100)
                throw new InvalidDataException($"CSV row {rowIndex + 1} has invalid component dimensions.");
            if (x < 0 || y < 0 ||
                x - width / 2 < 0 || y - height / 2 < 0 ||
                x + width / 2 > metadata.BoardWidthMm ||
                y + height / 2 > metadata.BoardHeightMm)
                throw new InvalidDataException($"CSV row {rowIndex + 1} component {reference} lies outside the declared PCB outline.");
            if (rotation is < 0 or >= 360)
                throw new InvalidDataException($"CSV row {rowIndex + 1} rotation must be from 0° to less than 360°.");

            var layer = Get("Layer");
            var footprint = Get("Footprint");
            var value = Get("Value");
            if (layer.Length > 80 || footprint.Length > 160 || value.Length > 120)
                throw new InvalidDataException($"CSV row {rowIndex + 1} has oversized layer, footprint or component value.");
            layerSet.Add((layer, side));

            var nets = Get("Nets").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (nets.Length > 256)
                throw new InvalidDataException($"CSV row {rowIndex + 1} contains more than 256 net names.");
            if (nets.Any(net => net.Length > 80 || net.Any(char.IsControl)))
                throw new InvalidDataException($"CSV row {rowIndex + 1} contains an invalid net name.");
            foreach (var net in nets)
            {
                if (!netReferences.TryGetValue(net, out var refs))
                    netReferences[net] = refs = [];
                refs.Add(reference);
            }

            parts.Add(new BoardPart
            {
                Id = reference,
                Reference = reference,
                Name = name,
                Value = value,
                Footprint = footprint,
                Side = side,
                Layer = layer,
                RotationDegrees = rotation,
                NetNames = nets,
                X = x,
                Y = y,
                Z = side == "Bottom" ? -0.45 : 0.45,
                Width = width,
                Height = height,
                Depth = 0.2,
                BaseColor = side == "Bottom" ? "#F59E0B" : "#3B82F6"
            });
        }

        if (parts.Count is < 1 or > MaximumRows)
            throw new InvalidDataException("The imported BoardView CSV contains no usable components or too many rows.");

        var safeSlug = Regex.Replace(
            $"{device.Manufacturer}-{device.ModelNumber}-{metadata.BoardRevision}",
            @"[^A-Za-z0-9._-]", "-").Trim('-');
        if (safeSlug.Length > 80)
            safeSlug = safeSlug[..80];
        if (safeSlug.Length == 0)
            throw new InvalidDataException("Could not derive a stable BoardMap identifier from the device identity.");

        return new BoardMap
        {
            SchemaVersion = 2,
            Id = "csv-" + safeSlug.ToLowerInvariant(),
            DisplayName = $"{device.Manufacturer} {device.Model} — BoardView {metadata.BoardRevision}",
            Manufacturer = device.Manufacturer,
            ModelPattern = device.ModelNumber,
            ModelNumbers = device.ModelNumber == "غير متاح" ? [] : [device.ModelNumber],
            DeviceCodes = device.DeviceCode == "غير متاح" ? [] : [device.DeviceCode],
            HardwareRevision = metadata.HardwareRevision,
            BoardRevision = metadata.BoardRevision,
            ManualRevisionMatchConfirmed = metadata.RevisionMatchConfirmed,
            CoordinateUnit = "mm",
            CoordinateSystem = "top-left-origin; component X/Y are center coordinates",
            BoardWidthMm = metadata.BoardWidthMm,
            BoardHeightMm = metadata.BoardHeightMm,
            SourceName = metadata.SourceName.Trim(),
            SourceUri = metadata.SourceUri.Trim(),
            SourceDocumentId = metadata.SourceDocumentId.Trim(),
            SourceLicense = metadata.SourceLicense.Trim(),
            SourceSha256 = sourceSha256,
            VerifiedBy = metadata.VerifiedBy.Trim(),
            VerifiedAtUtc = DateTimeOffset.UtcNow,
            AccuracyLevel = metadata.AccuracyLevel,
            IsVerifiedForModel = true,
            AccuracyNotice = "Normalized from a technician-reviewed CSV export. Source authenticity, model/board revision and coordinates remain the technician's responsibility.",
            Layers = layerSet.Select(item => new BoardMapLayer
            {
                Id = Regex.Replace($"{item.Name}-{item.Side}", @"[^A-Za-z0-9._-]", "-"),
                Name = item.Name,
                Side = item.Side,
                VisibleByDefault = true
            }).ToList(),
            Nets = netReferences.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new BoardNet { Name = item.Key, References = item.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() })
                .ToList(),
            Parts = parts
        };
    }

    private static void ValidateMetadata(DeviceSnapshot device, BoardMapCsvImportMetadata metadata)
    {
        static bool Missing(string value) => string.IsNullOrWhiteSpace(value);
        if (device.ModelNumber == "غير متاح" && device.DeviceCode == "غير متاح")
            throw new InvalidDataException("Exact model number or device codename is required before importing a model-specific BoardMap.");
        if (device.Manufacturer == "غير متاح")
            throw new InvalidDataException("Manufacturer identity is unavailable; refusing to bind board coordinates to an unknown manufacturer.");
        if (Missing(metadata.SourceName) || metadata.SourceName.Length > 180 ||
            !Uri.TryCreate(metadata.SourceUri, UriKind.Absolute, out var source) ||
            source.Scheme != Uri.UriSchemeHttps ||
            Missing(metadata.SourceDocumentId) || metadata.SourceDocumentId.Length > 180 ||
            Missing(metadata.SourceLicense) || metadata.SourceLicense.Length > 120 ||
            Missing(metadata.VerifiedBy) || metadata.VerifiedBy.Length > 160 ||
            Missing(metadata.BoardRevision) || metadata.BoardRevision.Length > 100 ||
            metadata.HardwareRevision.Length > 100)
            throw new InvalidDataException("Source name/HTTPS URL/document ID/license, reviewer and exact board revision are required.");
        if (!metadata.RevisionMatchConfirmed)
            throw new InvalidDataException("The technician must confirm that the physical board revision matches the imported source.");
        if (metadata.AccuracyLevel is not ("boardview" or "manufacturer" or "pcb"))
            throw new InvalidDataException("Accuracy level must be boardview, manufacturer or pcb.");
        if (!double.IsFinite(metadata.BoardWidthMm) || !double.IsFinite(metadata.BoardHeightMm) ||
            metadata.BoardWidthMm is <= 0 or > 500 || metadata.BoardHeightMm is <= 0 or > 500)
            throw new InvalidDataException("Board outline width and height must be finite millimeter values between 0 and 500.");
    }

    private static double Number(string value, string column, int row)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) &&
           double.IsFinite(result)
            ? result
            : throw new InvalidDataException($"CSV row {row} has an invalid number in {column}.");

    private static string NormalizeSide(string value, int row)
        => value.Trim().ToLowerInvariant() switch
        {
            "top" or "front" or "f" => "Top",
            "bottom" or "back" or "b" => "Bottom",
            _ => throw new InvalidDataException($"CSV row {row} Side must be Top/Front/F or Bottom/Back/B.")
        };

    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var closedQuote = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < csv.Length && csv[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                        closedQuote = true;
                    }
                }
                else
                {
                    field.Append(character);
                }
                continue;
            }

            if (closedQuote && character is not (',' or '\r' or '\n' or ' ' or '\t'))
                throw new InvalidDataException("CSV has characters after a closing quoted field.");
            if (character == '"' && field.Length == 0 && !closedQuote)
            {
                inQuotes = true;
                continue;
            }
            if (character == '"')
                throw new InvalidDataException("CSV has an unexpected quote inside an unquoted field.");
            if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                closedQuote = false;
                if (row.Count > MaximumColumns)
                    throw new InvalidDataException("CSV row contains more than 32 columns.");
                continue;
            }
            if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                    index++;
                row.Add(field.ToString());
                field.Clear();
                closedQuote = false;
                if (row.Count > 1 || row[0].Length > 0)
                    rows.Add(row);
                row = [];
                if (rows.Count > MaximumRows + 1)
                    throw new InvalidDataException($"CSV exceeds {MaximumRows} component rows.");
                continue;
            }
            field.Append(character);
        }

        if (inQuotes)
            throw new InvalidDataException("CSV contains an unterminated quoted field.");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Length > 0)
                rows.Add(row);
        }
        if (rows.Count > 0)
        {
            var headerCount = rows[0].Count;
            if (headerCount is < 1 or > MaximumColumns)
                throw new InvalidDataException("CSV header contains an unsupported number of columns.");
            if (rows[0].Distinct(StringComparer.OrdinalIgnoreCase).Count() != headerCount)
                throw new InvalidDataException("CSV header has duplicate column names.");
        }
        return rows;
    }
}
