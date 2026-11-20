using System.IO;
using System.Security.Cryptography;

namespace SoftBox.PhoneLab.Core.Backup;

/// <summary>
/// Password-protected streaming backup container.
/// Format: magic(8) + salt(16) + IV(16) + AES-256-CBC ciphertext + HMAC-SHA256(32).
/// PBKDF2-HMAC-SHA256 derives independent encryption/authentication keys.
/// Authentication is verified before any plaintext is written during decryption.
/// </summary>
public sealed class EncryptedBackupService
{
    private static readonly byte[] Magic = "SBXENC01"u8.ToArray();
    private const int SaltBytes = 16;
    private const int IvBytes = 16;
    private const int HeaderBytes = 8 + SaltBytes + IvBytes;
    private const int TagBytes = 32;
    private const int DerivedKeyBytes = 64;
    private const int Pbkdf2Iterations = 600_000;
    private const int BufferBytes = 128 * 1024;
    private const long MaximumContainerBytes = 24L * 1024 * 1024 * 1024;

    public async Task EncryptAsync(
        string sourceZipPath,
        string encryptedPath,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        if (string.IsNullOrWhiteSpace(sourceZipPath) || !File.Exists(sourceZipPath))
            throw new FileNotFoundException("ملف ZIP الأصلي غير موجود.", sourceZipPath);
        if (string.IsNullOrWhiteSpace(encryptedPath))
            throw new ArgumentException("حدد مسار حفظ النسخة المشفرة.", nameof(encryptedPath));

        var source = Path.GetFullPath(sourceZipPath);
        var target = Path.GetFullPath(encryptedPath);
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("لا يمكن استخدام نفس مسار ملف ZIP كمصدر ووجهة.");

        var sourceInfo = new FileInfo(source);
        if (sourceInfo.Length <= 0 || sourceInfo.Length > MaximumContainerBytes)
            throw new InvalidDataException("حجم ملف النسخة خارج الحدود الآمنة.");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var iv = RandomNumberGenerator.GetBytes(IvBytes);
        var keys = DeriveKeys(password, salt);
        var header = new byte[HeaderBytes];
        Magic.CopyTo(header, 0);
        salt.CopyTo(header, Magic.Length);
        iv.CopyTo(header, Magic.Length + SaltBytes);

        try
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = keys.AsSpan(0, 32).ToArray();
            aes.IV = iv;

            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await output.WriteAsync(header, cancellationToken);
                using (var encryptor = aes.CreateEncryptor())
                await using (var crypto = new CryptoStream(output, encryptor, CryptoStreamMode.Write, leaveOpen: true))
                {
                    await input.CopyToAsync(crypto, BufferBytes, cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
            }

            var tag = await ComputeMacForWholeFileAsync(temporary, keys.AsMemory(32, 32), cancellationToken);
            await using (var append = new FileStream(temporary, FileMode.Append, FileAccess.Write, FileShare.None,
                BufferBytes, FileOptions.Asynchronous))
            {
                await append.WriteAsync(tag, cancellationToken);
                await append.FlushAsync(cancellationToken);
            }

            var encryptedInfo = new FileInfo(temporary);
            if (encryptedInfo.Length > MaximumContainerBytes)
                throw new InvalidDataException("حجم النسخة المشفرة تجاوز الحد الآمن.");
            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keys);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(iv);
        }
    }

    public async Task DecryptAsync(
        string encryptedPath,
        string destinationZipPath,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        if (string.IsNullOrWhiteSpace(encryptedPath) || !File.Exists(encryptedPath))
            throw new FileNotFoundException("النسخة المشفرة غير موجودة.", encryptedPath);
        if (string.IsNullOrWhiteSpace(destinationZipPath))
            throw new ArgumentException("حدد مسار استخراج النسخة.", nameof(destinationZipPath));

        var source = Path.GetFullPath(encryptedPath);
        var target = Path.GetFullPath(destinationZipPath);
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("مسار الاستخراج لا يمكن أن يساوي مسار النسخة المشفرة.");

        var info = new FileInfo(source);
        if (info.Length is < (HeaderBytes + 16 + TagBytes) or > MaximumContainerBytes)
            throw new InvalidDataException("حجم النسخة المشفرة أو بنيتها غير صالح.");

        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        byte[] keys = [];
        try
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var header = new byte[HeaderBytes];
            await ReadExactlyAsync(input, header, cancellationToken);
            if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                throw new InvalidDataException("صيغة ملف SBX المشفر غير معروفة.");

            var salt = header.AsSpan(Magic.Length, SaltBytes).ToArray();
            var iv = header.AsSpan(Magic.Length + SaltBytes, IvBytes).ToArray();
            keys = DeriveKeys(password, salt);

            var cipherLength = input.Length - HeaderBytes - TagBytes;
            if (cipherLength < 16 || cipherLength % 16 != 0)
                throw new InvalidDataException("طول بيانات النسخة المشفرة غير صالح.");

            var computedTag = await ComputeMacPrefixAsync(input, input.Length - TagBytes, keys.AsMemory(32, 32), cancellationToken);
            input.Position = input.Length - TagBytes;
            var suppliedTag = new byte[TagBytes];
            await ReadExactlyAsync(input, suppliedTag, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(computedTag, suppliedTag))
                throw new CryptographicException("فشل التحقق من مصادقة النسخة. كلمة المرور خاطئة أو الملف تعرض للتعديل.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            input.Position = HeaderBytes;
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = keys.AsSpan(0, 32).ToArray();
            aes.IV = iv;

            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var limited = new BoundedReadStream(input, cipherLength))
            using (var decryptor = aes.CreateDecryptor())
            await using (var crypto = new CryptoStream(limited, decryptor, CryptoStreamMode.Read, leaveOpen: true))
            {
                await crypto.CopyToAsync(output, BufferBytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            var outputInfo = new FileInfo(temporary);
            if (outputInfo.Length <= 0 || outputInfo.Length > MaximumContainerBytes)
                throw new InvalidDataException("حجم ملف ZIP المستخرج خارج الحدود الآمنة.");
            File.Move(temporary, target, overwrite: true);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(iv);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
        finally
        {
            if (keys.Length > 0)
                CryptographicOperations.ZeroMemory(keys);
        }
    }

    public async Task<BackupVerificationResult> VerifyEncryptedBackupAsync(
        string encryptedPath,
        string password,
        CancellationToken cancellationToken = default)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "SoftBox-Verify-SBX-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryZip = Path.Combine(temporaryDirectory, "verified-backup.zip");
        try
        {
            await DecryptAsync(encryptedPath, temporaryZip, password, cancellationToken);
            return await new BackupArchiveService().VerifyArchiveAsync(temporaryZip, cancellationToken);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch { }
        }
    }

    public static void ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || password.Length > 1024 ||
            password.Any(char.IsControl))
            throw new ArgumentException("كلمة مرور النسخ المشفرة مطلوبة، ويجب أن تكون بين 12 و1024 حرفًا دون محارف تحكم.", nameof(password));
    }

    private static byte[] DeriveKeys(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            DerivedKeyBytes);

    private static async Task<byte[]> ComputeMacForWholeFileAsync(
        string path,
        ReadOnlyMemory<byte> macKey,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ComputeMacPrefixAsync(stream, stream.Length, macKey, cancellationToken);
    }

    private static async Task<byte[]> ComputeMacPrefixAsync(
        FileStream stream,
        long bytesToRead,
        ReadOnlyMemory<byte> macKey,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, macKey.ToArray());
        var buffer = new byte[BufferBytes];
        long remaining = bytesToRead;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wanted = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("انتهت النسخة قبل اكتمال التحقق من المصادقة.");
            mac.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return mac.GetHashAndReset();
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var read = await stream.ReadAsync(destination[offset..], cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("ملف النسخة المشفرة مبتور.");
            offset += read;
        }
    }

    private sealed class BoundedReadStream(Stream inner, long length) : Stream
    {
        private long _remaining = length;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => length - _remaining; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            var read = inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0) return 0;
            var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken);
            _remaining -= read;
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { /* The caller owns the underlying input stream. */ }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
