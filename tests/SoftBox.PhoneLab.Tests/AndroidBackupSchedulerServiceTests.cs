using SoftBox.PhoneLab.Core.Backup;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidBackupSchedulerServiceTests
{
    [Fact]
    public async Task ProcessesEncryptedBackupWhenAuthorizedDeviceConnects()
    {
        Assert.True(OperatingSystem.IsWindows());

        var root = Path.Combine(Path.GetTempPath(), "SoftBoxSchedulerServiceTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AndroidBackupScheduleStore(Path.Combine(root, "schedules.dpapi"));
            var schedule = CreateSchedule(root);
            await store.SaveAsync([schedule]);
            var runner = new FakeScheduledBackupRunner();
            var service = new AndroidBackupSchedulerService(store, runner);
            var completed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.ScheduleStatusChanged += (id, status) =>
            {
                if (id == schedule.Id && status.StartsWith("نجح النسخ المشفر", StringComparison.Ordinal))
                    completed.TrySetResult(status);
            };

            var now = DateTimeOffset.UtcNow;
            await service.ProcessAsync(["DEVICE-123"], now);
            var status = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("نجح النسخ المشفر", status);
            Assert.Equal(1, runner.CallCount);
            var saved = Assert.Single(await store.LoadAsync());
            Assert.NotNull(saved.LastSuccessfulRunUtc);
            Assert.True(saved.WasConnected);

            // A second poll while the same device remains connected must not create another backup.
            await service.ProcessAsync(["DEVICE-123"], now.AddMinutes(1));
            Assert.Equal(1, runner.CallCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SupportsUserRequestedRunNow()
    {
        Assert.True(OperatingSystem.IsWindows());

        var root = Path.Combine(Path.GetTempPath(), "SoftBoxSchedulerManualTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AndroidBackupScheduleStore(Path.Combine(root, "schedules.dpapi"));
            var schedule = CreateSchedule(root);
            await store.SaveAsync([schedule]);
            var runner = new FakeScheduledBackupRunner();
            var service = new AndroidBackupSchedulerService(store, runner);

            await service.RunNowAsync(schedule.Id);

            Assert.Equal(1, runner.CallCount);
            var saved = Assert.Single(await store.LoadAsync());
            Assert.NotNull(saved.LastSuccessfulRunUtc);
            Assert.Contains("نجح النسخ المشفر", saved.LastResult);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static AndroidBackupSchedule CreateSchedule(string root)
        => new()
        {
            DeviceSerial = "DEVICE-123",
            DeviceLabel = "Test phone",
            RemoteDirectory = "/sdcard/DCIM",
            DestinationDirectory = Path.Combine(root, "backups"),
            BackupPassword = "StrongBackupPassword123!",
            Kind = AndroidBackupScheduleKind.OnConnect
        };

    private sealed class FakeScheduledBackupRunner : IAndroidScheduledBackupRunner
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<BackupArchiveManifest> BackupEncryptedAsync(
            string serial,
            string remoteDirectory,
            string destinationPath,
            string password,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new BackupArchiveManifest(
                "SoftBox",
                1,
                DateTimeOffset.UtcNow,
                serial,
                remoteDirectory,
                2,
                4096,
                Array.Empty<BackupFileEntry>(),
                "Encrypted scheduled backup test result"));
        }
    }
}
