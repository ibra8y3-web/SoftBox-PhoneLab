using System.IO.Compression;
using SoftBox.PhoneLab.Core.Backup;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class EncryptedBackupServiceTests
{
    private readonly BackupArchiveService _archives = new();
    private readonly EncryptedBackupService _encrypted = new();

    [Fact]
    public async Task EncryptDecryptAndVerifyRoundTripsBackupArchive()
    {
        var root = CreateTempDirectory();
        var source = Path.Combine(root, "source");
        var zip = Path.Combine(root, "backup.zip");
        var encrypted = Path.Combine(root, "backup.sbx");
        var restored = Path.Combine(root, "restored.zip");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "private-note.txt"), "secret customer-owned data");

        try
        {
            await _archives.CreateArchiveAsync(source, zip, "••••1234", "/sdcard/Documents");
            await _encrypted.EncryptAsync(zip, encrypted, "correct horse battery staple");
            Assert.True(File.Exists(encrypted));
            var encryptedBytes = await File.ReadAllBytesAsync(encrypted);
            Assert.True(encryptedBytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("secret customer-owned data")) < 0);

            await _encrypted.DecryptAsync(encrypted, restored, "correct horse battery staple");
            var verification = await _archives.VerifyArchiveAsync(restored);
            Assert.True(verification.IsValid);
            Assert.Equal(1, verification.VerifiedFiles);
            var encryptedVerification = await _encrypted.VerifyEncryptedBackupAsync(encrypted, "correct horse battery staple");
            Assert.True(encryptedVerification.IsValid);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WrongPasswordIsRejectedBeforePlaintextOutputIsCreated()
    {
        var root = CreateTempDirectory();
        var source = Path.Combine(root, "source");
        var zip = Path.Combine(root, "backup.zip");
        var encrypted = Path.Combine(root, "backup.sbx");
        var output = Path.Combine(root, "wrong-password-output.zip");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "data.txt"), "protected data");

        try
        {
            await _archives.CreateArchiveAsync(source, zip, "••••1234", "/sdcard");
            await _encrypted.EncryptAsync(zip, encrypted, "a strong test password 2026");
            await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() =>
                _encrypted.DecryptAsync(encrypted, output, "a different password 2026"));
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TamperedEncryptedContainerFailsAuthentication()
    {
        var root = CreateTempDirectory();
        var source = Path.Combine(root, "source");
        var zip = Path.Combine(root, "backup.zip");
        var encrypted = Path.Combine(root, "backup.sbx");
        var output = Path.Combine(root, "output.zip");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "data.txt"), "authentic data");

        try
        {
            await _archives.CreateArchiveAsync(source, zip, "••••1234", "/sdcard");
            await _encrypted.EncryptAsync(zip, encrypted, "a long enough password");
            var bytes = await File.ReadAllBytesAsync(encrypted);
            bytes[bytes.Length / 2] ^= 0x40;
            await File.WriteAllBytesAsync(encrypted, bytes);

            await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() =>
                _encrypted.DecryptAsync(encrypted, output, "a long enough password"));
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("line one\nline two")]
    public async Task EncryptRejectsWeakOrInvalidPasswords(string password)
    {
        var root = CreateTempDirectory();
        var source = Path.Combine(root, "archive.zip");
        await File.WriteAllBytesAsync(source, [0x01, 0x02, 0x03]);
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _encrypted.EncryptAsync(source, Path.Combine(root, "archive.sbx"), password));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-encrypted-backup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
