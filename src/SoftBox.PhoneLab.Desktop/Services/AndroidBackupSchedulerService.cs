using System.Collections.Concurrent;
using SoftBox.PhoneLab.Core.Backup;

namespace SoftBox.PhoneLab.Desktop.Services;

public interface IAndroidScheduledBackupRunner
{
    Task<BackupArchiveManifest> BackupEncryptedAsync(
        string serial,
        string remoteDirectory,
        string destinationPath,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed class AndroidScheduledBackupRunner(AdbClient adb) : IAndroidScheduledBackupRunner
{
    private readonly AndroidBackupService _backup = new(adb);

    public Task<BackupArchiveManifest> BackupEncryptedAsync(
        string serial,
        string remoteDirectory,
        string destinationPath,
        string password,
        CancellationToken cancellationToken = default)
        => _backup.BackupSharedDirectoryEncryptedAsync(
            serial, remoteDirectory, destinationPath, password, cancellationToken: cancellationToken);
}

/// <summary>
/// Runs recurring Android shared-storage backups while the authenticated SoftBox session is open.
/// All schedule secrets are stored by the DPAPI-protected AndroidBackupScheduleStore.
/// </summary>
public sealed class AndroidBackupSchedulerService
{
    private readonly AndroidBackupScheduleStore _store;
    private readonly IAndroidScheduledBackupRunner _runner;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, byte> _running = new();

    public event Action<Guid, string>? ScheduleStatusChanged;

    public AndroidBackupSchedulerService(
        AndroidBackupScheduleStore store,
        IAndroidScheduledBackupRunner runner)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public AndroidBackupSchedulerService(AdbClient adb, AndroidBackupScheduleStore? store = null)
        : this(store ?? new AndroidBackupScheduleStore(), new AndroidScheduledBackupRunner(adb))
    {
    }

    public Task<IReadOnlyList<AndroidBackupSchedule>> GetSchedulesAsync(
        CancellationToken cancellationToken = default)
        => _store.LoadAsync(cancellationToken);

    public async Task AddAsync(AndroidBackupSchedule schedule, CancellationToken cancellationToken = default)
    {
        var validated = AndroidBackupSchedule.Validate(schedule);
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var all = (await _store.LoadAsync(cancellationToken)).ToList();
            if (all.Any(item => item.DeviceSerial.Equals(validated.DeviceSerial, StringComparison.Ordinal) &&
                                item.RemoteDirectory.Equals(validated.RemoteDirectory, StringComparison.Ordinal) &&
                                item.Kind == validated.Kind &&
                                item.IsEnabled))
                throw new InvalidOperationException("توجد جدولة مفعّلة بالفعل لنفس الجهاز والمجلد ونوع الجدولة.");

            all.Add(validated);
            await _store.SaveAsync(all, cancellationToken);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task RemoveAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var all = (await _store.LoadAsync(cancellationToken)).ToList();
            if (_running.ContainsKey(scheduleId))
                throw new InvalidOperationException("لا يمكن حذف جدولة أثناء تنفيذ نسختها الاحتياطية. أوقف الجدولة وانتظر اكتمال العملية.");
            all.RemoveAll(item => item.Id == scheduleId);
            await _store.SaveAsync(all, cancellationToken);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task SetEnabledAsync(
        Guid scheduleId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var all = (await _store.LoadAsync(cancellationToken)).ToList();
            var index = all.FindIndex(item => item.Id == scheduleId);
            if (index < 0)
                throw new KeyNotFoundException("الجدولة المحددة غير موجودة.");

            all[index] = all[index] with
            {
                IsEnabled = enabled,
                LastResult = enabled ? all[index].LastResult : "تم إيقاف الجدولة يدويًا."
            };
            await _store.SaveAsync(all, cancellationToken);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>
    /// Checks daily/weekly time rules and newly connected authorized devices.
    /// Backup jobs are launched asynchronously and never block the ADB discovery timer.
    /// </summary>
    public async Task ProcessAsync(
        IEnumerable<string> connectedAuthorizedSerials,
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectedAuthorizedSerials);
        var serials = connectedAuthorizedSerials
            .Where(IsSafeSerial)
            .ToHashSet(StringComparer.Ordinal);
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var due = new List<AndroidBackupSchedule>();

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var schedules = (await _store.LoadAsync(cancellationToken)).ToList();
            var changed = false;

            for (var index = 0; index < schedules.Count; index++)
            {
                var schedule = schedules[index];
                var connected = serials.Contains(schedule.DeviceSerial);
                var isDue = AndroidBackupSchedulePolicy.IsDue(schedule, connected, now);

                if (schedule.WasConnected != connected)
                {
                    schedule = schedule with { WasConnected = connected };
                    changed = true;
                }

                if (isDue && _running.TryAdd(schedule.Id, 0))
                {
                    schedule = schedule with
                    {
                        LastAttemptUtc = now,
                        LastResult = "جارٍ تنفيذ النسخة الاحتياطية المشفرة..."
                    };
                    due.Add(schedule);
                    changed = true;
                }

                schedules[index] = schedule;
            }

            if (changed)
                await _store.SaveAsync(schedules, cancellationToken);
        }
        finally
        {
            _stateGate.Release();
        }

        foreach (var schedule in due)
            _ = ExecuteScheduleAsync(schedule);
    }

    private async Task ExecuteScheduleAsync(AndroidBackupSchedule schedule)
    {
        try
        {
            Directory.CreateDirectory(schedule.DestinationDirectory);
            var safeSerialHint = schedule.DeviceSerial.Length > 4
                ? schedule.DeviceSerial[^4..]
                : "device";
            var fileName = $"SoftBox-Backup-{safeSerialHint}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.sbx";
            var destination = Path.Combine(schedule.DestinationDirectory, fileName);

            var manifest = await _runner.BackupEncryptedAsync(
                schedule.DeviceSerial,
                schedule.RemoteDirectory,
                destination,
                schedule.BackupPassword,
                CancellationToken.None);

            var result =
                $"نجح النسخ المشفر: {manifest.FileCount:N0} ملف، {manifest.TotalBytes:N0} بايت. الملف: {fileName}";
            await UpdateAfterRunAsync(schedule.Id, success: true, result);
        }
        catch (Exception ex)
        {
            await UpdateAfterRunAsync(
                schedule.Id,
                success: false,
                "فشل النسخ التلقائي: " + ex.Message);
        }
        finally
        {
            _running.TryRemove(schedule.Id, out _);
        }
    }

    private async Task UpdateAfterRunAsync(Guid scheduleId, bool success, string result)
    {
        await _stateGate.WaitAsync();
        try
        {
            var all = (await _store.LoadAsync()).ToList();
            var index = all.FindIndex(item => item.Id == scheduleId);
            if (index < 0)
                return;

            all[index] = all[index] with
            {
                LastSuccessfulRunUtc = success ? DateTimeOffset.UtcNow : all[index].LastSuccessfulRunUtc,
                LastResult = result.Length > 1900 ? result[..1900] : result
            };
            await _store.SaveAsync(all);
            ScheduleStatusChanged?.Invoke(scheduleId, all[index].LastResult);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private static bool IsSafeSerial(string serial)
        => !string.IsNullOrWhiteSpace(serial) &&
           !serial.StartsWith("-", StringComparison.Ordinal) &&
           serial.Length <= 128 &&
           serial.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');
}
