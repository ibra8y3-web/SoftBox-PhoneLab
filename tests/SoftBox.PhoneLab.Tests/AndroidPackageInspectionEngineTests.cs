using SoftBox.PhoneLab.Core.Applications;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidPackageInspectionEngineTests
{
    [Fact]
    public void ParsesPackageMetadataUserInstallStateAndGrantedPermissionEvidence()
    {
        const string dumpsys = """
            Package [com.example.camera] (2a4b):
              userId=10234
              codePath=/data/app/~~abc/com.example.camera-xyz
              dataDir=/data/user/0/com.example.camera
              versionCode=42 minSdk=23 targetSdk=35
              versionName=2.1.0
              firstInstallTime=2026-10-01 10:30:00
              lastUpdateTime=2026-11-20 08:15:00
              requested permissions:
                android.permission.CAMERA
                android.permission.INTERNET
                android.permission.READ_PHONE_STATE
              install permissions:
                android.permission.INTERNET: granted=true
              runtime permissions:
                android.permission.CAMERA: granted=true, flags=[ USER_SET|USER_SENSITIVE_WHEN_GRANTED ]
                android.permission.READ_PHONE_STATE: granted=false, flags=[ USER_SET ]
              User 0: installed=true hidden=false suspended=false stopped=false notLaunched=false enabled=0 instant=false
            """;

        var result = AndroidPackageInspectionEngine.Parse("com.example.camera", dumpsys);

        Assert.Equal("com.example.camera", result.PackageName);
        Assert.Equal(42, result.VersionCode);
        Assert.Equal("2.1.0", result.VersionName);
        Assert.Equal(23, result.MinSdkVersion);
        Assert.Equal(35, result.TargetSdkVersion);
        Assert.Equal(10234, result.Uid);
        Assert.Equal("/data/app/~~abc/com.example.camera-xyz", result.CodePath);
        Assert.Equal("/data/user/0/com.example.camera", result.DataDirectory);
        Assert.True(result.InstalledForUser);
        Assert.False(result.HiddenForUser);
        Assert.False(result.SuspendedForUser);
        Assert.Equal(0, result.EnabledStateForUser);

        var camera = Assert.Single(result.Permissions.Where(item => item.Name == "android.permission.CAMERA"));
        Assert.Equal(AndroidPermissionRisk.DangerousRuntime, camera.Risk);
        Assert.True(camera.Granted);

        var phoneState = Assert.Single(result.Permissions.Where(item => item.Name == "android.permission.READ_PHONE_STATE"));
        Assert.Equal(AndroidPermissionRisk.DangerousRuntime, phoneState.Risk);
        Assert.False(phoneState.Granted);

        var internet = Assert.Single(result.Permissions.Where(item => item.Name == "android.permission.INTERNET"));
        Assert.Equal(AndroidPermissionRisk.Normal, internet.Risk);
        Assert.True(internet.Granted);
    }

    [Fact]
    public void DoesNotAssumeRequestedPermissionWasGrantedWhenGrantEvidenceIsAbsent()
    {
        const string dumpsys = """
            Package [com.example.app] (abc):
              requested permissions:
                android.permission.CAMERA
                android.permission.READ_CONTACTS
            """;

        var result = AndroidPackageInspectionEngine.Parse("com.example.app", dumpsys);

        Assert.Equal(2, result.Permissions.Count);
        Assert.All(result.Permissions, permission => Assert.Null(permission.Granted));
    }

    [Fact]
    public void IdentifiesSpecialAccessSeparatelyFromDangerousRuntimePermissions()
    {
        const string dumpsys = """
            Package [com.example.overlay] (abc):
              requested permissions:
                android.permission.SYSTEM_ALERT_WINDOW
                android.permission.REQUEST_INSTALL_PACKAGES
                android.permission.RECORD_AUDIO
                android.permission.FAKE_PRIVATE_API
            """;

        var result = AndroidPackageInspectionEngine.Parse("com.example.overlay", dumpsys);

        Assert.Equal(AndroidPermissionRisk.SpecialAccess,
            result.Permissions.Single(item => item.Name == "android.permission.SYSTEM_ALERT_WINDOW").Risk);
        Assert.Equal(AndroidPermissionRisk.SpecialAccess,
            result.Permissions.Single(item => item.Name == "android.permission.REQUEST_INSTALL_PACKAGES").Risk);
        Assert.Equal(AndroidPermissionRisk.DangerousRuntime,
            result.Permissions.Single(item => item.Name == "android.permission.RECORD_AUDIO").Risk);
        Assert.Equal(AndroidPermissionRisk.Unknown,
            result.Permissions.Single(item => item.Name == "android.permission.FAKE_PRIVATE_API").Risk);
    }

    [Fact]
    public void WarnsWhenObservedPackageHeaderDoesNotMatchSelectedPackage()
    {
        const string dumpsys = """
            Package [com.example.other] (abc):
              versionCode=1 minSdk=21 targetSdk=28
            """;

        var result = AndroidPackageInspectionEngine.Parse("com.example.selected", dumpsys);

        Assert.Equal("com.example.selected", result.PackageName);
        Assert.Contains(result.Warnings,
            warning => warning.Contains("لا يطابق الحزمة المختارة", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnsafePackageNamesBeforeParsing()
    {
        Assert.Throws<ArgumentException>(() =>
            AndroidPackageInspectionEngine.Parse("com.example;pm", "anything"));
        Assert.Throws<ArgumentException>(() =>
            AndroidPackageInspectionEngine.Parse("-s", "anything"));
    }

    [Fact]
    public void CapsInputAndReportsTruncation()
    {
        var huge = new string('x', AndroidPackageInspectionEngine.MaximumInputCharacters + 1);

        var result = AndroidPackageInspectionEngine.Parse("com.example.app", huge);

        Assert.Contains(result.Warnings, warning => warning.Contains("تم تقليص dumpsys", StringComparison.Ordinal));
    }

    [Fact]
    public void DoesNotInventVersionOrInstallationDataWhenMissing()
    {
        var result = AndroidPackageInspectionEngine.Parse(
            "com.example.app",
            "Package [com.example.app] (abc):\n  userId=10234");

        Assert.Null(result.VersionCode);
        Assert.Null(result.VersionName);
        Assert.Null(result.TargetSdkVersion);
        Assert.Null(result.InstalledForUser);
        Assert.Contains(result.Warnings, warning => warning.Contains("Version Code غير متاح", StringComparison.Ordinal));
    }
}
