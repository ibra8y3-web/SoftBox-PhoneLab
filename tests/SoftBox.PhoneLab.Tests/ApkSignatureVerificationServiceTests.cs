using System.Security.Cryptography;
using System.Text;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class ApkSignatureVerificationServiceTests
{
    private const string ValidOutput = """
        Verifies
        Verified using v1 scheme (JAR signing): true
        Verified using v2 scheme (APK Signature Scheme v2): true
        Verified using v3 scheme (APK Signature Scheme v3): false
        Number of signers: 1
        Signer #1 certificate DN: CN=SoftBox Test, O=SoftBox
        Signer #1 certificate SHA-256 digest: aa:bb:cc:dd
        Signer #1 certificate SHA-1 digest: 11:22:33:44
        Signer #1 key algorithm: RSA
        Signer #1 key size (bits): 2048
        """;

    [Fact]
    public async Task VerifyAsyncParsesVerifiedSchemesAndSignerCertificate()
    {
        var path = CreateApk();
        try
        {
            var service = new ApkSignatureVerificationService(
                new FakeLocator(),
                new FakeRunner(new ApkSignerCommandResult(0, ValidOutput, "")));

            var result = await service.VerifyAsync(path);

            Assert.Equal(ApkSignatureVerificationStatus.Verified, result.Status);
            Assert.True(result.CryptographicVerificationPassed);
            Assert.Equal("34.0.0", result.BuildToolsVersion);
            Assert.True(result.VerifiedSchemes["v1 scheme (JAR signing)"]);
            Assert.True(result.VerifiedSchemes["v2 scheme (APK Signature Scheme v2)"]);
            Assert.False(result.VerifiedSchemes["v3 scheme (APK Signature Scheme v3)"]);
            var signer = Assert.Single(result.Certificates);
            Assert.Equal("CN=SoftBox Test, O=SoftBox", signer.DistinguishedName);
            Assert.Equal("aabbccdd", signer.Sha256Digest);
            Assert.Equal("11223344", signer.Sha1Digest);
            Assert.Equal("RSA", signer.PublicKeyAlgorithm);
            Assert.Equal(2048, signer.PublicKeySizeBits);
            Assert.Contains(result.Warnings, item => item.Contains("لا يثبت أن الشهادة تخص الناشر الرسمي", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task VerifyAsyncReturnsInvalidSignatureWhenApksignerRejectsTheFile()
    {
        var path = CreateApk();
        try
        {
            var service = new ApkSignatureVerificationService(
                new FakeLocator(),
                new FakeRunner(new ApkSignerCommandResult(1, "ERROR: APK Signature Scheme v2 signer #1: APK integrity check failed", "")));

            var result = await service.VerifyAsync(path);

            Assert.Equal(ApkSignatureVerificationStatus.InvalidSignature, result.Status);
            Assert.False(result.CryptographicVerificationPassed);
            Assert.Contains(result.Warnings, item => item.Contains("رفض apksigner", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task VerifyAsyncReportsMissingAndroidSdkBuildToolsWithoutClaimingSuccess()
    {
        var path = CreateApk();
        try
        {
            var service = new ApkSignatureVerificationService(
                new FakeLocator(available: false),
                new FakeRunner(new ApkSignerCommandResult(0, ValidOutput, "")));

            var result = await service.VerifyAsync(path);

            Assert.Equal(ApkSignatureVerificationStatus.ToolUnavailable, result.Status);
            Assert.False(result.CryptographicVerificationPassed);
            Assert.Contains(result.Warnings, item => item.Contains("Build-Tools", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task VerifyAsyncRejectsTheResultIfApkChangesDuringVerification()
    {
        var path = CreateApk();
        try
        {
            var runner = new FakeRunner(
                new ApkSignerCommandResult(0, ValidOutput, ""),
                async () => await File.WriteAllBytesAsync(path, [0x50, 0x4B, 0x03, 0x04, 0x11, 0x22]));
            var service = new ApkSignatureVerificationService(new FakeLocator(), runner);

            var result = await service.VerifyAsync(path);

            Assert.Equal(ApkSignatureVerificationStatus.FileChangedDuringVerification, result.Status);
            Assert.False(result.CryptographicVerificationPassed);
            Assert.Contains(result.Warnings, item => item.Contains("تغير محتوى APK", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task VerifyAsyncPassesArgumentsWithoutShellConcatenation()
    {
        var path = CreateApk();
        try
        {
            var runner = new FakeRunner(new ApkSignerCommandResult(0, ValidOutput, ""));
            var service = new ApkSignatureVerificationService(new FakeLocator(), runner);

            await service.VerifyAsync(path);

            Assert.Equal("java.exe", runner.JavaExecutable);
            Assert.Equal("C:\\Android\\Sdk\\build-tools\\34.0.0\\lib\\apksigner.jar", runner.JarPath);
            Assert.Equal(new[] { "verify", "--verbose", "--print-certs", Path.GetFullPath(path) }, runner.Arguments);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task VerifyAsyncRejectsNonApkInput()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-" + Guid.NewGuid().ToString("N") + ".aab");
        await File.WriteAllTextAsync(path, "bundle");
        try
        {
            var service = new ApkSignatureVerificationService(new FakeLocator(), new FakeRunner(new(0, ValidOutput, "")));
            await Assert.ThrowsAsync<ArgumentException>(() => service.VerifyAsync(path));
        }
        finally { File.Delete(path); }
    }

    private static string CreateApk()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-signature-" + Guid.NewGuid().ToString("N") + ".apk");
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04, 0x21, 0x22, 0x23]);
        return path;
    }

    private sealed class FakeLocator(bool available = true) : IApkSignerToolLocator
    {
        public ApkSignerTool? Locate() => available
            ? new ApkSignerTool("java.exe", @"C:\Android\Sdk\build-tools\34.0.0\lib\apksigner.jar", "34.0.0")
            : null;
    }

    private sealed class FakeRunner(
        ApkSignerCommandResult result,
        Func<Task>? beforeReturn = null) : IApkSignerCommandRunner
    {
        public string? JavaExecutable { get; private set; }
        public string? JarPath { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public async Task<ApkSignerCommandResult> RunAsync(
            string javaExecutable,
            string apksignerJarPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            JavaExecutable = javaExecutable;
            JarPath = apksignerJarPath;
            Arguments = arguments.ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            if (beforeReturn is not null)
                await beforeReturn();
            return result;
        }
    }
}
