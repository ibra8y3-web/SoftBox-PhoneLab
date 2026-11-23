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
            ["OpenMultiScreenSessions"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenScreenRecording"] = SoftBoxAccountPermission.DeviceOperations,
            ["AnalyzeScreen"] = SoftBoxAccountPermission.DeviceRead,
            ["MeasurementRig"] = SoftBoxAccountPermission.DeviceRead,
            ["RepairGuides"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenApkLab"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenNetworkDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenAndroidLogAnalyzer"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenWirelessAdbPairing"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenAndroidFileManager"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenAndroidAppManager"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenAndroidCommandCenter"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenAndroidBatchTools"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenAndroidServiceOperations"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenAndroidWorkflow"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenFastbootManager"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenDeviceCapabilityCenter"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenDeviceAccessRecoveryCenter"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenUsbDoctor"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenUsbDriverCenter"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenUsbDriverSignatureVerifier"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenQualcommUsbDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenMediaTekUsbDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenFirmwareCenter"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenDeviceHistory"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenNotificationCenter"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenDashboardAnalytics"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenSupportBundle"] = SoftBoxAccountPermission.ExportData,
            ["OpenSupportCenter"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenUpdateCenter"] = SoftBoxAccountPermission.ManageUpdates,
            ["OpenHelpCenter"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenOfflineCenter"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenApplicationSettings"] = SoftBoxAccountPermission.ManageSettings,
            ["OpenAbout"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenPartsInventory"] = SoftBoxAccountPermission.WorkshopOperations,
            ["OpenSamsungDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenSamsungServiceOperations"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenSamsungOdinAssistant"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenSamsungOfficialTools"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenXiaomiDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenXiaomiFastbootService"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenXiaomiOfficialTools"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenBrandDiagnostics"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenBrandOfficialTools"] = SoftBoxAccountPermission.ViewWorkspace,
            ["OpenQualcommRecovery"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenMediaTekRecovery"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenPluginStore"] = SoftBoxAccountPermission.ManagePlugins,
            ["OpenRepairAdvisor"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenOneClickDiagnostic"] = SoftBoxAccountPermission.DeviceRead,
            ["OpenPhoneBackup"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenAndroidBackupScheduler"] = SoftBoxAccountPermission.DeviceOperations,
            ["OpenWorkshopBackup"] = SoftBoxAccountPermission.WorkshopOperations,
            ["OpenWorkshopBackupScheduler"] = SoftBoxAccountPermission.WorkshopOperations,
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
