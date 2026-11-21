using System.Collections.Generic;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Security;

public enum AndroidNetworkRegistrationState
{
    Unknown,
    InService,
    OutOfService,
    EmergencyOnly,
    PowerOff
}

/// <summary>
/// Selected public radio-registration observations from dumpsys telephony.registry.
/// No subscriber identifiers, phone numbers, IMEI/IMSI/ICCID or unlock verdicts are retained.
/// </summary>
public sealed record AndroidTelephonyDiagnosticsReport(
    bool OutputReadable,
    AndroidNetworkRegistrationState VoiceRegistration,
    AndroidNetworkRegistrationState DataRegistration,
    string VoiceRadioTechnology,
    string DataRadioTechnology,
    bool? IsRoaming,
    bool? IsEmergencyOnly,
    bool? IsSearching,
    string OperatorName);

/// <summary>
/// Parses a deliberately narrow allow-list of Android telephony registry fields.
/// Unknown, conflicting and missing values remain Unknown/null.
/// </summary>
public static class AndroidTelephonyDiagnosticsParser
{
    private static readonly Regex FieldPattern(string name) => new(
        @"\b" + Regex.Escape(name) + @"\s*=\s*([^,;\r\n}]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<int, string> RadioTechnologies =
        new Dictionary<int, string>
        {
            [0] = "Unknown",
            [1] = "GPRS",
            [2] = "EDGE",
            [3] = "UMTS",
            [4] = "IS95A",
            [5] = "IS95B",
            [6] = "1xRTT",
            [7] = "EVDO_0",
            [8] = "EVDO_A",
            [9] = "HSDPA",
            [10] = "HSUPA",
            [11] = "HSPA",
            [12] = "EVDO_B",
            [13] = "EHRPD",
            [14] = "LTE",
            [15] = "HSPAP",
            [16] = "GSM",
            [17] = "TD_SCDMA",
            [18] = "IWLAN",
            [19] = "LTE_CA",
            [20] = "NR"
        };

    public static AndroidTelephonyDiagnosticsReport Parse(string? output)
    {
        var text = output ?? string.Empty;
        var readable = !string.IsNullOrWhiteSpace(text) &&
            !text.Contains("Permission Denial", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("Can't find service", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("not found", StringComparison.OrdinalIgnoreCase);

        return new AndroidTelephonyDiagnosticsReport(
            readable,
            ParseRegistration(text, "mVoiceRegState"),
            ParseRegistration(text, "mDataRegState"),
            ParseRadioTechnology(text, "mRilVoiceRadioTechnology"),
            ParseRadioTechnology(text, "mRilDataRadioTechnology"),
            ParseBoolean(text, "mIsRoaming"),
            ParseBoolean(text, "mIsEmergencyOnly"),
            ParseBoolean(text, "mIsSearching"),
            ParseOperatorName(text));
    }

    private static AndroidNetworkRegistrationState ParseRegistration(string text, string field)
    {
        var values = FieldPattern(field).Matches(text)
            .Select(match => NormalizeRegistration(match.Groups[1].Value))
            .Distinct()
            .ToArray();

        return values.Length == 1 ? values[0] : AndroidNetworkRegistrationState.Unknown;
    }

    private static AndroidNetworkRegistrationState NormalizeRegistration(string raw)
    {
        var normalized = raw.Trim();
        var parenthesis = normalized.IndexOf('(');
        if (parenthesis >= 0)
        {
            var end = normalized.IndexOf(')', parenthesis + 1);
            if (end > parenthesis)
                normalized = normalized[(parenthesis + 1)..end];
        }
        else if (int.TryParse(normalized, out var numeric))
        {
            return numeric switch
            {
                0 => AndroidNetworkRegistrationState.InService,
                1 => AndroidNetworkRegistrationState.OutOfService,
                2 => AndroidNetworkRegistrationState.EmergencyOnly,
                3 => AndroidNetworkRegistrationState.PowerOff,
                _ => AndroidNetworkRegistrationState.Unknown
            };
        }

        return normalized.ToUpperInvariant() switch
        {
            "IN_SERVICE" or "IN SERVICE" => AndroidNetworkRegistrationState.InService,
            "OUT_OF_SERVICE" or "OUT OF SERVICE" => AndroidNetworkRegistrationState.OutOfService,
            "EMERGENCY_ONLY" or "EMERGENCY ONLY" => AndroidNetworkRegistrationState.EmergencyOnly,
            "POWER_OFF" or "POWER OFF" => AndroidNetworkRegistrationState.PowerOff,
            _ => AndroidNetworkRegistrationState.Unknown
        };
    }

    private static string ParseRadioTechnology(string text, string field)
    {
        var values = FieldPattern(field).Matches(text)
            .Select(match => NormalizeRadioTechnology(match.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 1 ? values[0] : "Unknown";
    }

    private static string NormalizeRadioTechnology(string raw)
    {
        var normalized = raw.Trim();
        var parenthesis = normalized.IndexOf('(');
        if (parenthesis >= 0)
        {
            var end = normalized.IndexOf(')', parenthesis + 1);
            if (end > parenthesis)
                return SanitizeLabel(normalized[(parenthesis + 1)..end]);
        }

        return int.TryParse(normalized, out var numeric) &&
               RadioTechnologies.TryGetValue(numeric, out var technology)
            ? technology
            : "Unknown";
    }

    private static bool? ParseBoolean(string text, string field)
    {
        var values = FieldPattern(field).Matches(text)
            .Select(match => match.Groups[1].Value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return values.Length != 1
            ? null
            : values[0] switch
            {
                "true" => true,
                "false" => false,
                _ => null
            };
    }

    private static string ParseOperatorName(string text)
    {
        var values = FieldPattern("mOperatorAlphaLong").Matches(text)
            .Select(match => SanitizeLabel(match.Groups[1].Value.Trim().Trim('"')))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 1 ? values[0] : string.Empty;
    }

    private static string SanitizeLabel(string value)
    {
        var cleaned = new string(value
            .Where(character => !char.IsControl(character))
            .Take(80)
            .ToArray());
        return cleaned.Trim();
    }
}
