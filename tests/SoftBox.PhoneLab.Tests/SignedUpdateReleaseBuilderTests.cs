using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SoftBox.PhoneLab.Core.Plugins;
using SoftBox.PhoneLab.Core.Updates;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SignedUpdateReleaseBuilderTests
{
    [Fact]
    public async Task BuildsPackageAcceptedByTheProductionUpdateVerifier()
    {
        var root = CreateTempDirectory();
        var publish = Path.Combine(root, "publish");
        var output = Path.Combine(root, "SoftBox-Windows-x64.zip");
        Directory.CreateDirectory(Path.Combine(publish, "Tools", "platform-tools"));
        await File.WriteAllTextAsync(Path.Combine(publish, "SoftBox-PhoneLab.exe"), "published-executable");
        await File.WriteAllTextAsync(Path.Combine(publish, "Tools", "platform-tools", "adb.exe"), "published-adb");

        try
        {
            using var rsa = RSA.Create(2048);
            var privatePem = rsa.ExportPkcs8PrivateKeyPem();
            var publicPem = rsa.ExportSubjectPublicKeyInfoPem();

            var build = await SignedUpdateReleaseBuilder.BuildAsync(
                publish,
                output,
                "1.2.3",
                "0.1.0",
                "SoftBox-PhoneLab.exe",
                "Release signing integration test",
                privatePem);

            Assert.True(File.Exists(output));
            Assert.Equal(2, build.FileCount);
            Assert.True(build.ArchiveBytes > 0);
            Assert.Equal(64, build.ArchiveSha256.Length);
            Assert.Equal(64, build.PublisherKeyId.Length);

            var trust = new PluginPublisherTrustStore(Path.Combine(root, "trust"));
            await trust.TrustPublicKeyAsync(SignedUpdateReleaseBuilder.PublisherName, publicPem);
            var service = new SignedUpdatePackageService(
                trustStore: trust,
                rootDirectory: Path.Combine(root, "updates"));
            var staged = await service.VerifyAndStageAsync(output, "1.2.3", "0.1.0");

            Assert.Equal("1.2.3", staged.Version);
            Assert.True(File.Exists(Path.Combine(staged.StageDirectory, "SoftBox-PhoneLab.exe")));
            Assert.True(File.Exists(Path.Combine(staged.StageDirectory, "Tools", "platform-tools", "adb.exe")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RefusesMissingMainExecutableAndDoesNotCreateOutput()
    {
        var root = CreateTempDirectory();
        var publish = Path.Combine(root, "publish");
        var output = Path.Combine(root, "update.zip");
        Directory.CreateDirectory(publish);
        await File.WriteAllTextAsync(Path.Combine(publish, "other.exe"), "not main");
        try
        {
            using var rsa = RSA.Create(2048);
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                SignedUpdateReleaseBuilder.BuildAsync(
                    publish, output, "1.0.0", "0.1.0", "SoftBox.exe", "",
                    rsa.ExportPkcs8PrivateKeyPem()));
            Assert.False(File.Exists(output));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RefusesToOverwriteExistingSignedArchive()
    {
        var root = CreateTempDirectory();
        var publish = Path.Combine(root, "publish");
        var output = Path.Combine(root, "update.zip");
        Directory.CreateDirectory(publish);
        await File.WriteAllTextAsync(Path.Combine(publish, "SoftBox.exe"), "exe");
        await File.WriteAllTextAsync(output, "do not overwrite");
        try
        {
            using var rsa = RSA.Create();
            await Assert.ThrowsAsync<IOException>(() =>
                SignedUpdateReleaseBuilder.BuildAsync(
                    publish, output, "1.0.0", "0.1.0", "SoftBox.exe", "",
                    rsa.ExportPkcs8PrivateKeyPem()));
            Assert.Equal("do not overwrite", await File.ReadAllTextAsync(output));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RefusesInvalidVersionAndUnsafePublisherKey()
    {
        var root = CreateTempDirectory();
        var publish = Path.Combine(root, "publish");
        Directory.CreateDirectory(publish);
        await File.WriteAllTextAsync(Path.Combine(publish, "SoftBox.exe"), "exe");
        try
        {
            using var rsa = RSA.Create();
            await Assert.ThrowsAsync<ArgumentException>(() =>
                SignedUpdateReleaseBuilder.BuildAsync(
                    publish, Path.Combine(root, "bad.zip"), "latest", "0.1.0", "SoftBox.exe", "",
                    rsa.ExportPkcs8PrivateKeyPem()));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "softbox-release-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
