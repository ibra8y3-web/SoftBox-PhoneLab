using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record TaskSchedulerCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface ITaskSchedulerCommandRunner
{
    Task<TaskSchedulerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs Windows Task Scheduler through schtasks.exe without shell concatenation.
/// </summary>
public sealed class ProcessTaskSchedulerCommandRunner : ITaskSchedulerCommandRunner
{
    public async Task<TaskSchedulerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var start = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("تعذر تشغيل Windows Task Scheduler.");

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new TaskSchedulerCommandResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }
}

/// <summary>
/// Creates a daily interactive task under the current Windows user. The scheduled script
/// copies the DPAPI-protected workshop file as-is; it never decrypts customer information.
/// </summary>
public sealed class WorkshopBackupSchedulerService
{
    public const string ScheduledTaskName = @"\SoftBox\PhoneLab\DailyEncryptedWorkshopBackup";
    public const string BackupFilePattern = "SoftBox-Workshop-Backup-*.sbxbackup";
    private const string DpapiMarker = "SBXDPAPI1";
    private readonly ITaskSchedulerCommandRunner _runner;
    private readonly Func<string?> _userSidProvider;
    private readonly string _temporaryDirectory;

    public WorkshopBackupSchedulerService(
        ITaskSchedulerCommandRunner? runner = null,
        Func<string?>? userSidProvider = null,
        string? temporaryDirectory = null)
    {
        _runner = runner ?? new ProcessTaskSchedulerCommandRunner();
        _userSidProvider = userSidProvider ?? GetCurrentUserSid;
        _temporaryDirectory = Path.GetFullPath(temporaryDirectory ?? Path.GetTempPath());
    }

    public async Task ConfigureDailyBackupAsync(
        string workshopDatabasePath,
        string destinationDirectory,
        TimeOnly localTime,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("جدولة النسخ التلقائي تتطلب Windows Task Scheduler.");
        if (retentionDays is < 1 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "مدة الاحتفاظ يجب أن تكون بين يوم واحد و10 سنوات.");

        var source = Path.GetFullPath(workshopDatabasePath);
        var destination = Path.GetFullPath(destinationDirectory);
        if (!File.Exists(source))
            throw new FileNotFoundException("قاعدة بيانات الورشة غير موجودة بعد. افتح الورشة واحفظ بياناتها أولًا.", source);
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("مجلد النسخ الاحتياطي مطلوب.", nameof(destinationDirectory));

        var sourceBytes = await File.ReadAllBytesAsync(source, cancellationToken);
        try
        {
            if (!IsDpapiProtectedWorkshopFile(sourceBytes))
                throw new InvalidDataException("لن تتم جدولة نسخة من قاعدة غير مشفرة. افتح الورشة أولًا ليتم ترحيل JSON القديم إلى DPAPI.");
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(sourceBytes);
        }

        var sid = _userSidProvider();
        if (string.IsNullOrWhiteSpace(sid) || sid.Length > 184 || sid.Any(char.IsControl))
            throw new InvalidOperationException("تعذر تحديد معرف حساب Windows الحالي لإنشاء مهمة خاصة به.");

        Directory.CreateDirectory(destination);
        var document = BuildTaskDefinitionXml(
            sid,
            source,
            destination,
            localTime,
            retentionDays,
            DateTime.Now);
        Directory.CreateDirectory(_temporaryDirectory);
        var definitionPath = Path.Combine(
            _temporaryDirectory,
            "SoftBox-Workshop-Backup-" + Guid.NewGuid().ToString("N") + ".xml");

        try
        {
            await File.WriteAllTextAsync(
                definitionPath,
                document,
                new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
                cancellationToken);

            var result = await _runner.RunAsync(
                ["/Create", "/TN", ScheduledTaskName, "/XML", definitionPath, "/F"],
                cancellationToken);
            EnsureSuccess(result, "تعذر تسجيل مهمة النسخ الاحتياطي اليومية في Windows Task Scheduler.");
        }
        finally
        {
            try { File.Delete(definitionPath); } catch { }
        }
    }

    public async Task DeleteDailyBackupAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("إدارة مهام النسخ التلقائي تتطلب Windows Task Scheduler.");

        var result = await _runner.RunAsync(
            ["/Delete", "/TN", ScheduledTaskName, "/F"],
            cancellationToken);
        EnsureSuccess(result, "تعذر حذف مهمة النسخ الاحتياطي من Windows Task Scheduler.");
    }

    public static bool IsDpapiProtectedWorkshopFile(ReadOnlySpan<byte> content)
        => content.Length > DpapiMarker.Length &&
           content[..DpapiMarker.Length].SequenceEqual(Encoding.ASCII.GetBytes(DpapiMarker));

    public static string BuildTaskDefinitionXml(
        string userSid,
        string workshopDatabasePath,
        string destinationDirectory,
        TimeOnly localTime,
        int retentionDays,
        DateTime localNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        ArgumentException.ThrowIfNullOrWhiteSpace(workshopDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (retentionDays is < 1 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(retentionDays));

        var source = Path.GetFullPath(workshopDatabasePath);
        var destination = Path.GetFullPath(destinationDirectory);
        var nextRun = localNow.Date.Add(localTime.ToTimeSpan());
        if (nextRun <= localNow)
            nextRun = nextRun.AddDays(1);

        var script = BuildBackupScript(source, destination, retentionDays);
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var taskNamespace = XNamespace.Get("http://schemas.microsoft.com/windows/2004/02/mit/task");
        var task = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(taskNamespace + "Task",
                new XAttribute("version", "1.2"),
                new XElement(taskNamespace + "RegistrationInfo",
                    new XElement(taskNamespace + "Author", "SoftBox PhoneLab"),
                    new XElement(taskNamespace + "Description",
                        "Copies the DPAPI-protected SoftBox workshop database for the current Windows user and prunes expired SoftBox backups.")),
                new XElement(taskNamespace + "Triggers",
                    new XElement(taskNamespace + "CalendarTrigger",
                        new XElement(taskNamespace + "StartBoundary",
                            nextRun.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)),
                        new XElement(taskNamespace + "Enabled", "true"),
                        new XElement(taskNamespace + "ScheduleByDay",
                            new XElement(taskNamespace + "DaysInterval", "1")))),
                new XElement(taskNamespace + "Principals",
                    new XElement(taskNamespace + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(taskNamespace + "UserId", userSid),
                        new XElement(taskNamespace + "LogonType", "InteractiveToken"),
                        new XElement(taskNamespace + "RunLevel", "LeastPrivilege"))),
                new XElement(taskNamespace + "Settings",
                    new XElement(taskNamespace + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(taskNamespace + "DisallowStartIfOnBatteries", "false"),
                    new XElement(taskNamespace + "StopIfGoingOnBatteries", "false"),
                    new XElement(taskNamespace + "AllowHardTerminate", "true"),
                    new XElement(taskNamespace + "StartWhenAvailable", "true"),
                    new XElement(taskNamespace + "Enabled", "true"),
                    new XElement(taskNamespace + "Hidden", "false"),
                    new XElement(taskNamespace + "ExecutionTimeLimit", "PT10M"),
                    new XElement(taskNamespace + "Priority", "7")),
                new XElement(taskNamespace + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(taskNamespace + "Exec",
                        new XElement(taskNamespace + "Command", "powershell.exe"),
                        new XElement(taskNamespace + "Arguments",
                            "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encodedScript),
                        new XElement(taskNamespace + "WorkingDirectory", Path.GetDirectoryName(source) ?? destination)))));
        return task.ToString(SaveOptions.DisableFormatting);
    }

    public static string BuildBackupScript(
        string workshopDatabasePath,
        string destinationDirectory,
        int retentionDays)
    {
        if (retentionDays is < 1 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(retentionDays));

        var source = PowerShellLiteral(Path.GetFullPath(workshopDatabasePath));
        var destination = PowerShellLiteral(Path.GetFullPath(destinationDirectory));
        return $"""
            $ErrorActionPreference = 'Stop'
            $source = '{source}'
            $destinationDirectory = '{destination}'
            $retentionDays = {retentionDays}
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {{ throw 'Workshop database is missing.' }}
            if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) {{
                [void][IO.Directory]::CreateDirectory($destinationDirectory)
            }}
            $directoryInfo = [IO.DirectoryInfo]::new($destinationDirectory)
            if (($directoryInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {{
                throw 'Backup destination cannot be a reparse point.'
            }}
            $sourceBytes = [IO.File]::ReadAllBytes($source)
            if ($sourceBytes.Length -le 9 -or [Text.Encoding]::ASCII.GetString($sourceBytes, 0, 9) -ne 'SBXDPAPI1') {{
                throw 'Workshop database is not a DPAPI-protected SoftBox file.'
            }}
            $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
            $fileName = 'SoftBox-Workshop-Backup-' + $timestamp + '.sbxbackup'
            $target = Join-Path $destinationDirectory $fileName
            $temporary = $target + '.tmp'
            try {{
                [IO.File]::WriteAllBytes($temporary, $sourceBytes)
                [IO.File]::Move($temporary, $target)
            }}
            finally {{
                if (Test-Path -LiteralPath $temporary) {{ Remove-Item -LiteralPath $temporary -Force }}
                $sourceBytes = $null
            }}
            $cutoffUtc = [DateTime]::UtcNow.AddDays(-$retentionDays)
            foreach ($candidate in [IO.Directory]::EnumerateFiles($destinationDirectory, '{BackupFilePattern}', [IO.SearchOption]::TopDirectoryOnly)) {{
                $candidateInfo = [IO.FileInfo]::new($candidate)
                if (($candidateInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and $candidateInfo.LastWriteTimeUtc -lt $cutoffUtc) {{
                    [IO.File]::Delete($candidate)
                }}
            }}
            """;
    }

    private static string PowerShellLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string? GetCurrentUserSid()
        => WindowsIdentity.GetCurrent().User?.Value;

    private static void EnsureSuccess(TaskSchedulerCommandResult result, string message)
    {
        if (result.ExitCode != 0)
        {
            var detail = string.Join(Environment.NewLine,
                new[] { result.StandardError, result.StandardOutput }.Where(text => !string.IsNullOrWhiteSpace(text)));
            throw new InvalidOperationException(
                detail.Length == 0 ? message : message + Environment.NewLine + detail);
        }
    }
}
