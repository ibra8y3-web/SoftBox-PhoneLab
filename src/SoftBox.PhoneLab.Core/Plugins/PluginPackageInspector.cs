using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SoftBox.PhoneLab.Core.Plugins;

public sealed record PluginPackageInspection(
    string PackagePath,
    string PackageSha256,
    long PackageSizeBytes,
    PluginManifest? Manifest,
    PluginManifestValidation ManifestValidation,
    IReadOnlyList<string> PackageErrors,
    IReadOnlyList<string> Warnings,
    bool PublisherSignatureVerified,
    bool CodeLoadedOrExecuted,
    bool SignaturePresent = false,
    string? PublisherKeyId = null,
    string? TrustedPublisher = null);

/// <summary>
/// Inspects plugin ZIP packages and verifies RSA signatures against the current-user publisher trust store.
/// It never loads native plugin assemblies.
/// </summary>
public sealed class PluginPackageInspector
{
    public const string ManifestEntryName = "softbox-plugin.json";
    private readonly PluginPublisherTrustStore _trustStore;

    public PluginPackageInspector(PluginPublisherTrustStore? trustStore = null)
        => _trustStore = trustStore ?? new PluginPublisherTrustStore();
    private const long MaximumPackageBytes = 100L * 1024 * 1024;
    private const long MaximumExpandedBytes = 100L * 1024 * 1024;
    private const long MaximumEntryBytes = 50L * 1024 * 1024;
    private const int MaximumEntries = 1000;
    private const int MaximumManifestBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16
    };

    public async Task<PluginPackageInspection> InspectAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            throw new FileNotFoundException("Plugin package does not exist.", packagePath);

        var fullPath = Path.GetFullPath(packagePath);
        var info = new FileInfo(fullPath);
        if (info.Length <= 0 || info.Length > MaximumPackageBytes)
            throw new InvalidDataException("Plugin package exceeds the 100 MiB size limit.");

        string packageHash;
        await using (var packageStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            packageHash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken))
                .ToLowerInvariant();
        }

        var packageErrors = new List<string>();
        var warnings = new List<string>
        {
            "Package SHA-256 identifies these exact bytes only; it does not authenticate the publisher.",
            "No DLL or plugin code is loaded or executed by this inspection service.",
            "PublisherSignatureVerified remains false until a trusted signing-root and signature verification service is configured."
        };
        PluginManifest? manifest = null;
        PluginManifestValidation validation = new(false, ["Plugin manifest was not read."], []);
        var signaturePresent = false;
        var publisherSignatureVerified = false;
        string? publisherKeyId = null;
        string? trustedPublisher = null;
        try
        {
            using var archive = ZipFile.OpenRead(fullPath);
            if (archive.Entries.Count is < 1 or > MaximumEntries)
                throw new InvalidDataException($"Plugin archive must contain 1 to {MaximumEntries} entries.");

            long expandedBytes = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateArchiveEntry(entry);
                if (!names.Add(entry.FullName))
                    packageErrors.Add("Duplicate ZIP path: " + entry.FullName);
                if (entry.Length > MaximumEntryBytes)
                    packageErrors.Add("ZIP entry exceeds 50 MiB: " + entry.FullName);
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaximumExpandedBytes)
                    packageErrors.Add("Expanded package content exceeds 100 MiB.");

                if (entry.Length > 1_000_000 &&
                    (entry.CompressedLength == 0 || entry.Length / Math.Max(1, entry.CompressedLength) > 1000))
                    packageErrors.Add("Suspicious compression ratio: " + entry.FullName);
            }

            var signatureEntries = archive.Entries
                .Where(entry => entry.FullName.Equals(PluginSignaturePayload.SignatureEntryName, StringComparison.Ordinal))
                .ToArray();
            signaturePresent = signatureEntries.Length > 0;
            if (signatureEntries.Length > 1)
                packageErrors.Add("Package must not contain duplicate detached signature files.");

            var manifests = archive.Entries
                .Where(entry => entry.FullName.Equals(ManifestEntryName, StringComparison.Ordinal))
                .ToArray();
            if (manifests.Length != 1)
            {
                packageErrors.Add("Package must contain exactly one root softbox-plugin.json manifest.");
            }
            else if (manifests[0].Length > MaximumManifestBytes)
            {
                packageErrors.Add("Plugin manifest exceeds 64 KiB.");
            }
            else
            {
                await using var stream = manifests[0].Open();
                manifest = await JsonSerializer.DeserializeAsync<PluginManifest>(stream, JsonOptions, cancellationToken);
                validation = PluginManifestValidator.Validate(manifest);
                if (manifest is not null)
                {
                    var entryExists = manifest.ExecutionMode switch
                    {
                        "native-assembly" => PluginManifestValidator.IsSafeRelativeAssemblyPath(manifest.EntryAssembly) && names.Contains(manifest.EntryAssembly),
                        "read-only-workflow" => string.Equals(manifest.EntryAssembly, "workflow.json", StringComparison.Ordinal) && names.Contains("workflow.json"),
                        _ => false
                    };
                    if (!entryExists)
                        packageErrors.Add("Plugin entry point does not exist or does not match the execution mode: " + manifest.EntryAssembly);
                }
            }

            if (signatureEntries.Length == 1 && signatureEntries[0].Length is > 0 and <= 16 * 1024 && packageErrors.Count == 0)
            {
                PluginPackageSignature? signature;
                await using (var signatureStream = signatureEntries[0].Open())
                    signature = await JsonSerializer.DeserializeAsync<PluginPackageSignature>(signatureStream, JsonOptions, cancellationToken);

                if (signature is null ||
                    signature.KeyId is null || signature.KeyId.Length != 64 ||
                    !signature.KeyId.All(Uri.IsHexDigit) ||
                    !string.Equals(signature.Algorithm, PluginSignaturePayload.AlgorithmName, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(signature.SignatureBase64) || signature.SignatureBase64.Length > 16 * 1024)
                {
                    packageErrors.Add("Detached plugin signature metadata is invalid.");
                }
                else
                {
                    publisherKeyId = signature.KeyId.ToLowerInvariant();
                    var trusted = await _trustStore.FindAsync(publisherKeyId, cancellationToken);
                    if (trusted is null)
                    {
                        warnings.Add("Plugin signature key is not trusted on this Windows profile: " + publisherKeyId);
                    }
                    else if (manifest is null ||
                             !trusted.PublisherName.Equals(manifest.Publisher, StringComparison.OrdinalIgnoreCase))
                    {
                        packageErrors.Add("Plugin manifest publisher does not match the trusted key owner.");
                    }
                    else
                    {
                        try
                        {
                            var signatureBytes = Convert.FromBase64String(signature.SignatureBase64);
                            var payload = await PluginSignaturePayload.ComputeAsync(archive.Entries, cancellationToken);
                            publisherSignatureVerified = PluginSignaturePayload.Verify(payload, signatureBytes, trusted.PublicKeyPem);
                            if (!publisherSignatureVerified)
                                packageErrors.Add("Plugin RSA signature does not match the package content.");
                            else
                                trustedPublisher = trusted.PublisherName;
                        }
                        catch (FormatException)
                        {
                            packageErrors.Add("Plugin signature is not valid Base64.");
                        }
                    }
                }
            }
            else if (signatureEntries.Length == 1)
            {
                packageErrors.Add("Detached plugin signature is empty or exceeds 16 KiB.");
            }
            else if (!signaturePresent)
            {
                warnings.Add("Plugin package is unsigned; its publisher identity is not authenticated.");
            }
        }
        catch (InvalidDataException ex)
        {
            packageErrors.Add(ex.Message);
        }
        catch (JsonException ex)
        {
            packageErrors.Add("Plugin manifest JSON is invalid: " + ex.Message);
        }

        return new PluginPackageInspection(
            fullPath,
            packageHash,
            info.Length,
            manifest,
            validation,
            packageErrors,
            warnings.Concat(validation.Warnings).Distinct(StringComparer.Ordinal).ToArray(),
            PublisherSignatureVerified: publisherSignatureVerified,
            CodeLoadedOrExecuted: false,
            SignaturePresent: signaturePresent,
            PublisherKeyId: publisherKeyId,
            TrustedPublisher: trustedPublisher);
    }

    private static void ValidateArchiveEntry(ZipArchiveEntry entry)
    {
        var name = entry.FullName;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 512 ||
            name.StartsWith("/", StringComparison.Ordinal) || name.Contains('\\') ||
            name.Contains(':') || name.Any(char.IsControl))
            throw new InvalidDataException("Unsafe path detected in plugin package.");

        var normalized = name.EndsWith("/", StringComparison.Ordinal) ? name[..^1] : name;
        if (normalized.Length == 0 ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("Unsafe path detected in plugin package.");

        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixFileType == 0xA000)
            throw new InvalidDataException("Symbolic links are not allowed in plugin packages.");
    }
}
