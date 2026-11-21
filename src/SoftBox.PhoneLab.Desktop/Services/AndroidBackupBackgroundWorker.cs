using System;
using System.IO;
using System.Text.Json;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Executes due encrypted Android shared-storage backups without opening the normal UI.
/// The stored schedule is DPAPI-protected for the current Windows user.
/// </summary>
public static class AndroidBackupBackgroundWorker
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var adb = new AdbClient();
            if (!adb.IsAvailable)
            {
                WriteEvent("adb_unavailable", "لم يتم العثور على adb.exe في حزمة SoftBox أو PATH.");
                return 2;
            }

            await adb.StartServerAsync(cancellationToken);
            var devices = await new PhoneDiscoveryService(adb).DiscoverAsync(cancellationToken);
            var authorizedSerials = devices
                .Where(device => device.IsAuthorized)
                .Select(device => device.Serial)
                .Where(AdbClient.IsSafeSerial)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var scheduler = new AndroidBackupSchedulerService(adb);
            await scheduler.ProcessAsync(
                authorizedSerials,
                cancellationToken: cancellationToken,
                waitForCompletion: true);

            WriteEvent("worker_completed", "اكتمل فحص الجداول. الأجهزة المصرح بها المرصودة: " + authorizedSerials.Length + ".");
            return 0;
        }
        catch (OperationCanceledException)
        {
            WriteEvent("worker_cancelled", "تم إلغاء تشغيل مهمة النسخ الخلفية.");
            return 3;
        }
        catch (Exception ex)
        {
            WriteEvent("worker_failed", ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static void WriteEvent(string eventName, string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SoftBox", "PhoneLab", "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "android-backup-worker.jsonl");
            var line = JsonSerializer.Serialize(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                eventName,
                message
            });
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // The backup result remains persisted in the protected schedule store.
        }
    }
}
