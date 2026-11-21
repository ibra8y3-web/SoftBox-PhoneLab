using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Plugins;
using SoftBox.PhoneLab.Core.Updates;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SignedUpdatePackageServiceTests
{
    [Fact]
    public async Task VerifiesOfficialSignedUpdateAndStagesOnlyListedFiles()
    {
        var root = CreateTempDirectory();
        var package = Path.Combine(root, "update.zip");
        try
        {
            using var rsa = RSA.Create(2048);
            await CreateSignedPackageAsync(package, rsa, "1.2.3");
            var trust = new PluginPublisherTrustStore(Path.Combine(root, "trust"));
            await trust.TrustPublicKeyAsync("SoftBox Release Publisher", rsa.ExportSubjectPublicKeyInfoPem());
            var bytes = await File.ReadAllBytesAsync(package);
            using var client = new HttpClient(new BytesHandler(bytes));
            var service = new SignedUpdatePackageService(client, trust, Path.Combine(root, "updates"));
            var asset = new ReleaseAssetInfo("SoftBox-Windows-x64.zip", bytes.Length,
                "https://github.com/ibra8y3-web/SoftBox-PhoneLab/releases/download/v1.2.3/SoftBox-Windows-x64.zip");

            var staged = await service.DownloadAndStageAsync(asset, "v1.2.3");

            Assert.Equal("1.2.3", staged.Version);
            Assert.True(File.Exists(Path.Combine(staged.StageDirectory, "SoftBox.PhoneLab.Desktop.exe")));
            Assert.Equal("signed-update-payload", await File.ReadAllTextAsync(Path.Combine(staged.StageDirectory, "SoftBox.PhoneLab.Desktop.exe")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsUnsignedOrUntrustedUpdateBeforeExtractingAnything()
    {
        var root = CreateTempDirectory();
        var package = Path.Combine(root, "update.zip");
        try
        {
            using var rsa = RSA.Create(2048);
            await CreateSignedPackageAsync(package, rsa, "1.2.3");
            var bytes = await File.ReadAllBytesAsync(package);
            using var client = new HttpClient(new BytesHandler(bytes));
            var service = new SignedUpdatePackageService(client, new PluginPublisherTrustStore(Path.Combine(root, "trust")),
                Path.Combine(root, "updates"));
            var asset = new ReleaseAssetInfo("SoftBox-Windows-x64.zip", bytes.Length,
                "https://github.com/ibra8y3-web/SoftBox-PhoneLab/releases/download/v1.2.3/SoftBox-Windows-x64.zip");

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DownloadAndStageAsync(asset, "1.2.3"));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "updates", "Staged"), "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsUntrustedAssetUrlAndInvalidPackagePaths()
    {
        var root = CreateTempDirectory();
        try
        {
            var service = new SignedUpdatePackageService(rootDirectory: Path.Combine(root, "updates"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DownloadAndStageAsync(
                new ReleaseAssetInfo("update.zip", 100, "https://evil.example/update.zip"), "1.0.0"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InstallAndRollbackScriptsWaitForAppExitAndPreserveRollbackSnapshot()
    {
        var root = CreateTempDirectory();
        try
        {
            var install = Path.Combine(root, "install");
            var stage = Path.Combine(root, "updates", "Staged", "1.2.3");
            Directory.CreateDirectory(install);
            Directory.CreateDirectory(stage);
            var exe = Path.Combine(install, "SoftBox.exe");
            File.WriteAllText(exe, "old");
            var service = new SignedUpdatePackageService(rootDirectory: Path.Combine(root, "updates"));
            var info = new SignedUpdateStageInfo("1.2.3", stage, "SoftBox.exe", "SoftBox Release Publisher",
                [new("SoftBox.exe", 3, new string('a', 64))], DateTimeOffset.UtcNow);

            var script = service.BuildInstallScript(info, install, exe, 1234, "1.2.2");

            Assert.Contains("Wait-Process -Id", script);
            Assert.Contains("Get-FileHash", script);
            Assert.Contains("rollbackRoot", script);
            Assert.Contains("Start-Process", script);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task CreateSignedPackageAsync(string path, RSA rsa, string version)
    {
        var exeBytes = Encoding.UTF8.GetBytes("signed-update-payload");
        var manifest = new SignedUpdateManifest(1, version, "1.0.0", "SoftBox Release Publisher",
            "SoftBox.PhoneLab.Desktop.exe",
            [new("SoftBox.PhoneLab.Desktop.exe", exeBytes.Length,
                Convert.ToHexString(SHA256.HashData(exeBytes)).ToLowerInvariant())],
            "Signed update test");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry(SignedUpdatePackageService.ManifestEntryName);
            await using (var stream = manifestEntry.Open())
                await JsonSerializer.SerializeAsync(stream, manifest);
            var exe = archive.CreateEntry("SoftBox.PhoneLab.Desktop.exe");
            await using var output = exe.Open();
            await output.WriteAsync(exeBytes);
        }

        // The detached package signature uses the same deterministic archive payload and publisher RSA PSS algorithm.
        using var source = ZipFile.OpenRead(path);
        var payload = await PluginSignaturePayload.ComputeAsync(source.Entries);
        var keyId = PluginPublisherTrustStore.GetKeyId(rsa.ExportSubjectPublicKeyInfoPem());
        var signature = new PluginPackageSignature(keyId,
            Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)),
            PluginSignaturePayload.AlgorithmName);
        var temp = path + ".signed";
        using (var input = ZipFile.OpenRead(path))
        using (var signed = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var item in input.Entries)
            {
                var copy = signed.CreateEntry(item.FullName);
                await using var src = item.Open();
                await using var dest = copy.Open();
                await src.CopyToAsync(dest);
            }
            var signatureEntry = signed.CreateEntry(SignedUpdatePackageService.SignatureEntryName);
            await using var signatureStream = signatureEntry.Open();
            await JsonSerializer.SerializeAsync(signatureStream, signature);
        }
        File.Move(temp, path, overwrite: true);
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "softbox-signed-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
    }
}
