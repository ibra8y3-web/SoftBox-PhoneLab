using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Plugins;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record NativePluginRunReport(
    string PluginId, string PluginName, string Publisher, string PackageSha256,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc,
    bool Success, bool Sandboxed, bool TimedOut, int? ExitCode,
    string? ResponseJson, string? Error, string AppContainerName);

/// <summary>Executes signed native plugins only inside a no-capability AppContainer worker.</summary>
public sealed class NativePluginRuntimeService
{
    private const int MaxRequest = 64 * 1024;
    private const int MaxResponse = 1024 * 1024;
    private const long MaxDllSize = 80L * 1024 * 1024;
    private readonly PluginPackageInspector _inspector;
    private readonly WindowsAppContainerProcessLauncher _launcher;
    private readonly string _hostDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public NativePluginRuntimeService(PluginPackageInspector? inspector = null,
        WindowsAppContainerProcessLauncher? launcher = null, string? hostDirectory = null)
    {
        _inspector = inspector ?? new PluginPackageInspector();
        _launcher = launcher ?? new WindowsAppContainerProcessLauncher();
        _hostDirectory = Path.GetFullPath(hostDirectory ?? Path.Combine(AppContext.BaseDirectory, "PluginHost"));
    }

    public async Task<NativePluginRunReport> RunAsync(string packagePath, string requestJson = "{}",
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        if (!File.Exists(packagePath)) throw new FileNotFoundException("Native plugin package not found.", packagePath);
        if (Encoding.UTF8.GetByteCount(requestJson ?? "") > MaxRequest)
            throw new InvalidDataException("Plugin input exceeds 64 KiB.");
        using (JsonDocument.Parse(requestJson)) { }

        var inspection = await _inspector.InspectAsync(packagePath, cancellationToken);
        if (inspection.PackageErrors.Count != 0 || !inspection.ManifestValidation.IsValid ||
            !inspection.PublisherSignatureVerified || inspection.Manifest is null)
            throw new UnauthorizedAccessException("Native plugin requires a valid package and a trusted publisher signature.");

        var manifest = inspection.Manifest;
        if (manifest.ExecutionMode != "native-assembly" ||
            !PluginManifestValidator.IsSafeRelativeAssemblyPath(manifest.EntryAssembly) ||
            manifest.EntryAssembly.Contains('/') ||
            manifest.RequestedPermissions is null || manifest.RequestedPermissions.Count != 0)
            throw new UnauthorizedAccessException("Native AppContainer plugins must be a root DLL and request no SoftBox privileges.");

        var sourceHost = Path.Combine(_hostDirectory, "SoftBox.PluginHost.exe");
        if (!File.Exists(sourceHost)) throw new FileNotFoundException("SoftBox.PluginHost is missing from the Windows package.", sourceHost);

        var root = Path.Combine(Path.GetTempPath(), "SoftBox-PluginSandbox-" + Guid.NewGuid().ToString("N"));
        var runtimeFolder = Path.Combine(root, "runtime");
        var pluginFolder = Path.Combine(root, "plugin");
        var exchangeFolder = Path.Combine(root, "exchange");
        Directory.CreateDirectory(runtimeFolder);
        Directory.CreateDirectory(pluginFolder);
        Directory.CreateDirectory(exchangeFolder);
        try
        {
            CopyRuntimeFiles(_hostDirectory, runtimeFolder);
            var stagedDll = await ExtractDllsAsync(packagePath, inspection.PackageSha256, manifest.EntryAssembly, pluginFolder, cancellationToken);
            var requestPath = Path.Combine(exchangeFolder, "request.json");
            var responsePath = Path.Combine(exchangeFolder, "response.json");
            var payload = JsonSerializer.Serialize(new HostRequest(stagedDll, requestJson), JsonOptions);
            await File.WriteAllTextAsync(requestPath, payload, new UTF8Encoding(false), cancellationToken);

            var result = await _launcher.RunAsync(root,
                Path.Combine(runtimeFolder, "SoftBox.PluginHost.exe"),
                requestPath, responsePath, TimeSpan.FromMinutes(2), cancellationToken);

            if (result.TimedOut || result.Cancelled)
                return Build(false, result.TimedOut, result.ExitCode, null, result.Error, result.AppContainerName);
            if (!File.Exists(responsePath)) return Build(false, false, result.ExitCode, null, result.Error ?? "Worker produced no response.", result.AppContainerName);
            var responseInfo = new FileInfo(responsePath);
            if (responseInfo.Length is <= 0 or > MaxResponse + 4096)
                return Build(false, false, result.ExitCode, null, "Worker response exceeded 1 MiB.", result.AppContainerName);

            var response = JsonSerializer.Deserialize<HostResponse>(await File.ReadAllTextAsync(responsePath), JsonOptions);
            if (response is null || !response.Success || string.IsNullOrWhiteSpace(response.Json))
                return Build(false, false, result.ExitCode, null, response?.Error ?? result.Error ?? "Plugin returned no valid JSON.", result.AppContainerName);
            if (Encoding.UTF8.GetByteCount(response.Json) > MaxResponse)
                return Build(false, false, result.ExitCode, null, "Plugin output exceeded 1 MiB.", result.AppContainerName);
            using (JsonDocument.Parse(response.Json)) { }
            return Build(true, false, result.ExitCode, response.Json, null, result.AppContainerName);

            NativePluginRunReport Build(bool success, bool timedOut, int? exitCode, string? json, string? error, string container)
                => new(manifest.Id, manifest.Name, inspection.TrustedPublisher ?? manifest.Publisher,
                    inspection.PackageSha256, started, DateTimeOffset.UtcNow,
                    success, true, timedOut, exitCode, json, error, container);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    private static void CopyRuntimeFiles(string source, string destination)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length is <= 0 or > 100 * 1024 * 1024)
                throw new InvalidDataException("PluginHost runtime contains a reparse point or oversized file.");
            total = checked(total + info.Length);
            if (total > 250 * 1024 * 1024) throw new InvalidDataException("PluginHost runtime is larger than 250 MiB.");
            var target = Path.GetFullPath(Path.Combine(destination, Path.GetRelativePath(source, file)));
            EnsureUnderRoot(destination, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        if (!File.Exists(Path.Combine(destination, "SoftBox.PluginHost.exe")))
            throw new FileNotFoundException("PluginHost executable was not copied.");
    }

    private static async Task<string> ExtractDllsAsync(string packagePath, string packageHash,
        string entryAssembly, string destination, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(packageHash)))
            throw new UnauthorizedAccessException("Package bytes changed after signature validation.");
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var dlls = archive.Entries.Where(x => Path.GetExtension(x.FullName).Equals(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (dlls.Length is < 1 or > 32) throw new InvalidDataException("Native package must contain 1-32 root DLL files.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        var found = false;
        foreach (var entry in dlls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.Contains('/') || entry.FullName.Contains('\\') ||
                Path.GetFileName(entry.FullName) != entry.FullName || !names.Add(entry.FullName) ||
                entry.Length is <= 0 or > MaxDllSize)
                throw new InvalidDataException("Native DLL dependencies must be unique root files, each no larger than 80 MiB.");
            total = checked(total + entry.Length);
            if (total > MaxDllSize) throw new InvalidDataException("Native DLL dependency set exceeds 80 MiB.");
            var target = Path.Combine(destination, entry.FullName);
            await using var source = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(output, cancellationToken);
            if (entry.FullName.Equals(entryAssembly, StringComparison.OrdinalIgnoreCase)) found = true;
        }
        if (!found) throw new InvalidDataException("Signed native entry DLL is missing.");
        return Path.Combine(destination, entryAssembly);
    }

    private static void EnsureUnderRoot(string root, string path)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escaped plugin staging directory.");
    }

    private sealed record HostRequest(string PluginPath, string Json);
    private sealed record HostResponse(bool Success, int ApiVersion, string? Json, string? Error, DateTimeOffset CompletedAtUtc);
}
