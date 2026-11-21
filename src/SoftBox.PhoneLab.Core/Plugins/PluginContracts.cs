using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Plugins;

public static class PluginPermissionIds
{
    public const string DeviceRead = "device.read";
    public const string DeviceDiagnostics = "device.diagnostics";
    public const string SharedStorageRead = "storage.shared.read";
    public const string SharedStorageWrite = "storage.shared.write";
    public const string ReportsRead = "reports.read";
    public const string ReportsCreate = "reports.create";
    public const string NotificationsCreate = "notifications.create";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        DeviceRead,
        DeviceDiagnostics,
        SharedStorageRead,
        SharedStorageWrite,
        ReportsRead,
        ReportsCreate,
        NotificationsCreate
    };
}

public sealed record PluginManifest(
    int ManifestVersion,
    string Id,
    string Name,
    string Version,
    string ApiVersion,
    string MinimumSoftBoxVersion,
    string EntryAssembly,
    string Publisher,
    string Description,
    IReadOnlyList<string> RequestedPermissions,
    string ExecutionMode = "native-assembly");

public sealed record PluginManifestValidation(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Initial SDK contract validator. It does not load assemblies or grant permissions.
/// Every unknown permission fails closed.
/// </summary>
public static class PluginManifestValidator
{
    private static readonly Regex IdPattern = new(
        @"^[a-z][a-z0-9]*(?:[.-][a-z0-9]+){1,5}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SemVerPattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static PluginManifestValidation Validate(PluginManifest? manifest)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (manifest is null)
            return new PluginManifestValidation(false, ["Plugin manifest is missing."], warnings);

        if (manifest.ManifestVersion != 1)
            errors.Add("Unsupported Plugin Manifest schema version.");
        if (string.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.Length > 100 ||
            !IdPattern.IsMatch(manifest.Id))
            errors.Add("Plugin ID must be a lowercase reverse-domain-like identifier such as org.example.tool.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 120 ||
            manifest.Name.Any(char.IsControl))
            errors.Add("Plugin display name is required and must be at most 120 characters.");
        if (!IsSemVer(manifest.Version))
            errors.Add("Plugin version must use semantic versioning.");
        if (!IsSemVer(manifest.ApiVersion))
            errors.Add("Plugin API version must use semantic versioning.");
        if (!IsSemVer(manifest.MinimumSoftBoxVersion))
            errors.Add("Minimum SoftBox version must use semantic versioning.");
        if (manifest.ExecutionMode == "native-assembly")
        {
            if (!IsSafeRelativeAssemblyPath(manifest.EntryAssembly))
                errors.Add("Native entry assembly must be a relative .dll path without traversal segments.");
            warnings.Add("Native plugin assemblies are not loaded or executed by the current SoftBox runtime.");
        }
        else if (manifest.ExecutionMode == "read-only-workflow")
        {
            if (!string.Equals(manifest.EntryAssembly, "workflow.json", StringComparison.Ordinal))
                errors.Add("Read-only workflow plugins must use workflow.json as their entry point.");
            if (manifest.RequestedPermissions is null ||
                manifest.RequestedPermissions.Any(permission =>
                    permission is not (PluginPermissionIds.DeviceRead or PluginPermissionIds.DeviceDiagnostics)))
                errors.Add("Read-only workflow plugins may request diagnostics/read-only permissions only.");
        }
        else
        {
            errors.Add("Unsupported plugin execution mode: " + manifest.ExecutionMode);
        }
        if (string.IsNullOrWhiteSpace(manifest.Publisher) || manifest.Publisher.Length > 160 ||
            manifest.Publisher.Any(char.IsControl))
            errors.Add("Publisher is required and must be at most 160 characters.");
        if (string.IsNullOrWhiteSpace(manifest.Description) || manifest.Description.Length > 2000 ||
            manifest.Description.Contains('\0'))
            errors.Add("Description is required and must be at most 2000 characters.");

        if (manifest.RequestedPermissions is null || manifest.RequestedPermissions.Count > 32)
        {
            errors.Add("Requested permission list is missing or exceeds 32 entries.");
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var permission in manifest.RequestedPermissions)
            {
                if (string.IsNullOrWhiteSpace(permission) || permission.Length > 80 ||
                    !PluginPermissionIds.Allowed.Contains(permission))
                    errors.Add("Unknown or forbidden permission: " + (permission ?? "<null>"));
                else if (!seen.Add(permission))
                    errors.Add("Duplicate permission: " + permission);
            }
        }

        warnings.Add("Manifest validation does not authenticate publisher identity or code signature.");
        warnings.Add("Plugin assemblies are not loaded or executed by this validator.");
        return new PluginManifestValidation(errors.Count == 0, errors, warnings);
    }

    public static bool IsSemVer(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && SemVerPattern.IsMatch(value);

    public static bool IsSafeRelativeAssemblyPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 240 ||
            Path.IsPathRooted(value) || value.Contains('\\') ||
            !value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            value.Any(char.IsControl))
            return false;

        return !value.Split('/').Any(segment => segment is "" or "." or "..");
    }
}
