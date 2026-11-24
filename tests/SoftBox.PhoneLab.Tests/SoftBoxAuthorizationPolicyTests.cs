using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SoftBoxAuthorizationPolicyTests
{
    [Fact]
    public void EveryCatalogActionHasAnExplicitFailClosedPermission()
    {
        var actions = SoftBoxModuleCatalog.All.SelectMany(x => x.Features)
            .Where(x => x.ActionId is not null).Select(x => x.ActionId!).ToArray();

        Assert.Equal(actions.Length, actions.Distinct(StringComparer.Ordinal).Count());
        var navigationActions = SoftBoxAuthorizationPolicy.SupportedActionIds
            .Except(SoftBoxAuthorizationPolicy.InternalActionIds, StringComparer.Ordinal);
        Assert.Equal(navigationActions.OrderBy(x => x, StringComparer.Ordinal),
            actions.OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(SoftBoxAuthorizationPolicy.InternalActionIds,
            action => Assert.Contains(action, SoftBoxAuthorizationPolicy.SupportedActionIds));
        Assert.All(actions, action =>
        {
            Assert.True(SoftBoxAuthorizationPolicy.TryGetRequiredPermission(action, out var permission));
            Assert.NotEqual(SoftBoxAccountPermission.None, permission);
        });
    }

    [Fact]
    public void ReadOnlyAccountCannotPerformDeviceWritesOrWorkshopChanges()
    {
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "InspectDevice"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenHelpCenter"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenOfflineCenter"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenDashboardAnalytics"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenAndroidFileManager"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenWirelessAdbPairing"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "MirrorScreen"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenIPhoneCenter"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenIPhonePartsHistory"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenIPhoneBatteryHealth"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenMaintenanceModeCenter"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenIPhoneHdmiCapture"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenDriverPackageManager"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "ListUsbDriverPackages"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "InstallUsbDriverPackage"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "RemoveUsbDriverPackage"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenIPhoneFunctionalTests"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenAndroidCommandCenter"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenAndroidWorkflow"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenAndroidBatchTools"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenWorkshop"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenUserManagement"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "OpenPluginStore"));
    }

    [Fact]
    public void TechnicianCanRepairButCannotManageAccountsOrApplicationSecurity()
    {
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Technician, "OpenAndroidAppManager"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Technician, "OpenIPhoneFunctionalTests"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Technician, "OpenAirPlayReceiver"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Technician, "OpenWirelessAdbPairing"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Technician, "OpenPartsInventory"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Technician, "OpenUserManagement"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Technician, "OpenApplicationSettings"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Technician, "OpenUpdateCenter"));
    }

    [Fact]
    public void SamsungDownloadModeRebootIsInternalAuditedDeviceOperation()
    {
        const string actionId = "RebootSamsungDownloadMode";

        Assert.Contains(actionId, SoftBoxAuthorizationPolicy.InternalActionIds);
        Assert.DoesNotContain(
            SoftBoxModuleCatalog.All.SelectMany(module => module.Features)
                .Where(feature => feature.ActionId is not null)
                .Select(feature => feature.ActionId!),
            actionId);

        Assert.True(SoftBoxAuthorizationPolicy.TryGetRequiredPermission(actionId, out var permission));
        Assert.Equal(SoftBoxAccountPermission.DeviceOperations, permission);
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Administrator, actionId));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Technician, actionId));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, actionId));
    }

    [Fact]
    public void SamsungOdinStartRequiresDeviceOperationsAndIsExplicitlyAuthorized()
    {
        Assert.True(SoftBoxAuthorizationPolicy.TryGetRequiredPermission("ExecuteSamsungOdinFlash", out var permission));
        Assert.Equal(SoftBoxAccountPermission.DeviceOperations, permission);
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Administrator, "ExecuteSamsungOdinFlash"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.Technician, "ExecuteSamsungOdinFlash"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(SoftBoxUserRole.ReadOnly, "ExecuteSamsungOdinFlash"));
    }

    [Fact]
    public void UnknownActionIdsAreDeniedForEveryRole()
    {
        foreach (var role in Enum.GetValues<SoftBoxUserRole>())
            Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(role, "ExecuteArbitraryCommand"));
    }
}
