using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SoftBox.PhoneLab.Core.Plugins;

/// <summary>
/// Signs a complete plugin archive using a publisher-owned private RSA key.
/// The private key is supplied by the publisher for this operation and never placed in the package.
/// </summary>
public static class PluginPackageSigner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> SignPackageAsync(
        string packagePath,
        string privateKeyPem,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            throw new FileNotFoundException("Plugin package does not exist.", packagePath);
        if (string.IsNullOrWhiteSpace(privateKeyPem) || privateKeyPem.Length > 32 * 1024)
            throw new ArgumentException("RSA private key PEM is empty or too large.", nameof(privateKeyPem));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        if (rsa.KeySize is < 2048 or > 4096)
            throw new CryptographicException("Plugin signing key must be RSA 2048 to 4096 bits.");

        var keyId = PluginPublisherTrustStore.GetKeyId(rsa.ExportSubjectPublicKeyInfoPem());
        var fullPath = Path.GetFullPath(packagePath);
        var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            byte[] payload;
            using (var archive = ZipFile.OpenRead(fullPath))
            {
                ValidateEntries(archive.Entries);
                payload = await PluginSignaturePayload.ComputeAsync(archive.Entries, cancellationToken);
            }

            var signature = new PluginPackageSignature(
                keyId,
                Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)),
                PluginSignaturePayload.AlgorithmName);

            using (var source = ZipFile.OpenRead(fullPath))
            using (var output = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                foreach (var entry in source.Entries.Where(item =>
                             !item.FullName.Equals(PluginSignaturePayload.SignatureEntryName, StringComparison.Ordinal)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var copy = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    copy.LastWriteTime = entry.LastWriteTime;
                    copy.ExternalAttributes = entry.ExternalAttributes;
                    await using var input = entry.Open();
                    await using var destination = copy.Open();
                    await input.CopyToAsync(destination, cancellationToken);
                }

                var signatureEntry = output.CreateEntry(PluginSignaturePayload.SignatureEntryName, CompressionLevel.Optimal);
                await using var signatureStream = signatureEntry.Open();
                await JsonSerializer.SerializeAsync(signatureStream, signature, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, fullPath, overwrite: true);
            return keyId;
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private static void ValidateEntries(IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        if (entries.Count is < 1 or > 1000)
            throw new InvalidDataException("Plugin archive must contain 1 to 1000 entries.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var raw = entry.FullName;
            var normalized = raw.EndsWith("/", StringComparison.Ordinal) ? raw[..^1] : raw;
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 512 ||
                normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains('\\') ||
                normalized.Contains(':') || normalized.Any(char.IsControl) ||
                normalized.Split('/').Any(segment => segment is "" or "." or ".."))
                throw new InvalidDataException("Unsafe ZIP path cannot be signed: " + raw);
            if (!names.Add(raw))
                throw new InvalidDataException("Duplicate ZIP path cannot be signed: " + raw);
        }
    }
}
