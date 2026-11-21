using SoftBox.PhoneLab.Core.Backup;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidBackupSchedulePolicyTests
{
    [Fact]
    public void OnConnectScheduleRunsOnAConnectionTransitionOnly()
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = CreateSchedule() with { Kind = AndroidBackupScheduleKind.OnConnect };

        Assert.True(AndroidBackupSchedulePolicy.IsDue(schedule, deviceConnected: true, now));
        Assert.False(AndroidBackupSchedulePolicy.IsDue(schedule, deviceConnected: false, now));

        var alreadyConnected = schedule with
        {
            WasConnected = true,
            LastAttemptUtc = now.AddMinutes(-10),
            LastSuccessfulRunUtc = now.AddMinutes(-10)
        };
        Assert.False(AndroidBackupSchedulePolicy.IsDue(alreadyConnected, deviceConnected: true, now));
    }

    [Fact]
    public void DailyScheduleRunsAfterConfiguredLocalTimeOnlyOncePerDay()
    {
        var now = DateTimeOffset.UtcNow;
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        var localMinute = TimeOnly.FromDateTime(local.DateTime).ToString("HH:mm");
        var schedule = CreateSchedule() with
        {
            Kind = AndroidBackupScheduleKind.Daily,
            LocalTime = localMinute,
            LastSuccessfulRunUtc = now.AddDays(-1)
        };

        Assert.True(AndroidBackupSchedulePolicy.IsDue(schedule, deviceConnected: true, now));
        Assert.False(AndroidBackupSchedulePolicy.IsDue(
            schedule with { LastSuccessfulRunUtc = now }, deviceConnected: true, now));
    }

    [Fact]
    public void WeeklyScheduleRunsOnlyOnConfiguredLocalWeekday()
    {
        var now = DateTimeOffset.UtcNow;
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        var schedule = CreateSchedule() with
        {
            Kind = AndroidBackupScheduleKind.Weekly,
            WeeklyDay = local.DayOfWeek,
            LocalTime = "00:00",
            LastSuccessfulRunUtc = now.AddDays(-7)
        };

        Assert.True(AndroidBackupSchedulePolicy.IsDue(schedule, deviceConnected: true, now));
        Assert.False(AndroidBackupSchedulePolicy.IsDue(
            schedule with { WeeklyDay = NextDay(local.DayOfWeek) }, deviceConnected: true, now));
    }

    [Fact]
    public void RecentAttemptPreventsDuplicateScheduledRun()
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = CreateSchedule() with
        {
            Kind = AndroidBackupScheduleKind.Daily,
            LocalTime = "00:00",
            LastSuccessfulRunUtc = now.AddDays(-1),
            LastAttemptUtc = now.AddMinutes(-2)
        };

        Assert.False(AndroidBackupSchedulePolicy.IsDue(schedule, deviceConnected: true, now));
    }

    [Fact]
    public void DisabledSchedulesNeverRun()
    {
        var schedule = CreateSchedule() with { IsEnabled = false };

        Assert.False(AndroidBackupSchedulePolicy.IsDue(
            schedule, deviceConnected: true, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RejectsUnsafeDeviceAndRemotePaths()
    {
        var valid = CreateSchedule();

        Assert.Throws<ArgumentException>(() =>
            AndroidBackupSchedule.Validate(valid with { DeviceSerial = "-s DEVICE" }));
        Assert.Throws<ArgumentException>(() =>
            AndroidBackupSchedule.Validate(valid with { RemoteDirectory = "/sdcard/../data" }));
        Assert.Throws<ArgumentException>(() =>
            AndroidBackupSchedule.Validate(valid with { BackupPassword = "short" }));
    }


    [Fact]
    public void OnConnectScheduleRetriesAfterFailedAttemptButNotAfterSuccessfulAttempt()
    {
        var now = DateTimeOffset.UtcNow;
        var failed = CreateSchedule() with
        {
            Kind = AndroidBackupScheduleKind.OnConnect,
            WasConnected = true,
            LastSuccessfulRunUtc = now.AddDays(-1),
            LastAttemptUtc = now.AddMinutes(-10)
        };
        Assert.True(AndroidBackupSchedulePolicy.IsDue(failed, deviceConnected: true, now));

        var successful = failed with { LastSuccessfulRunUtc = now.AddMinutes(-9) };
        Assert.False(AndroidBackupSchedulePolicy.IsDue(successful, deviceConnected: true, now));
    }

    private static AndroidBackupSchedule CreateSchedule()
        => new()
        {
            DeviceSerial = "DEVICE-123",
            DeviceLabel = "Android test device",
            RemoteDirectory = "/sdcard/DCIM",
            DestinationDirectory = Path.Combine(Path.GetTempPath(), "SoftBoxBackupTests"),
            BackupPassword = "StrongBackupPassword123!"
        };

    private static DayOfWeek NextDay(DayOfWeek day)
        => (DayOfWeek)(((int)day + 1) % 7);
}
