using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Android;

public sealed record AndroidRuntimePermission(
    string Name,
    bool? IsGranted,
    string Flags,
    bool IsConflicting)
{
    public override string ToString()
        => $"{Name} — {(IsConflicting ? "متعارض" : IsGranted == true ? "ممنوحة" : IsGranted == false ? "مرفوضة" : "غير معروفة")}";
}

/// <summary>
/// Extracts only permissions explicitly requested by an app and represented in the
/// PackageManager runtime-permissions section. Install-time and privileged grants are
/// never treated as runtime permissions that SoftBox can change.
/// </summary>
public static class AndroidRuntimePermissionParser
{
    private enum Section { None, Requested, Runtime, Other }

    private static readonly Regex RequestedPermissionPattern = new(
        @"^\s*(?<name>[A-Za-z0-9_.]+)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RuntimePermissionPattern = new(
        @"^\s*(?<name>[A-Za-z0-9_.]+):\s*granted=(?<granted>true|false)(?:,\s*flags=\[(?<flags>[^\]]*)\])?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<AndroidRuntimePermission> Parse(string? dumpsysPackageOutput)
    {
        var section = Section.None;
        var requested = new HashSet<string>(StringComparer.Ordinal);
        var runtime = new Dictionary<string, HashSet<bool>>(StringComparer.Ordinal);
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in (dumpsysPackageOutput ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length is 0 or > 16_384)
                continue;

            if (line.Equals("requested permissions:", StringComparison.OrdinalIgnoreCase))
            {
                section = Section.Requested;
                continue;
            }
            if (line.Equals("runtime permissions:", StringComparison.OrdinalIgnoreCase))
            {
                section = Section.Runtime;
                continue;
            }
            if (line.Equals("install permissions:", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("install permissions", StringComparison.OrdinalIgnoreCase))
            {
                section = Section.Other;
                continue;
            }

            if (line.EndsWith("permissions:", StringComparison.OrdinalIgnoreCase) &&
                section != Section.Requested && section != Section.Runtime)
            {
                section = Section.Other;
                continue;
            }

            if (section == Section.Requested)
            {
                var match = RequestedPermissionPattern.Match(rawLine);
                if (match.Success && IsSafePermissionName(match.Groups["name"].Value))
                    requested.Add(match.Groups["name"].Value);
                continue;
            }

            if (section != Section.Runtime)
                continue;

            var runtimeMatch = RuntimePermissionPattern.Match(rawLine);
            if (!runtimeMatch.Success)
                continue;

            var name = runtimeMatch.Groups["name"].Value;
            if (!IsSafePermissionName(name) || !requested.Contains(name))
                continue;

            var granted = bool.Parse(runtimeMatch.Groups["granted"].Value);
            if (!runtime.TryGetValue(name, out var states))
            {
                states = [];
                runtime.Add(name, states);
            }
            states.Add(granted);
            flags[name] = runtimeMatch.Groups["flags"].Success
                ? runtimeMatch.Groups["flags"].Value.Trim()
                : "";
        }

        return runtime
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new AndroidRuntimePermission(
                pair.Key,
                pair.Value.Count == 1 ? pair.Value.Single() : null,
                flags.GetValueOrDefault(pair.Key, ""),
                pair.Value.Count > 1))
            .ToArray();
    }

    public static bool IsSafePermissionName(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= 255 &&
           value.Contains('.') &&
           Regex.IsMatch(value, @"^[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+$", RegexOptions.CultureInvariant);
}
