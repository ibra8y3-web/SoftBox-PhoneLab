using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace SoftBox.PhoneLab.Core.Security;

public enum KeyguardVisibility
{
    Unknown,
    Visible,
    NotVisible
}

/// <summary>
/// Read-only Android access-protection observations. These values are deliberately limited
/// to public shell diagnostics; they do not attest FRP, OEM account activation or carrier unlock.
/// </summary>
public sealed record DeviceAccessDiagnosticsReport(
    bool PolicyOutputReadable,
    bool HasDeviceOwner,
    bool HasProfileOwner,
    bool HasActiveAdmin,
    KeyguardVisibility KeyguardVisibility,
    string SimStateProperty,
    string NetworkOperatorProperty)
{
    public bool HasManagementOwner => HasDeviceOwner || HasProfileOwner;

    /// <summary>Only component names found in the public Device Policy Manager dump.</summary>
    public IReadOnlyList<string> ManagementComponents { get; init; } = Array.Empty<string>();

    /// <summary>Only restriction tokens matching Android's public no_* naming convention.</summary>
    public IReadOnlyList<string> ActiveUserRestrictions { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Conservative parser for a small set of Android dumpsys/getprop indicators.
/// Missing or conflicting fields remain Unknown; absence is never treated as proof
/// that an account lock, MDM policy or network lock is absent.
/// </summary>
public static class DeviceAccessDiagnosticsParser
{
    private static readonly Regex KeyguardPattern = new(
        @"\b(?:mShowingLockscreen|mKeyguardShowing)\s*=\s*(true|false)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DeviceOwnerPattern = new(
        @"(?im)^\s*Device\s+Owner\b[^\r\n]*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ProfileOwnerPattern = new(
        @"(?im)^\s*Profile\s+Owner\b[^\r\n]*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ActiveAdminPattern = new(
        @"(?im)^.*\bActive\s+admin\b.*(?:ComponentInfo|admin\s*=).*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ComponentInfoPattern = new(
        @"ComponentInfo\{(?<component>[A-Za-z0-9_.$]+/[A-Za-z0-9_.$]+)\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UserRestrictionPattern = new(
        @"\bno_[a-z0-9_]+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static DeviceAccessDiagnosticsReport Parse(
        string? devicePolicyOutput,
        string? windowOutput,
        string? simStateProperty,
        string? networkOperatorProperty)
    {
        var policy = devicePolicyOutput ?? string.Empty;
        var policyReadable =
            !string.IsNullOrWhiteSpace(policy) &&
            !policy.Contains("Can't find service", StringComparison.OrdinalIgnoreCase) &&
            !policy.Contains("Permission Denial", StringComparison.OrdinalIgnoreCase) &&
            !policy.Contains("not found", StringComparison.OrdinalIgnoreCase);

        var visibilityValues = KeyguardPattern.Matches(windowOutput ?? string.Empty)
            .Select(match => bool.Parse(match.Groups[1].Value))
            .Distinct()
            .ToArray();

        var visibility = visibilityValues.Length != 1
            ? KeyguardVisibility.Unknown
            : visibilityValues[0] ? KeyguardVisibility.Visible : KeyguardVisibility.NotVisible;

        var report = new DeviceAccessDiagnosticsReport(
            policyReadable,
            policyReadable && DeviceOwnerPattern.IsMatch(policy),
            policyReadable && ProfileOwnerPattern.IsMatch(policy),
            policyReadable && ActiveAdminPattern.IsMatch(policy),
            visibility,
            NormalizeProperty(simStateProperty),
            NormalizeProperty(networkOperatorProperty));

        return !policyReadable
            ? report
            : report with
            {
                ManagementComponents = ComponentInfoPattern.Matches(policy)
                    .Select(match => match.Groups["component"].Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToArray(),
                ActiveUserRestrictions = UserRestrictionPattern.Matches(policy)
                    .Select(match => match.Value.ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .Take(60)
                    .ToArray()
            };
    }

    private static string NormalizeProperty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = new string(value
            .Where(character => !char.IsControl(character))
            .Take(80)
            .ToArray());

        return normalized.Trim();
    }
}
