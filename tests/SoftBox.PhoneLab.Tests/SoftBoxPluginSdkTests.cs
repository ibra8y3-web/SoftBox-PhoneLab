using System.Text.Json;
using SoftBox.PluginSdk;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SoftBoxPluginSdkTests
{
    [Fact]
    public void NativeManifestSerializesUsingVersionedPublicContract()
    {
        var manifest = new SoftBoxPluginManifestV1(1, "org.example.audit", "Audit sample",
            "1.0.0", "1.0.0", "0.1.0", "Audit.Plugin.dll", "Example Publisher",
            "Read-only sample.", Array.Empty<string>());

        var json = SoftBoxPluginManifestWriter.Serialize(manifest);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("manifestVersion").GetInt32());
        Assert.Equal("native-assembly", document.RootElement.GetProperty("executionMode").GetString());
        Assert.Empty(document.RootElement.GetProperty("requestedPermissions").EnumerateArray());
        Assert.Equal("1.0.0", SoftBoxPluginSdkInfo.ApiVersion);
    }

    [Fact]
    public void NativeManifestCannotRequestHostPermissionsOrPathTraversal()
    {
        var withPermission = new SoftBoxPluginManifestV1(1, "org.example.audit", "Audit sample",
            "1.0.0", "1.0.0", "0.1.0", "Audit.Plugin.dll", "Example Publisher",
            "Read-only sample.", [SoftBoxPluginPermissions.DeviceRead]);
        var traversal = withPermission with
        {
            EntryAssembly = "../Audit.Plugin.dll",
            RequestedPermissions = Array.Empty<string>()
        };

        Assert.Contains(SoftBoxPluginManifestWriter.Validate(withPermission),
            error => error.Contains("empty permission list", StringComparison.Ordinal));
        Assert.Contains(SoftBoxPluginManifestWriter.Validate(traversal),
            error => error.Contains("root-level DLL", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadOnlyWorkflowAllowsOnlyReadPermissions()
    {
        var workflow = new SoftBoxPluginManifestV1(1, "org.example.workflow", "Workflow",
            "1.0.0", "1.0.0", "0.1.0", "workflow.json", "Example Publisher",
            "Read-only workflow.", [SoftBoxPluginPermissions.DeviceRead], "read-only-workflow");
        Assert.Empty(SoftBoxPluginManifestWriter.Validate(workflow));
    }
}
