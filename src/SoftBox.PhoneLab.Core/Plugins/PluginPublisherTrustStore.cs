using System.Security.Cryptography;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Security;

namespace SoftBox.PhoneLab.Core.Plugins;

public sealed record TrustedPluginPublisher(
    string KeyId,
    string PublisherName,
    string PublicKeyPem,
    DateTimeOffset AddedAtUtc);

/// <summary>Current-user DPAPI protected RSA public-key trust roots for plugin publishers.</summary>
public sealed class PluginPublisherTrustStore
{
    private const int SchemaVersion = 1;
    private const int MaximumTrustedPublishers = 500;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private TrustData? _data;

    public PluginPublisherTrustStore(string? rootDirectory = null)
    {
        var root = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "PluginTrust"));
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "trusted-publishers.sbxtrust");
    }

    public static string GetKeyId(string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        if (rsa.KeySize is < 2048 or > 4096)
            throw new CryptographicException("مفتاح الناشر يجب أن يكون RSA بطول 2048 إلى 4096 بت.");
        return Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
    }

    public async Task<TrustedPluginPublisher> TrustPublicKeyAsync(
        string publisherName,
        string publicKeyPem,
        CancellationToken cancellationToken = default)
    {
        var name = (publisherName ?? "").Trim();
        if (name.Length is 0 or > 160 || name.Any(char.IsControl))
            throw new ArgumentException("اسم الناشر مطلوب وبحد أقصى 160 حرفًا.", nameof(publisherName));
        if (string.IsNullOrWhiteSpace(publicKeyPem) || publicKeyPem.Length > 32 * 1024)
            throw new ArgumentException("المفتاح العام فارغ أو أكبر من 32 KiB.", nameof(publicKeyPem));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        if (rsa.KeySize is < 2048 or > 4096)
            throw new CryptographicException("مفتاح الناشر يجب أن يكون RSA بطول 2048 إلى 4096 بت.");
        var pem = rsa.ExportSubjectPublicKeyInfoPem();
        var id = GetKeyId(pem);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var index = _data!.Publishers.FindIndex(item => item.KeyId == id);
            var publisher = new TrustedPluginPublisher(id, name, pem,
                index >= 0 ? _data.Publishers[index].AddedAtUtc : DateTimeOffset.UtcNow);
            if (index >= 0)
                _data.Publishers[index] = publisher;
            else
            {
                if (_data.Publishers.Count >= MaximumTrustedPublishers)
                    throw new InvalidOperationException("مخزن مفاتيح الإضافات ممتلئ.");
                _data.Publishers.Add(publisher);
            }
            await SaveLockedAsync(cancellationToken);
            return publisher;
        }
        finally { _gate.Release(); }
    }

    public async Task<TrustedPluginPublisher?> FindAsync(string keyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            return _data!.Publishers.FirstOrDefault(item =>
                item.KeyId.Equals(keyId, StringComparison.OrdinalIgnoreCase));
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<TrustedPluginPublisher>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            return _data!.Publishers.OrderBy(item => item.PublisherName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RemoveAsync(string keyId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedLockedAsync(cancellationToken);
            var removed = _data!.Publishers.RemoveAll(item =>
                item.KeyId.Equals(keyId, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
                await SaveLockedAsync(cancellationToken);
            return removed;
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureLoadedLockedAsync(CancellationToken cancellationToken)
    {
        if (_data is not null)
            return;
        if (!File.Exists(_path))
        {
            _data = new TrustData();
            return;
        }

        var info = new FileInfo(_path);
        if (info.Length is <= 0 or > 8 * 1024 * 1024)
            throw new InvalidDataException("ملف مفاتيح ناشري الإضافات غير صالح.");
        var protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
        var json = WindowsUserDataProtection.UnprotectOrLegacy(
            protectedBytes, "PluginPublisherTrustStore.v1", out var wasProtected);
        CryptographicOperations.ZeroMemory(protectedBytes);
        TrustData restored;
        try
        {
            restored = JsonSerializer.Deserialize<TrustData>(json, JsonOptions)
                ?? throw new InvalidDataException("مخزن مفاتيح الناشرين فارغ.");
        }
        finally { CryptographicOperations.ZeroMemory(json); }

        if (restored.SchemaVersion != SchemaVersion || restored.Publishers is null ||
            restored.Publishers.Count > MaximumTrustedPublishers)
            throw new InvalidDataException("إصدار مخزن مفاتيح الناشرين غير مدعوم.");

        foreach (var item in restored.Publishers)
        {
            if (item is null || item.KeyId.Length != 64 || !item.KeyId.All(Uri.IsHexDigit) ||
                !string.Equals(GetKeyId(item.PublicKeyPem), item.KeyId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("مفتاح ناشر لا يطابق بصمته المسجلة.");
        }

        _data = restored;
        if (!wasProtected)
        {
            try { await SaveLockedAsync(cancellationToken); }
            catch { _data = null; throw; }
        }
    }

    private async Task SaveLockedAsync(CancellationToken cancellationToken)
    {
        var temp = _path + ".tmp";
        byte[]? json = null;
        byte[]? protectedBytes = null;
        try
        {
            json = JsonSerializer.SerializeToUtf8Bytes(_data, JsonOptions);
            protectedBytes = WindowsUserDataProtection.Protect(json, "PluginPublisherTrustStore.v1");
            await File.WriteAllBytesAsync(temp, protectedBytes, cancellationToken);
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
        finally
        {
            if (json is not null) CryptographicOperations.ZeroMemory(json);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private sealed class TrustData
    {
        public int SchemaVersion { get; set; } = PluginPublisherTrustStore.SchemaVersion;
        public List<TrustedPluginPublisher> Publishers { get; set; } = [];
    }
}
