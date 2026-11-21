using System;
using System.IO;
using System.Security.Principal;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Installs a per-user Windows Task Scheduler trigger that invokes the same signed/self-contained
/// SoftBox executable in its background-only backup mode. It runs only while that Windows user is
/// interactively logged on; the USB device must be connected and ADB-authorized.
/// </summary>
public sealed class AndroidBackupTaskSchedulerService
{
    public const string ScheduledTaskName = @"\\SoftBoxPhoneLabAndroidBackupWorker";
    private const string WorkerArgument = "--run-android-backups";
    private readonly ITaskSchedulerCommandRunner _runner;

    public AndroidBackupTaskSchedulerService(ITaskSchedulerCommandRunner? runner = null)
        => _runner = runner ?? new ProcessTaskSchedulerCommandRunner();

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Task Scheduler is available only on Windows.");

        var result = await _runner.RunAsync(
            ["/Query", "/TN", ScheduledTaskName, "/FO", "LIST"],
            cancellationToken);
        return result.ExitCode == 0;
    }

    public async Task InstallAsync(string? executablePath = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Task Scheduler is available only on Windows.");

        var path = Path.GetFullPath(executablePath ?? Environment.ProcessPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("تعذر العثور على ملف SoftBox التنفيذي الذي سيشغل مهام النسخ الخلفية.", path);

        var username = WindowsIdentity.GetCurrent().Name;
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("تعذر تحديد حساب Windows الحالي.");

        var action = BuildTaskRunCommand(path);
        var result = await _runner.RunAsync(
        [
            "/Create", "/TN", ScheduledTaskName,
            "/SC", "MINUTE", "/MO", "5",
            "/TR", action,
            "/RU", username,
            "/IT",
            "/F"
        ], cancellationToken);

        EnsureSuccess(result, "تعذر إنشاء مهمة النسخ الاحتياطي في Windows Task Scheduler.");
    }

    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Task Scheduler is available only on Windows.");

        if (!await IsInstalledAsync(cancellationToken))
            return;

        var result = await _runner.RunAsync(
            ["/Delete", "/TN", ScheduledTaskName, "/F"],
            cancellationToken);
        EnsureSuccess(result, "تعذر حذف مهمة النسخ الاحتياطي من Windows Task Scheduler.");
    }

    public static string BuildTaskRunCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullPath = Path.GetFullPath(executablePath);
        return "\"" + fullPath + "\" " + WorkerArgument;
    }

    private static void EnsureSuccess(TaskSchedulerCommandResult result, string message)
    {
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException(message + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " " + detail.Trim()));
        }
    }
}
