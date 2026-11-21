using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SoftBox.PhoneLab.Core.Plugins;

public sealed record PluginPackageSignature(string KeyId, string SignatureBase64, string Algorithm);

/// <summary>Deterministic signature payload for every uncompressed ZIP file except the detached signature.</summary>
public static class PluginSignaturePayload
{
    public const string SignatureEntryName = "softbox-plugin.signature.json";
    public const string AlgorithmName = "RSA-SHA256-PSS";

    public static async Task<byte[]> ComputeAsync(
        IEnumerable<ZipArchiveEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var selected = entries
            .Where(entry => !entry.FullName.Equals(SignatureEntryName, StringComparison.Ordinal))
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
            .ToArray();
        if (selected.Length is < 1 or > 1000)
            throw new InvalidDataException("عدد ملفات الإضافة المراد توقيعها غير مدعوم.");

        var canonical = new StringBuilder("SOFTBOX-PLUGIN-PACKAGE-V1\n");
        foreach (var entry in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var input = entry.Open();
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > entry.Length)
                    throw new InvalidDataException("بيانات الإضافة تجاوزت الحجم المعلن.");
                hash.AppendData(buffer, 0, read);
            }
            if (total != entry.Length)
                throw new InvalidDataException("حجم ملف الإضافة لا يطابق بيانات ZIP: " + entry.FullName);

            var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            canonical.Append(entry.FullName).Append('\0')
                .Append(entry.Length).Append('\0')
                .Append(digest).Append('\n');
        }
        return Encoding.UTF8.GetBytes(canonical.ToString());
    }

    public static byte[] Sign(byte[] payload, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        if (rsa.KeySize is < 2048 or > 4096)
            throw new CryptographicException("مفتاح توقيع الإضافة يجب أن يكون RSA بطول 2048 إلى 4096 بت.");
        return rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    public static bool Verify(byte[] payload, byte[] signature, string publicKeyPem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.KeySize is >= 2048 and <= 4096 &&
                   rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (CryptographicException) { return false; }
    }
}
