using System.Diagnostics;
using System.IO;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record UsbTransportReport(
    string AdbDevices, string FastbootDevices, bool AdbAvailable, bool FastbootAvailable,
    IReadOnlyList<string> Notes, string CapturedAtUtc);

/// <summary>Queries existing Android transport tools only. It does not change boot modes or unlock devices.</summary>
public sealed class UsbTransportInspector(AdbClient adb)
{
    public async Task<UsbTransportReport> InspectAsync(CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        var adbOutput = "ADB غير متاح";
        var fastbootOutput = "Fastboot غير متاح";
        var adbOk = false;
        var fastbootOk = false;
        if (adb.IsAvailable)
        {
            try
            {
                await adb.StartServerAsync(cancellationToken);
                adbOutput = await adb.RunAsync(["devices", "-l"], cancellationToken);
                adbOk = true;
            }
            catch (Exception ex) { adbOutput = ex.Message; }
        }
        var fastboot = FindFastboot();
        if (fastboot is not null)
        {
            try
            {
                fastbootOutput = await RunFastbootAsync(fastboot, cancellationToken);
                fastbootOk = true;
            }
            catch (Exception ex) { fastbootOutput = ex.Message; }
        }

        notes.Add("MTP / نقل الملفات لا يساوي تصريح ADB؛ لا يمكن استنتاج صلاحية التشخيص من ظهور الهاتف في مستكشف الملفات.");
        notes.Add("ADB unauthorized: افتح قفل الهاتف ووافق على بصمة RSA الخاصة بهذا الكمبيوتر.");
        notes.Add("Fastboot يظهر فقط إذا كان الجهاز في وضع Bootloader/Fastboot وكان تعريف USB المناسب مثبتًا.");
        notes.Add("Recovery وSamsung Download Mode وبعض أوضاع Qualcomm/MediaTek تستخدم واجهات خاصة؛ قد لا تظهر في ADB أو Fastboot العام.");
        notes.Add("لا يتم تبديل وضع الإقلاع أو فتح القفل أو إجراء مسح أو تفليش من هذه الأداة.");
        return new UsbTransportReport(adbOutput, fastbootOutput, adbOk, fastbootOk, notes, DateTimeOffset.UtcNow.ToString("O"));
    }

    private static string? FindFastboot()
    {
        var basePath = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(basePath, "Tools", "platform-tools", "fastboot.exe"),
            Path.Combine(basePath, "Tools", "fastboot.exe"),
            Path.Combine(basePath, "fastboot.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<string> RunFastbootAsync(string executable, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add("devices");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("تعذر تشغيل fastboot.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { try { process.Kill(true); } catch { } throw; }
        var outText = await stdout;
        var errText = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(errText) ? outText : errText);
        return string.IsNullOrWhiteSpace(outText) ? "لم يتم اكتشاف جهاز Fastboot." : outText;
    }
}
