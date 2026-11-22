using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Plugins;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class PluginPackageInspectorTests
{
    private readonly PluginPackageInspector _inspector = new();

    [Fact]
    public async Task ValidManifestPackageIsInspectedButNeverExecuted()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "sample.sbxplugin");
        try
        {
            await CreatePackageAsync(path, new PluginManifest(
                1,
                "org.softbox.sample",
                "Sample Diagnostics",
                "1.0.0",
                "1.0.0",
                "1.0.0",
                "Sample.Plugin.dll",
                "Example Publisher",
                "Read-only sample extension package.",
                []));

            var result = await _inspector.InspectAsync(path);

            Assert.True(result.ManifestValidation.IsValid);
            Assert.Empty(result.PackageErrors);
            Assert.NotNull(result.Manifest);
            Assert.Matches("^[0-9a-f]{64}$", result.PackageSha256);
            Assert.False(result.PublisherSignatureVerified);
            Assert.False(result.CodeLoadedOrExecuted);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task UnknownAndDangerousPermissionsFailClosed()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "bad-permissions.zip");
        try
        {
            await CreatePackageAsync(path, ValidManifest() with
            {
                RequestedPermissions = ["device.read", "shell.execute", "firmware.flash"]
            });

            var result = await _inspector.InspectAsync(path);

            Assert.False(result.ManifestValidation.IsValid);
            Assert.Contains(result.ManifestValidation.Errors, error => error.Contains("Unknown or forbidden permission"));
            Assert.False(result.CodeLoadedOrExecuted);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("/absolute/evil.dll")]
    [InlineData("folder\\evil.dll")]
    [InlineData("folder/../evil.dll")]
    public async Task UnsafeEntryAssemblyPathsAreRejected(string entryAssembly)
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "unsafe.zip");
        try
        {
            await CreatePackageAsync(path, ValidManifest() with { EntryAssembly = entryAssembly });
            var result = await _inspector.InspectAsync(path);
            Assert.False(result.ManifestValidation.IsValid);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PackageRejectsZipSlipEntries()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "zip-slip.zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var manifest = archive.CreateEntry(PluginPackageInspector.ManifestEntryName);
                await using (var stream = manifest.Open())
                    await JsonSerializer.SerializeAsync(stream, ValidManifest());

                var evil = archive.CreateEntry("../outside.txt");
                await using var writer = new StreamWriter(evil.Open());
                await writer.WriteAsync("not extracted");
            }

            var result = await _inspector.InspectAsync(path);
            Assert.Contains(result.PackageErrors, error => error.Contains("Unsafe path", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PackageRejectsMissingEntryAssembly()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "missing-entry.zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var manifest = archive.CreateEntry(PluginPackageInspector.ManifestEntryName);
                await using var stream = manifest.Open();
                await JsonSerializer.SerializeAsync(stream, ValidManifest() with { EntryAssembly = "DoesNotExist.dll" });
            }

            var result = await _inspector.InspectAsync(path);
            Assert.Contains(result.PackageErrors, error => error.Contains("Plugin entry point does not exist", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static PluginManifest ValidManifest() => new(
        1,
        "org.softbox.sample",
        "Sample",
        "1.0.0",
        "1.0.0",
        "1.0.0",
        "Sample.Plugin.dll",
        "Example Publisher",
        "Sample plugin package.",
        []);

    private static async Task CreatePackageAsync(string path, PluginManifest manifest)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry(PluginPackageInspector.ManifestEntryName);
        await using (var stream = manifestEntry.Open())
            await JsonSerializer.SerializeAsync(stream, manifest);
        var assembly = archive.CreateEntry(manifest.EntryAssembly);
        await using var output = assembly.Open();
        await output.WriteAsync(Encoding.UTF8.GetBytes("This is a placeholder byte sequence, not executable plugin code."));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-plugin-package-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
