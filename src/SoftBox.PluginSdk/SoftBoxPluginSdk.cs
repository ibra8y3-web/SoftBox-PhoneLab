using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SoftBox.PluginSdk;

public static class SoftBoxPluginSdkInfo
{
    public const string ApiVersion = "1.0.0";
    public const int ManifestVersion = 1;
    public const int NativeAbiVersion = 1;
    public const string ManifestFileName = "softbox-plugin.json";
    public const int MaximumRequestBytes = 64 * 1024;
    public const int MaximumResponseBytes = 1024 * 1024;
    public const int MaximumNativePackageBytes = 100 * 1024 * 1024;
}

public static class SoftBoxPluginPermissions
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
        DeviceRead, DeviceDiagnostics, SharedStorageRead, SharedStorageWrite,
        ReportsRead, ReportsCreate, NotificationsCreate
    };
}

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
                if (string.IsNullOrWhiteSpace(permission) || !SoftBoxPluginPermissions.Allowed.Contains(permission))
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
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 240 &&
           !Path.IsPathRooted(value) && value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
           !value.Contains('/') && !value.Contains('\\') && !value.Any(char.IsControl);
}
