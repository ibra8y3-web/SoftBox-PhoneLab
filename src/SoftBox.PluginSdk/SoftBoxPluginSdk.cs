using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SoftBox.PluginSdk;

/// <summary>Describes the public protocol and size limits supported by this Plugin SDK build.</summary>
public static class SoftBoxPluginSdkInfo
{
    /// <summary>Version of the public SDK API contract.</summary>
    public const string ApiVersion = "1.0.0";
    /// <summary>Supported JSON plugin manifest schema version.</summary>
    public const int ManifestVersion = 1;
    /// <summary>Supported isolated native plugin ABI version.</summary>
    public const int NativeAbiVersion = 1;
    /// <summary>Canonical filename for a plugin manifest.</summary>
    public const string ManifestFileName = "softbox-plugin.json";
    /// <summary>Maximum request size accepted by a plugin host message.</summary>
    public const int MaximumRequestBytes = 64 * 1024;
    /// <summary>Maximum response size accepted by a plugin host message.</summary>
    public const int MaximumResponseBytes = 1024 * 1024;
    /// <summary>Maximum supported native plugin package size.</summary>
    public const int MaximumNativePackageBytes = 100 * 1024 * 1024;
}

/// <summary>Permission identifiers recognized by the SoftBox plugin host.</summary>
public static class SoftBoxPluginPermissions
{
    /// <summary>Allows read-only access to approved device identity fields.</summary>
    public const string DeviceRead = "device.read";
    /// <summary>Allows approved read-only diagnostic operations.</summary>
    public const string DeviceDiagnostics = "device.diagnostics";
    /// <summary>Allows read-only access to shared storage exposed by the host.</summary>
    public const string SharedStorageRead = "storage.shared.read";
    /// <summary>Allows writes only to host-designated shared storage locations.</summary>
    public const string SharedStorageWrite = "storage.shared.write";
    /// <summary>Allows reading reports that the host exposes to plugins.</summary>
    public const string ReportsRead = "reports.read";
    /// <summary>Allows creating reports through host-approved APIs.</summary>
    public const string ReportsCreate = "reports.create";
    /// <summary>Allows creating notifications through host-approved APIs.</summary>
    public const string NotificationsCreate = "notifications.create";

    /// <summary>Gets the immutable set of permission identifiers supported by this SDK.</summary>
    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        DeviceRead, DeviceDiagnostics, SharedStorageRead, SharedStorageWrite,
        ReportsRead, ReportsCreate, NotificationsCreate
    };
}

/// <summary>Represents the version 1 JSON manifest consumed by SoftBox Plugin Store.</summary>
/// <param name="ManifestVersion">Schema version. This SDK currently accepts version 1.</param>
/// <param name="Id">Stable lowercase reverse-domain-like plugin identifier.</param>
/// <param name="Name">Human-readable plugin name.</param>
/// <param name="Version">Plugin semantic version.</param>
/// <param name="ApiVersion">Plugin API contract version.</param>
/// <param name="MinimumSoftBoxVersion">Minimum compatible SoftBox host version.</param>
/// <param name="EntryAssembly">Root-level DLL for native mode, or workflow.json for read-only workflow mode.</param>
/// <param name="Publisher">Publisher identity shown by the host.</param>
/// <param name="Description">Human-readable plugin description.</param>
/// <param name="RequestedPermissions">Permission identifiers requested by the plugin manifest.</param>
/// <param name="ExecutionMode">Supported execution mode: native-assembly or read-only-workflow.</param>
public sealed record SoftBoxPluginManifestV1(
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

/// <summary>Validates and serializes version 1 SoftBox plugin manifests.</summary>
public static class SoftBoxPluginManifestWriter
{
    private static readonly Regex IdPattern = new(
        @"^[a-z][a-z0-9]*(?:[.-][a-z0-9]+){1,5}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SemVerPattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>Validates a manifest against the SDK schema, size limits and permission policy.</summary>
    /// <param name="manifest">Manifest to validate; null is reported as an error.</param>
    /// <returns>A list of validation errors. An empty list means the manifest passes local schema validation.</returns>
    public static IReadOnlyList<string> Validate(SoftBoxPluginManifestV1? manifest)
    {
        var errors = new List<string>();
        if (manifest is null)
            return ["Plugin manifest is missing."];

        if (manifest.ManifestVersion != SoftBoxPluginSdkInfo.ManifestVersion)
            errors.Add("Unsupported plugin manifest version.");
        if (string.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.Length > 100 || !IdPattern.IsMatch(manifest.Id))
            errors.Add("Plugin ID must be a lowercase reverse-domain-like ID such as org.example.tool.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 120 || manifest.Name.Any(char.IsControl))
            errors.Add("Plugin name is required and must be at most 120 characters.");
        if (!IsSemVer(manifest.Version) || !IsSemVer(manifest.ApiVersion) || !IsSemVer(manifest.MinimumSoftBoxVersion))
            errors.Add("Version, API version and minimum host version must use semantic versioning.");
        if (string.IsNullOrWhiteSpace(manifest.Publisher) || manifest.Publisher.Length > 160 || manifest.Publisher.Any(char.IsControl))
            errors.Add("Publisher is required and must be at most 160 characters.");
        if (string.IsNullOrWhiteSpace(manifest.Description) || manifest.Description.Length > 2000 || manifest.Description.Contains('\0'))
            errors.Add("Description is required and must be at most 2000 characters.");

        if (manifest.RequestedPermissions is null || manifest.RequestedPermissions.Count > 32)
            errors.Add("Requested permissions are missing or exceed the 32-entry limit.");
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var permission in manifest.RequestedPermissions)
            {
                if (string.IsNullOrWhiteSpace(permission) || permission.Length > 80 || !SoftBoxPluginPermissions.Allowed.Contains(permission))
                    errors.Add("Unknown or forbidden permission: " + (permission ?? "<null>"));
                else if (!seen.Add(permission))
                    errors.Add("Duplicate permission: " + permission);
            }
        }

        if (manifest.ExecutionMode == "native-assembly")
        {
            if (!IsRootDll(manifest.EntryAssembly))
                errors.Add("Native entry assembly must be one root-level DLL filename.");
            if (manifest.RequestedPermissions is { Count: > 0 })
                errors.Add("Native AppContainer plugins must request an empty permission list.");
        }
        else if (manifest.ExecutionMode == "read-only-workflow")
        {
            if (!string.Equals(manifest.EntryAssembly, "workflow.json", StringComparison.Ordinal))
                errors.Add("Workflow plugins must use workflow.json.");
            if (manifest.RequestedPermissions is null ||
                manifest.RequestedPermissions.Any(permission =>
                    permission is not (SoftBoxPluginPermissions.DeviceRead or SoftBoxPluginPermissions.DeviceDiagnostics)))
                errors.Add("Workflow plugins may request read-only diagnostics permissions only.");
        }
        else
        {
            errors.Add("Unsupported plugin execution mode: " + manifest.ExecutionMode);
        }

        return errors;
    }

    /// <summary>Serializes a manifest after successful validation.</summary>
    /// <param name="manifest">Manifest to validate and serialize.</param>
    /// <returns>Indented UTF-8-compatible JSON text for the manifest.</returns>
    /// <exception cref="ArgumentException">The manifest is null or fails validation.</exception>
    public static string Serialize(SoftBoxPluginManifestV1 manifest)
    {
        var errors = Validate(manifest);
        if (errors.Count > 0)
            throw new ArgumentException("Invalid SoftBox plugin manifest: " + string.Join(" ", errors), nameof(manifest));
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    private static bool IsSemVer(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && SemVerPattern.IsMatch(value);

    private static bool IsRootDll(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 240 ||
            Path.IsPathRooted(value) ||
            !value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
            return false;

        var stem = value[..^4];
        if (stem.Length == 0 || stem.EndsWith('.') || stem.EndsWith(' '))
            return false;

        var firstSegment = stem.Split('.')[0];
        return !Regex.IsMatch(firstSegment, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
