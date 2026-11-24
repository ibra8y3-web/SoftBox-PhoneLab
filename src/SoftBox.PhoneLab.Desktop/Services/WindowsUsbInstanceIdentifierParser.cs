namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Extracts a possible USB device-instance suffix from a Windows PnP instance path.
/// The suffix may be a device serial, a port-generated instance value or another OEM token;
/// it is intentionally named a PnP suffix and is never promoted to a confirmed handset serial.
/// </summary>
public static class WindowsUsbInstanceIdentifierParser
{
    public static bool TryExtractInstanceSuffix(string? instanceId, out string suffix)
    {
        suffix = string.Empty;
        if (string.IsNullOrWhiteSpace(instanceId) ||
            instanceId.Length > 512 ||
            !instanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
            return false;

        var separator = instanceId.LastIndexOf('\\');
        if (separator < 0 || separator == instanceId.Length - 1)
            return false;

        var candidate = instanceId[(separator + 1)..].Trim();
        if (candidate.Length is < 4 or > 128 ||
            !candidate.Any(char.IsAsciiLetterOrDigit) ||
            candidate.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.'))
            return false;

        // Windows-generated location/container tokens commonly contain '&' and are not serial values.
        if (candidate.Contains('&') ||
            candidate.Equals("00000000", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("0", StringComparison.OrdinalIgnoreCase))
            return false;

        suffix = candidate;
        return true;
    }
}
