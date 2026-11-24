using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace SoftBox.PhoneLab.Core.Diagnostics;

/// <summary>
/// Parses only the GasGauge section returned by the read-only
/// "idevicediagnostics diagnostics GasGauge" command.
/// Unknown properties and unrelated diagnostic sections are ignored.
/// </summary>
public static class IPhoneBatteryDiagnosticsParser
{
    private const int MaximumPlistCharacters = 256 * 1024;

    public static IPhoneBatteryDiagnosticsSnapshot Parse(string xmlPlist, string iOSVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlPlist);
        if (xmlPlist.Length > MaximumPlistCharacters)
            throw new InvalidDataException("Apple GasGauge plist exceeds the safe size limit.");

        try
        {
            var settings = new XmlReaderSettings
            {
                // Apple's plist writer includes a standard external DOCTYPE. Ignore it;
                // XmlResolver=null guarantees that no external DTD/entity is fetched.
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumPlistCharacters,
                MaxCharactersFromEntities = 0
            };
            using var input = new StringReader(xmlPlist);
            using var reader = XmlReader.Create(input, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            var root = document.Root?.Element("dict")
                ?? throw new InvalidDataException("GasGauge response does not contain a plist dictionary.");

            var rootValues = ReadDictionary(root);
            if (!rootValues.TryGetValue("GasGauge", out var gasGaugeElement) ||
                gasGaugeElement.Name.LocalName != "dict")
                throw new InvalidDataException("Apple diagnostics response does not contain a GasGauge dictionary.");

            var values = ReadDictionary(gasGaugeElement);
            long? Integer(string key)
            {
                if (!values.TryGetValue(key, out var element) || element.Name.LocalName != "integer")
                    return null;
                return long.TryParse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result >= 0
                    ? result
                    : null;
            }

            string? TextValue(string key)
            {
                if (!values.TryGetValue(key, out var element) || element.Name.LocalName != "string")
                    return null;
                var value = element.Value.Trim();
                return value.Length is > 0 and <= 128 && !value.Any(char.IsControl) ? value : null;
            }

            var cycleCount = Integer("CycleCount");
            var designCapacity = Integer("DesignCapacity");
            var fullChargeCapacity = Integer("FullChargeCapacity");
            var status = TextValue("Status") ?? "غير متاح";
            var estimate = EstimateMaximumCapacityPercent(fullChargeCapacity, designCapacity, status, iOSVersion, out var basis);

            return new IPhoneBatteryDiagnosticsSnapshot(
                CycleCount: cycleCount is <= 100000 ? cycleCount : null,
                DesignCapacity: designCapacity is <= 100000 ? designCapacity : null,
                FullChargeCapacity: fullChargeCapacity is <= 100000 ? fullChargeCapacity : null,
                Status: status,
                EstimatedMaximumCapacityPercent: estimate,
                EstimateBasis: basis,
                ReadAtUtc: DateTimeOffset.UtcNow);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("Apple GasGauge plist is malformed.", ex);
        }
    }

    private static Dictionary<string, XElement> ReadDictionary(XElement dictionary)
    {
        var values = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var elements = dictionary.Elements().ToArray();
        for (var i = 0; i + 1 < elements.Length; i++)
        {
            if (elements[i].Name.LocalName != "key")
                continue;
            values[elements[i].Value] = elements[i + 1];
            i++;
        }
        return values;
    }

    private static int? EstimateMaximumCapacityPercent(
        long? fullCharge,
        long? design,
        string status,
        string iOSVersion,
        out string basis)
    {
        basis = "Unavailable";
        if (!status.Equals("Success", StringComparison.OrdinalIgnoreCase) || fullCharge is null)
            return null;

        // Current libimobiledevice consumers report the GasGauge FullChargeCapacity
        // on iOS 17+ on a percentage scale (0..100); older releases normally report
        // capacity units and can be compared with DesignCapacity. This remains an
        // estimate and is never silently substituted for the value shown in Settings.
        var major = ParseMajorVersion(iOSVersion);
        double estimate;
        if (major >= 17 && fullCharge.Value <= 100)
        {
            estimate = fullCharge.Value;
            basis = "iOS17PlusReportedPercent";
        }
        else if (design is > 0)
        {
            estimate = fullCharge.Value * 100d / design.Value;
            basis = "FullChargeCapacityToDesignCapacityRatio";
        }
        else
        {
            return null;
        }

        if (!double.IsFinite(estimate) || estimate is < 1 or > 120)
        {
            basis = "UnavailableInconsistentValues";
            return null;
        }

        return (int)Math.Round(estimate, MidpointRounding.AwayFromZero);
    }

    private static int ParseMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return 0;
        var token = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? major : 0;
    }
}

public sealed record IPhoneBatteryDiagnosticsSnapshot(
    long? CycleCount,
    long? DesignCapacity,
    long? FullChargeCapacity,
    string Status,
    int? EstimatedMaximumCapacityPercent,
    string EstimateBasis,
    DateTimeOffset ReadAtUtc)
{
    public bool IsSuccessful => Status.Equals("Success", StringComparison.OrdinalIgnoreCase);
}
