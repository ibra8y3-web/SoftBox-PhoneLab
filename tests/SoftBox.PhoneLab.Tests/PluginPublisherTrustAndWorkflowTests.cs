using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Plugins;
using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class PluginPublisherTrustAndWorkflowTests
{
    [Fact]
    public async Task TrustStorePersistsPublicKeyEncryptedAndSupportsRemoval()
    {
        var root = CreateTempDirectory();
        try
        {
            using var rsa = RSA.Create(2048);
            var store = new PluginPublisherTrustStore(root);
            var trusted = await store.TrustPublicKeyAsync("Workshop Publisher", rsa.ExportSubjectPublicKeyInfoPem());

            Assert.Matches("^[0-9a-f]{64}$", trusted.KeyId);
            var path = Path.Combine(root, "trusted-publishers.sbxtrust");
            Assert.True(WindowsUserDataProtection.IsProtected(await File.ReadAllBytesAsync(path)));

            var reopened = new PluginPublisherTrustStore(root);
            var found = await reopened.FindAsync(trusted.KeyId);
            Assert.NotNull(found);
            Assert.Equal("Workshop Publisher", found.PublisherName);
            Assert.True(await reopened.RemoveAsync(trusted.KeyId));
            Assert.Null(await reopened.FindAsync(trusted.KeyId));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SignerAndInspectorAuthenticateTheCompletePackage()
    {
        var root = CreateTempDirectory();
        var package = Path.Combine(root, "signed.sbxplugin");
        try
        {
            using var rsa = RSA.Create(2048);
            await CreateNativePackageAsync(package, "publisher-content");
            var keyId = await PluginPackageSigner.SignPackageAsync(package, rsa.ExportPkcs8PrivateKeyPem());
            var trust = new PluginPublisherTrustStore(Path.Combine(root, "trust"));
            var trusted = await trust.TrustPublicKeyAsync("Workshop Publisher", rsa.ExportSubjectPublicKeyInfoPem());
            Assert.Equal(keyId, trusted.KeyId);

            var result = await new PluginPackageInspector(trust).InspectAsync(package);

            Assert.True(result.SignaturePresent);
            Assert.True(result.PublisherSignatureVerified, string.Join("; ", result.PackageErrors));
            Assert.Equal("Workshop Publisher", result.TrustedPublisher);
            Assert.Empty(result.PackageErrors);
            Assert.False(result.CodeLoadedOrExecuted);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task TamperedPackageCannotPassDetachedSignatureVerification()
    {
        var root = CreateTempDirectory();
        var package = Path.Combine(root, "tampered.sbxplugin");
        try
        {
            using var rsa = RSA.Create(2048);
            await CreateNativePackageAsync(package, "original");
            await PluginPackageSigner.SignPackageAsync(package, rsa.ExportPkcs8PrivateKeyPem());
            var trust = new PluginPublisherTrustStore(Path.Combine(root, "trust"));
            await trust.TrustPublicKeyAsync("Workshop Publisher", rsa.ExportSubjectPublicKeyInfoPem());
            await RewriteEntryAsync(package, "sample.dll", "changed");

            var result = await new PluginPackageInspector(trust).InspectAsync(package);

            Assert.False(result.PublisherSignatureVerified);
            Assert.Contains(result.PackageErrors, message =>
                message.Contains("signature", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SignedReadOnlyWorkflowRunnerExecutesAllowListedPresetsInOrder()
    {
        var root = CreateTempDirectory();
        var package = Path.Combine(root, "workflow.sbxplugin");
        try
        {
            using var rsa = RSA.Create(2048);
            var manifest = ReadOnlyManifest();
            await CreateWorkflowPackageAsync(package, manifest,
                new { schemaVersion = 1, name = "Device snapshot", steps = new[] { new { presetId = "properties" }, new { presetId = "battery" } } });
            await PluginPackageSigner.SignPackageAsync(package, rsa.ExportPkcs8PrivateKeyPem());

            var trust = new PluginPublisherTrustStore(Path.Combine(root, "trust"));
            await trust.TrustPublicKeyAsync("Workshop Publisher", rsa.ExportSubjectPublicKeyInfoPem());
            var runner = new PluginWorkflowRunner(new PluginPackageInspector(trust));
            var calls = new List<string>();

            var report = await runner.RunAsync(package, manifest,
                new HashSet<string>(StringComparer.Ordinal) { PluginPermissionIds.DeviceRead },
                (preset, _) =>
                {
                    calls.Add(preset);
                    return Task.FromResult("captured:" + preset);
                });

            Assert.True(report.Success);
            Assert.Equal(new[] { "properties", "battery" }, calls);
            Assert.Equal(new[] { "captured:properties", "captured:battery" }, report.Steps.Select(step => step.Output));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task UnsignedWorkflowIsRejectedBeforeExecutingAnyStep()
    {
        var root = CreateTempDirectory();
        var package = Path.Combine(root, "unsigned.sbxplugin");
        try
        {
            var manifest = ReadOnlyManifest();
            await CreateWorkflowPackageAsync(package, manifest,
                new { schemaVersion = 1, name = "Device snapshot", steps = new[] { new { presetId = "properties" } } });
            var trust = new PluginPublisherTrustStore(Path.Combine(root, "trust"));
            var runner = new PluginWorkflowRunner(new PluginPackageInspector(trust));
            var called = false;

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => runner.RunAsync(
                package, manifest, new HashSet<string> { PluginPermissionIds.DeviceRead },
                (_, _) =>
                {
                    called = true;
                    return Task.FromResult("must not run");
                }));

            Assert.False(called);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task NativeDllPackageMayBeVerifiedButTheRuntimeNeverLoadsItsCode()
    {
        var root = CreateTempDirectory();
        var package = Path.Combine(root, "native.sbxplugin");
        try
        {
            using var rsa = RSA.Create(2048);
            await CreateNativePackageAsync(package, "not executed");
            await PluginPackageSigner.SignPackageAsync(package, rsa.ExportPkcs8PrivateKeyPem());
            var trust = new PluginPublisherTrustStore(Path.Combine(root, "trust"));
            await trust.TrustPublicKeyAsync("Workshop Publisher", rsa.ExportSubjectPublicKeyInfoPem());
            var inspected = await new PluginPackageInspector(trust).InspectAsync(package);

            Assert.True(inspected.PublisherSignatureVerified);
            Assert.False(inspected.CodeLoadedOrExecuted);
            Assert.Equal("native-assembly", inspected.Manifest!.ExecutionMode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static PluginManifest ReadOnlyManifest()
        => new(1, "org.softbox.readonly", "Read-only workflow", "1.0.0", "1.0.0", "1.0.0",
            "workflow.json", "Workshop Publisher", "Read-only Android diagnostics",
            [PluginPermissionIds.DeviceRead], "read-only-workflow");

    private static PluginManifest NativeManifest()
        => new(1, "org.softbox.sample", "Sample", "1.0.0", "1.0.0", "1.0.0",
            "sample.dll", "Workshop Publisher", "A signed test package", []);

    private static async Task CreateNativePackageAsync(string path, string content)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var manifest = archive.CreateEntry(PluginPackageInspector.ManifestEntryName);
        await using (var stream = manifest.Open())
            await JsonSerializer.SerializeAsync(stream, NativeManifest());
        var payload = archive.CreateEntry("sample.dll");
        await using var output = payload.Open();
        await output.WriteAsync(Encoding.UTF8.GetBytes(content));
    }

    private static async Task CreateWorkflowPackageAsync(string path, PluginManifest manifest, object workflow)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(PluginPackageInspector.ManifestEntryName);
        await using (var stream = entry.Open())
            await JsonSerializer.SerializeAsync(stream, manifest);
        var data = archive.CreateEntry("workflow.json");
        await using var workflowStream = data.Open();
        await JsonSerializer.SerializeAsync(workflowStream, workflow);
    }

    private static async Task RewriteEntryAsync(string path, string entryName, string newContents)
    {
        var temp = path + ".rewrite";
        using (var source = ZipFile.OpenRead(path))
        using (var output = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                var copy = output.CreateEntry(entry.FullName);
                await using var input = entry.Open();
                await using var stream = copy.Open();
                if (entry.FullName == entryName)
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(newContents));
                else
                    await input.CopyToAsync(stream);
            }
        }
        File.Move(temp, path, overwrite: true);
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "softbox-plugin-signing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
