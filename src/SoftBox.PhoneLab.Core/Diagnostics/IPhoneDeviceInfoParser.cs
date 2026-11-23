using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace SoftBox.PhoneLab.Core.Diagnostics;

/// <summary>
/// Parses the XML property-list returned by Apple's Lockdown service through ideviceinfo.
/// It deliberately separates current charge from battery health and never infers part authenticity.
/// </summary>
public static class IPhoneDeviceInfoParser
{
    private const int MaximumPlistCharacters = 1024 * 1024;

    public static IPhoneDeviceInfoSnapshot Parse(string xmlPlist, string deviceIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlPlist);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceIdentifier);
        if (xmlPlist.Length > MaximumPlistCharacters)
            throw new InvalidDataException("Apple device-info plist exceeds the safe size limit.");

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumPlistCharacters,
                MaxCharactersFromEntities = 0
            };
            using var input = new StringReader(xmlPlist);
            using var reader = XmlReader.Create(input, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            var dictionary = document.Root?.Element("dict");
            if (dictionary is null)
                throw new InvalidDataException("Apple device-info response does not contain a plist dictionary.");

            var values = new Dictionary<string, XElement>(StringComparer.Ordinal);
            var elements = dictionary.Elements().ToArray();
            for (var i = 0; i + 1 < elements.Length; i++)
            {
                if (elements[i].Name.LocalName != "key")
                    continue;
                values[elements[i].Value] = elements[i + 1];
                i++;
            }

            string? ReadString(string key)
            {
                if (!values.TryGetValue(key, out var element))
                    return null;
                var value = element.Value.Trim();
                return value.Length is > 0 and <= 512 && !value.Any(char.IsControl) ? value : null;
            }

            long? ReadInteger(string key)
            {
                var value = ReadString(key);
                return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result >= 0
                    ? result
                    : null;
            }

            bool? ReadBoolean(string key)
            {
                if (!values.TryGetValue(key, out var element))
                    return null;
                return element.Name.LocalName switch
                {
                    "true" => true,
                    "false" => false,
                    "string" when bool.TryParse(element.Value, out var parsed) => parsed,
                    _ => null
                };
            }

            var serial = ReadString("SerialNumber");
            var fallbackIdentity = deviceIdentifier.Trim();
            var evidenceIdentity = !string.IsNullOrWhiteSpace(serial) ? serial : fallbackIdentity;
            var charge = ReadInteger("BatteryCurrentCapacity");
            int? chargePercent = charge is >= 0 and <= 100 ? (int)charge.Value : null;

            return new IPhoneDeviceInfoSnapshot(
                DeviceName: ReadString("DeviceName") ?? "iPhone / iPad",
                ProductType: ReadString("ProductType") ?? "غير متاح",
                ProductVersion: ReadString("ProductVersion") ?? "غير متاح",
                BuildVersion: ReadString("BuildVersion") ?? "غير متاح",
                ModelNumber: ReadString("ModelNumber") ?? "غير متاح",
                HardwareModel: ReadString("HardwareModel") ?? "غير متاح",
                MaskedSerialNumber: MaskIdentifier(serial),
                MaskedDeviceIdentifier: MaskIdentifier(fallbackIdentity),
                TotalDiskCapacityBytes: ReadInteger("TotalDiskCapacity"),
                TotalDataCapacityBytes: ReadInteger("TotalDataCapacity"),
                AmountDataAvailableBytes: ReadInteger("AmountDataAvailable"),
                CurrentChargePercent: chargePercent,
                IsCharging: ReadBoolean("BatteryIsCharging"),
                ReadAtUtc: DateTimeOffset.UtcNow,
                EvidenceIdentity: evidenceIdentity);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("Apple device-info plist is malformed.", ex);
        }
    }

    public static string MaskIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "غير متاح";
        var visible = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (visible.Length == 0)
            return "غير متاح";
        var suffix = visible.Length <= 4 ? visible : visible[^4..];
        return "••••" + suffix;
    }
}

public sealed record IPhoneDeviceInfoSnapshot(
    string DeviceName,
    string ProductType,
    string ProductVersion,
    string BuildVersion,
    string ModelNumber,
    string HardwareModel,
    string MaskedSerialNumber,
    string MaskedDeviceIdentifier,
    long? TotalDiskCapacityBytes,
    long? TotalDataCapacityBytes,
    long? AmountDataAvailableBytes,
    int? CurrentChargePercent,
    bool? IsCharging,
    DateTimeOffset ReadAtUtc,
    string EvidenceIdentity)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(ProductType) || ProductType == "غير متاح"
        ? DeviceName
        : $"{DeviceName} ({ProductType})";
}
