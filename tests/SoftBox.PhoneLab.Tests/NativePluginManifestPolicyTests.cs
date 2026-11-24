using SoftBox.PhoneLab.Core.Plugins;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class NativePluginManifestPolicyTests
{
    private static PluginManifest NativeManifest()
        => new(1, "org.example.native", "Native Plugin", "1.0.0", "1.0.0", "0.1.0",
            "Native.Plugin.dll", "Example Publisher", "Native AppContainer plugin", []);

    [Fact]
    public void AcceptsNativeManifestWithNoSoftBoxPermissions()
    {
        var result = PluginManifestValidator.Validate(NativeManifest());
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void RejectsNativeManifestThatRequestsAnySoftBoxPermission()
    {
        var result = PluginManifestValidator.Validate(NativeManifest() with
        {
            RequestedPermissions = [PluginPermissionIds.DeviceRead, PluginPermissionIds.SharedStorageRead]
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("empty SoftBox permission list", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("folder/Native.Plugin.dll")]
    [InlineData("../Native.Plugin.dll")]
    [InlineData("C:/Native.Plugin.dll")]
    public void RejectsNativeEntryPathSegments(string entry)
    {
        var result = PluginManifestValidator.Validate(NativeManifest() with { EntryAssembly = entry });
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("C:Native.Plugin.dll")]
    [InlineData("native:payload.dll")]
    [InlineData("bad?.dll")]
    [InlineData("CON.dll")]
    [InlineData("COM1.dll")]
    public void RejectsWindowsAlternateStreamsAndReservedDllNames(string entry)
    {
        var result = PluginManifestValidator.Validate(NativeManifest() with { EntryAssembly = entry });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("root-level .dll", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadOnlyWorkflowKeepsItsSeparateLimitedPermissionContract()
    {
        var manifest = NativeManifest() with
        {
            EntryAssembly = "workflow.json",
            ExecutionMode = "read-only-workflow",
            RequestedPermissions = [PluginPermissionIds.DeviceRead, PluginPermissionIds.DeviceDiagnostics]
        };
        Assert.True(PluginManifestValidator.Validate(manifest).IsValid);
    }
}
