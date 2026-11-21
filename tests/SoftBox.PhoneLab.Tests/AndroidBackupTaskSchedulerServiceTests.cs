using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidBackupTaskSchedulerServiceTests
{
    [Fact]
    public void ScheduledTaskCommandQuotesExecutablePathWithSpaces()
    {
        var command = AndroidBackupTaskSchedulerService.BuildTaskRunCommand(@"C:\Program Files\SoftBox PhoneLab\SoftBox-PhoneLab.exe");
        Assert.Equal("\"C:\\Program Files\\SoftBox PhoneLab\\SoftBox-PhoneLab.exe\" --run-android-backups", command);
    }

    [Fact]
    public void ScheduledTaskNameUsesStableRootTaskIdentifier()
    {
        Assert.Equal(@"\SoftBoxPhoneLabAndroidBackupWorker", AndroidBackupTaskSchedulerService.ScheduledTaskName);
    }
}
