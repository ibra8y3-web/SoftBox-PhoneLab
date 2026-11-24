using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record AirPlayReceiverRelease(string TagName, Uri DownloadUri, string Sha256);
public sealed record AirPlayReceiverInstallation(string ExecutablePath, string Version, bool InstalledNow);

/// <summary>
/// Finds an existing AirPlay-Windows installation or downloads the latest official x64 release.
/// The complete ZIP is SHA-256 checked against the digest published by the official GitHub Releases API
/// before any executable is extracted or launched.
/// </summary>
public sealed class AirPlayReceiverBootstrapService
{
    private const string Repository = "moieric11/AirPlay-Windows";
    private const string LatestReleaseApi = "https://api.github.com/repos/moieric11/AirPlay-Windows/releases/latest";
    private const long MaximumDownloadBytes = 80L * 1024 * 1024;
    private const long MaximumExpandedBytes = 600L * 1024 * 1024;
    private const int MaximumArchiveEntries = 1024;
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    private readonly string _managedInstallRoot;

    public AirPlayReceiverBootstrapService(string? managedInstallRoot = null)
    {
        _managedInstallRoot = Path.GetFullPath(managedInstallRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "Tools", "AirPlay-Windows"));
    }

    public async Task<AirPlayReceiverInstallation> EnsureInstalledAsync(
        string? preferredExecutablePath = null,
        CancellationToken cancellationToken = default)
    {
        var existing = FindExistingExecutable(preferredExecutablePath);
        if (existing is not null)
            return new AirPlayReceiverInstallation(existing, "Existing installation", false);

        await InstallGate.WaitAsync(cancellationToken);
        try
        {
            existing = FindExistingExecutable(preferredExecutablePath);
            if (existing is not null)
                return new AirPlayReceiverInstallation(existing, "Existing installation", false);

            var release = await GetLatestReleaseAsync(cancellationToken);
            Directory.CreateDirectory(_managedInstallRoot);

            var stagingDirectory = Path.Combine(_managedInstallRoot, ".staging-" + Guid.NewGuid().ToString("N"));
            var archivePath = Path.Combine(Path.GetTempPath(), "softbox-airplay-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                await DownloadAndVerifyAsync(release, archivePath, cancellationToken);
                await ExtractSafeArchiveAsync(archivePath, stagingDirectory, cancellationToken);

                var candidates = Directory.EnumerateFiles(stagingDirectory, "airplay-windows.exe", SearchOption.AllDirectories).ToArray();
                if (candidates.Length != 1)
                    throw new InvalidDataException(
                        $"Official AirPlay release must contain exactly one airplay-windows.exe; found {candidates.Length}.");

                var versionDirectoryName = release.TagName + "-" + release.Sha256[..8];
                var destination = Path.Combine(_managedInstallRoot, versionDirectoryName);
                if (Directory.Exists(destination))
                    destination += "-" + Guid.NewGuid().ToString("N")[..8];

                Directory.Move(stagingDirectory, destination);
                var relativeExecutable = Path.GetRelativePath(stagingDirectory, candidates[0]);
                var installedExecutable = Path.GetFullPath(Path.Combine(destination, relativeExecutable));
                AirPlayReceiverService.ValidateReceiverPath(installedExecutable);
                return new AirPlayReceiverInstallation(installedExecutable, release.TagName, true);
            }
            finally
            {
                try { if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true); } catch { }
                try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
            }
        }
        finally
        {
            InstallGate.Release();
        }
    }

    public static AirPlayReceiverRelease ParseLatestReleaseManifest(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            throw new InvalidDataException("GitHub returned a draft AirPlay release.");
        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True)
            throw new InvalidDataException("GitHub returned a prerelease AirPlay build.");

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag) ||
            tag.Length > 64 ||
            tag.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
            throw new InvalidDataException("The official AirPlay release has an invalid version tag.");

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The official AirPlay release does not expose its asset list.");

        var expectedAssetName = $"airplay-windows-{tag}-x64.zip";
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (!string.Equals(name, expectedAssetName, StringComparison.Ordinal))
                continue;

            var download = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;
            var digest = asset.TryGetProperty("digest", out var digestElement)
                ? digestElement.GetString()
                : null;

            if (!Uri.TryCreate(download, UriKind.Absolute, out var downloadUri) ||
                downloadUri.Scheme != Uri.UriSchemeHttps ||
                !downloadUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                !downloadUri.AbsolutePath.StartsWith(
                    $"/{Repository}/releases/download/{tag}/", StringComparison.Ordinal))
                throw new InvalidDataException("The AirPlay release asset URL is not an official HTTPS GitHub release URL.");

            if (string.IsNullOrWhiteSpace(digest) ||
                !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                digest.Length != 71 ||
                digest[7..].Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("The official AirPlay asset has no valid SHA-256 digest. Installation was stopped.");

            return new AirPlayReceiverRelease(tag, downloadUri, digest[7..].ToLowerInvariant());
        }

        throw new FileNotFoundException($"The official release does not contain the expected x64 asset: {expectedAssetName}");
    }

    private async Task<AirPlayReceiverRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.ParseAdd("SoftBox-PhoneLab/0.1");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > 1024 * 1024)
            throw new InvalidDataException("GitHub release metadata exceeds the safe size limit.");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (json.Length > 1024 * 1024)
            throw new InvalidDataException("GitHub release metadata exceeds the safe size limit.");
        return ParseLatestReleaseManifest(json);
    }

    private static async Task DownloadAndVerifyAsync(
        AirPlayReceiverRelease release,
        string archivePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUri);
        request.Headers.UserAgent.ParseAdd("SoftBox-PhoneLab/0.1");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || finalUri.Scheme != Uri.UriSchemeHttps ||
            !(finalUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
              finalUri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The AirPlay download was redirected to an untrusted or non-HTTPS host.");

        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
            throw new InvalidDataException("The AirPlay release ZIP is larger than the allowed download limit.");

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[128 * 1024];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > MaximumDownloadBytes)
                    throw new InvalidDataException("The AirPlay release ZIP exceeded the allowed download limit.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            await destination.FlushAsync(cancellationToken);
        }

        await using var verify = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualDigest = Convert.ToHexString(await SHA256.HashDataAsync(verify, cancellationToken)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualDigest),
                Convert.FromHexString(release.Sha256)))
            throw new InvalidDataException("AirPlay-Windows ZIP SHA-256 does not match the digest published by its official GitHub release.");
    }

    private static async Task ExtractSafeArchiveAsync(
        string archivePath,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        await using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        if (archive.Entries.Count is < 1 or > MaximumArchiveEntries)
            throw new InvalidDataException("The AirPlay ZIP has an unexpected number of entries.");

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedName = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalizedName) ||
                normalizedName.StartsWith("/", StringComparison.Ordinal) ||
                Path.IsPathRooted(normalizedName) ||
                normalizedName.Split('/').Any(segment => segment is "." or "..") ||
                normalizedName.Contains(':') ||
                normalizedName.Contains('\0'))
                throw new InvalidDataException("The AirPlay ZIP contains an unsafe entry path.");

            var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType == 0xA000)
                throw new InvalidDataException("The AirPlay ZIP contains a symbolic link; extraction was stopped.");

            expandedBytes += entry.Length;
            if (expandedBytes > MaximumExpandedBytes)
                throw new InvalidDataException("The AirPlay ZIP exceeds the safe expanded-size limit.");

            var outputPath = Path.GetFullPath(Path.Combine(root, normalizedName.Replace('/', Path.DirectorySeparatorChar)));
            if (!outputPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) &&
                !outputPath.Equals(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The AirPlay ZIP attempted to write outside its installation directory.");

            if (normalizedName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(outputPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await using var input = entry.Open();
            await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, 64 * 1024, cancellationToken);
        }
    }

    private string? FindExistingExecutable(string? preferredPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
        {
            try { return AirPlayReceiverService.ValidateReceiverPath(preferredPath); }
            catch { }
        }

        var candidates = new List<string>
        {
            // Search only the SoftBox application folder and its own managed tool directory.
            // A random executable found on PATH or an unrelated install folder is not trusted.
            Path.Combine(AppContext.BaseDirectory, "Tools", "AirPlay-Windows", "airplay-windows.exe")
        };

        if (Directory.Exists(_managedInstallRoot))
        {
            try
            {
                candidates.AddRange(Directory.EnumerateFiles(_managedInstallRoot, "airplay-windows.exe", SearchOption.AllDirectories));
            }
            catch { }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate)) continue;
            try { return AirPlayReceiverService.ValidateReceiverPath(candidate); }
            catch { }
        }
        return null;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = System.Net.DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SoftBox-PhoneLab/0.1");
        return client;
    }
}
