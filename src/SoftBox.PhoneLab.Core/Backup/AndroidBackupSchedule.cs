using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Backup;

public enum AndroidBackupScheduleKind
{
    OnConnect,
    Daily,
    Weekly
}

/// <summary>
/// A recurring encrypted shared-storage backup definition. The containing schedule store
/// must be protected with Windows DPAPI because BackupPassword is a secret.
/// </summary>
public sealed record AndroidBackupSchedule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DeviceSerial { get; init; } = "";
    public string DeviceLabel { get; init; } = "";
    public string RemoteDirectory { get; init; } = "/sdcard/DCIM";
    public string DestinationDirectory { get; init; } = "";
    public AndroidBackupScheduleKind Kind { get; init; } = AndroidBackupScheduleKind.OnConnect;
    public string LocalTime { get; init; } = "02:00";
    public DayOfWeek WeeklyDay { get; init; } = DayOfWeek.Sunday;
    public string BackupPassword { get; init; } = "";
    public bool IsEnabled { get; init; } = true;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastAttemptUtc { get; init; }
    public DateTimeOffset? LastSuccessfulRunUtc { get; init; }
    public bool WasConnected { get; init; }
    public string LastResult { get; init; } = "لم يتم التشغيل بعد";

    public static AndroidBackupSchedule Validate(AndroidBackupSchedule value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Id == Guid.Empty)
            throw new ArgumentException("معرف الجدولة غير صالح.", nameof(value));
        if (string.IsNullOrWhiteSpace(value.DeviceSerial) ||
            value.DeviceSerial.StartsWith("-", StringComparison.Ordinal) ||
            !Regex.IsMatch(value.DeviceSerial, @"^[A-Za-z0-9._:-]{1,128}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Serial الجهاز غير صالح.", nameof(value));
        if (string.IsNullOrWhiteSpace(value.RemoteDirectory) ||
            value.RemoteDirectory.Length > 1024 ||
            value.RemoteDirectory.Any(char.IsControl) ||
            !value.RemoteDirectory.StartsWith("/", StringComparison.Ordinal) ||
            value.RemoteDirectory.Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException("مسار النسخ من الهاتف غير صالح.", nameof(value));
        if (string.IsNullOrWhiteSpace(value.DestinationDirectory) ||
            !Path.IsPathFullyQualified(value.DestinationDirectory) ||
            value.DestinationDirectory.Length > 2048)
            throw new ArgumentException("مجلد حفظ النسخ الاحتياطية المحلي غير صالح.", nameof(value));
        if (!Enum.IsDefined(value.Kind))
            throw new ArgumentOutOfRangeException(nameof(value.Kind));
        if (!Enum.IsDefined(value.WeeklyDay))
            throw new ArgumentOutOfRangeException(nameof(value.WeeklyDay));
        if (value.Kind is AndroidBackupScheduleKind.Daily or AndroidBackupScheduleKind.Weekly &&
            !TimeOnly.TryParseExact(value.LocalTime, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("وقت الجدولة يجب أن يكون بصيغة 24 ساعة HH:mm.", nameof(value));
        EncryptedBackupService.ValidatePassword(value.BackupPassword);
        if (value.LastResult.Length > 2000)
            throw new ArgumentException("رسالة نتيجة الجدولة أطول من الحد المسموح.", nameof(value));
        return value;
    }

    public override string ToString()
    {
        var pattern = Kind switch
        {
            AndroidBackupScheduleKind.OnConnect => "عند توصيل الهاتف",
            AndroidBackupScheduleKind.Daily => "يوميًا " + LocalTime,
            AndroidBackupScheduleKind.Weekly => $"أسبوعيًا ({WeeklyDay}) {LocalTime}",
            _ => Kind.ToString()
        };
        return $"{DeviceLabel} | {pattern} | {(IsEnabled ? "مفعّل" : "متوقف")} | {LastResult}";
    }
}

public static class AndroidBackupSchedulePolicy
{
    public static bool IsDue(
        AndroidBackupSchedule schedule,
        bool deviceConnected,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (!schedule.IsEnabled || !deviceConnected)
            return false;

        if (schedule.LastAttemptUtc is DateTimeOffset lastAttempt &&
            nowUtc - lastAttempt < TimeSpan.FromMinutes(5))
            return false;

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, TimeZoneInfo.Local);
        switch (schedule.Kind)
        {
            case AndroidBackupScheduleKind.OnConnect:
                return !schedule.WasConnected ||
                       (schedule.LastSuccessfulRunUtc is null && schedule.LastAttemptUtc is null);

            case AndroidBackupScheduleKind.Daily:
            {
                if (!TimeOnly.TryParseExact(schedule.LocalTime, "HH:mm", out var dueTime) ||
                    TimeOnly.FromDateTime(localNow.DateTime) < dueTime)
                    return false;
                return schedule.LastSuccessfulRunUtc is null ||
                       TimeZoneInfo.ConvertTime(schedule.LastSuccessfulRunUtc.Value, TimeZoneInfo.Local).Date
                           < localNow.Date;
            }

            case AndroidBackupScheduleKind.Weekly:
            {
                if (localNow.DayOfWeek != schedule.WeeklyDay ||
                    !TimeOnly.TryParseExact(schedule.LocalTime, "HH:mm", out var dueTime) ||
                    TimeOnly.FromDateTime(localNow.DateTime) < dueTime)
                    return false;
                return schedule.LastSuccessfulRunUtc is null ||
                       TimeZoneInfo.ConvertTime(schedule.LastSuccessfulRunUtc.Value, TimeZoneInfo.Local).Date
                           < localNow.Date;
            }

            default:
                return false;
        }
    }
}
