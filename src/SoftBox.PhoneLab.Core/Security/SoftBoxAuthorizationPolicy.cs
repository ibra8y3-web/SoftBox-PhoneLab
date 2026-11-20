namespace SoftBox.PhoneLab.Core.Security;

/// <summary>
/// Fail-closed action policy for the desktop shell. Unknown action IDs are denied.
/// Module visibility does not grant permission to execute the tools inside a module.
/// </summary>
public static class SoftBoxAuthorizationPolicy
{
    private static readonly IReadOnlyDictionary<string, SoftBoxAccountPermission> Requirements =
        new Dictionary<string, SoftBoxAccountPermission>(StringComparer.Ordinal)
        {
            ["RefreshDevices"] = SoftBoxAccountPermission.DeviceRead,
            ["InspectDevice"] = SoftBoxAccountPermission.DeviceRead,
            ["UpdateDeviceCatalog"] = SoftBoxAccountPermission.ManageUpdates,
            ["OpenPhoneTests"] = SoftBoxAccountPermission.DeviceOperations,
            ["AndroidLogs"] = SoftBoxAccountPermission.DeviceRead,
            ["InstallSamsungDriver"] = SoftBoxAccountPermission.DeviceOperations,
            ["InspectFirmware"] = SoftBoxAccountPermission.ViewWorkspace,
            ["UsbTransport"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenWorkshop"] = SoftBoxAccountPermission.WorkshopOperations,
            ["MirrorScreen"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenScreenRecording"] = SoftBoxAccountPermission.DeviceOperations,
            ["AnalyzeScreen"] = SoftBoxAccountPermission.DeviceRead,
            ["MeasurementRig"] = SoftBoxAccountPermission.DeviceRead,
            ["RepairGuides"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenApkLab"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenNetworkDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenAndroidFileManager"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenAndroidAppManager"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenAndroidCommandCenter"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenAndroidBatchTools"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenAndroidWorkflow"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenFastbootManager"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenUsbDoctor"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenQualcommUsbDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenMediaTekUsbDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenFirmwareCenter"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenDeviceHistory"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenNotificationCenter"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenSupportBundle"] = SoftBoxAccountPermission.ExportData,
            ["OpenUpdateCenter"] = SoftBoxAccountPermission.ManageUpdates,
            ["OpenHelpCenter"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenApplicationSettings"] = SoftBoxAccountPermission.ManageSettings,
            ["OpenAbout"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenPartsInventory"] = SoftBoxAccountPermission.WorkshopOperations,
            ["OpenSamsungDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenXiaomiDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenBrandDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenPluginStore"] = SoftBoxAccountPermission.ManagePlugins,
            ["OpenRepairAdvisor"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenOneClickDiagnostic"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenPhoneBackup"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenWorkshopBackup"] = SoftBoxAccountPermission.WorkshopOperations,
            ["OpenGlobalWorkshopHistory"] = SoftBoxAccountPermission.WorkshopOperations,
            ["OpenGlobalWorkshopReports"] = SoftBoxAccountPermission.WorkshopOperations,
            ["OpenGlobalWorkshopSettings"] = SoftBoxAccountPermission.ManageSettings,
            ["ExportDeviceReport"] = SoftBoxAccountPermission.ExportData,
            ["ConnectionHelp"] = SoftBoxAccountPermission.ViewWorkspace,
            ["AgentHelp"] = SoftBoxAccountPermission.ViewWorkspace,
            ["UpdateAgent"] = SoftBoxAccountPermission.DeviceOperations,
            ["SetupUsb"] = SoftBoxAccountPermission.DeviceOperations,
            ["ImportBoardMap"] = SoftBoxAccountPermission.ManageSettings,
            ["OpenBoardView"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenUserManagement"] = SoftBoxAccountPermission.ManageUsers
        };

    public static SoftBoxAccountPermission PermissionsForRole(SoftBoxUserRole role) => role switch
    {
        SoftBoxUserRole.Administrator =>
            SoftBoxAccountPermission.ViewWorkspace |
            SoftBoxAccountPermission.DeviceRead |
            SoftBoxAccountPermission.DeviceOperations |
            SoftBoxAccountPermission.WorkshopOperations |
            SoftBoxAccountPermission.ExportData |
            SoftBoxAccountPermission.ManageUsers |
            SoftBoxAccountPermission.ManageSettings |
            SoftBoxAccountPermission.ManagePlugins |
            SoftBoxAccountPermission.ManageUpdates |
            SoftBoxAccountPermission.LockSession,
        SoftBoxUserRole.Technician =>
            SoftBoxAccountPermission.ViewWorkspace |
            SoftBoxAccountPermission.DeviceRead |
            SoftBoxAccountPermission.DeviceOperations |
            SoftBoxAccountPermission.WorkshopOperations |
            SoftBoxAccountPermission.ExportData |
            SoftBoxAccountPermission.LockSession,
        SoftBoxUserRole.ReadOnly =>
            SoftBoxAccountPermission.ViewWorkspace |
            SoftBoxAccountPermission.DeviceRead |
            SoftBoxAccountPermission.LockSession,
        _ => SoftBoxAccountPermission.None
    };

    public static bool IsAllowed(SoftBoxUserRole role, string? actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId) ||
            !Requirements.TryGetValue(actionId, out var required) ||
            required == SoftBoxAccountPermission.None)
            return false;

        return (PermissionsForRole(role) & required) == required;
    }

    public static bool TryGetRequiredPermission(string? actionId, out SoftBoxAccountPermission permission)
    {
        if (!string.IsNullOrWhiteSpace(actionId) && Requirements.TryGetValue(actionId, out permission))
            return true;
        permission = SoftBoxAccountPermission.None;
        return false;
    }

    public static IReadOnlySet<string> SupportedActionIds { get; } =
        new HashSet<string>(Requirements.Keys, StringComparer.Ordinal);
}
