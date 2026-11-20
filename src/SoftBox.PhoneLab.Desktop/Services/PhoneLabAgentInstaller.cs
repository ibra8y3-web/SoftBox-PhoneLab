using System.IO;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed class PhoneLabAgentInstaller(AdbClient adb)
{
    private const string PackageName = "com.softbox.phonelab.agent";
    private const string ConnectAction = "com.softbox.phonelab.agent.action.CONNECT_REQUEST";
    private string ApkPath => Path.Combine(AppContext.BaseDirectory, "Agent", "SoftBox-PhoneLab-Agent.apk");

    public async Task InstallAndRequestConsentAsync(string serial, CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial)) throw new ArgumentException("Invalid ADB serial.", nameof(serial));
        if (!File.Exists(ApkPath)) throw new FileNotFoundException("PhoneLab Agent APK is missing from the offline package.", ApkPath);
        var result = await adb.RunAsync(["-s", serial, "install", "-r", ApkPath], cancellationToken);
        if (!result.Contains("Success", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PhoneLab Agent installation failed: " + result.Trim());
        await adb.RunForDeviceAsync(serial, "shell", "am", "start", "-n",
            PackageName + "/.MainActivity", "-a", ConnectAction);
    }
}
