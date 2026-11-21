using System.Text.Json;
using SoftBox.PhoneLab.Core.Security;

namespace SoftBox.PhoneLab.Core.Backup;

/// <summary>
/// Stores recurring backup definitions in a DPAPI CurrentUser-protected local file.
/// Passwords are never written as plaintext JSON to disk.
/// </summary>
public sealed class AndroidBackupScheduleStore
{
    private const int MaximumSchedules = 100;
    private const long MaximumStoreBytes = 1_048_576;
    private const string ProtectionPurpose = "AndroidBackupSchedules.v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public string PathName => _path;

    public AndroidBackupScheduleStore(string? path = null)
    {
        _path = System.IO.Path.GetFullPath(path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "backup-schedules.sbxdpapi"));
    }

    public async Task<IReadOnlyList<AndroidBackupSchedule>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var schedules = await LoadCoreAsync(cancellationToken);
            return schedules.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        IEnumerable<AndroidBackupSchedule> schedules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        var validated = schedules.Select(AndroidBackupSchedule.Validate).ToArray();
        if (validated.Length > MaximumSchedules)
            throw new InvalidDataException($"لا يمكن حفظ أكثر من {MaximumSchedules} جدولة نسخ احتياطي.");
        if (validated.Select(item => item.Id).Distinct().Count() != validated.Length)
            throw new InvalidDataException("توجد معرفات جدولة مكررة.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(validated, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<AndroidBackupSchedule>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return [];

        var info = new FileInfo(_path);
        if (info.Length is <= 0 or > MaximumStoreBytes)
            throw new InvalidDataException("ملف جدولة النسخ الاحتياطي فارغ أو تجاوز الحد الآمن.");

        var stored = await File.ReadAllBytesAsync(_path, cancellationToken);
        byte[] plaintext;
        bool wasProtected;
        try
        {
            plaintext = WindowsUserDataProtection.UnprotectOrLegacy(
                stored, ProtectionPurpose, out wasProtected);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(stored);
        }

        try
        {
            var schedules = JsonSerializer.Deserialize<List<AndroidBackupSchedule>>(plaintext, JsonOptions)
                ?? throw new InvalidDataException("ملف جدولة النسخ الاحتياطي غير صالح.");
            if (schedules.Count > MaximumSchedules ||
                schedules.Select(item => item.Id).Distinct().Count() != schedules.Count)
                throw new InvalidDataException("عدد الجداول أو معرفاتها غير صالح.");

            var validated = schedules.Select(AndroidBackupSchedule.Validate).ToList();
            if (!wasProtected)
                await SaveCoreAsync(validated, cancellationToken);
            return validated;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("تعذر قراءة ملف جدولة النسخ الاحتياطي.", ex);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private async Task SaveCoreAsync(
        IReadOnlyCollection<AndroidBackupSchedule> schedules,
        CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(schedules, JsonOptions);
        byte[] protectedBytes;
        try
        {
            protectedBytes = WindowsUserDataProtection.Protect(plaintext, ProtectionPurpose);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext);
        }

        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(protectedBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }
}
