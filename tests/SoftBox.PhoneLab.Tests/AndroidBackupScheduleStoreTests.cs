using System.Security.Cryptography;
using System.Text;
using SoftBox.PhoneLab.Core.Backup;
using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidBackupScheduleStoreTests
{
    [Fact]
    public async Task ProtectsScheduleAndPasswordWithWindowsDpapi()
    {
        Assert.True(OperatingSystem.IsWindows());

        var root = Path.Combine(Path.GetTempPath(), "SoftBoxScheduleTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "schedules.dpapi");
        const string password = "PrivateScheduledBackupPassword123!";
        try
        {
            var store = new AndroidBackupScheduleStore(path);
            await store.SaveAsync([CreateSchedule(password)]);

            var stored = await File.ReadAllBytesAsync(path);
            Assert.True(WindowsUserDataProtection.IsProtected(stored));
            Assert.False(ContainsSequence(stored, Encoding.UTF8.GetBytes(password)));

            var loaded = await store.LoadAsync();
            var schedule = Assert.Single(loaded);
            Assert.Equal(password, schedule.BackupPassword);
            Assert.Equal("DEVICE-123", schedule.DeviceSerial);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsTamperedProtectedScheduleStore()
    {
        Assert.True(OperatingSystem.IsWindows());

        var root = Path.Combine(Path.GetTempPath(), "SoftBoxScheduleTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "schedules.dpapi");
        try
        {
            var store = new AndroidBackupScheduleStore(path);
            await store.SaveAsync([CreateSchedule("PrivateScheduledBackupPassword123!")]);
            var bytes = await File.ReadAllBytesAsync(path);
            bytes[^1] ^= 0x40;
            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAsync<CryptographicException>(() => store.LoadAsync());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsDuplicateScheduleIds()
    {
        Assert.True(OperatingSystem.IsWindows());

        var root = Path.Combine(Path.GetTempPath(), "SoftBoxScheduleTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AndroidBackupScheduleStore(Path.Combine(root, "schedules.dpapi"));
            var schedule = CreateSchedule("PrivateScheduledBackupPassword123!");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync([schedule, schedule]));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static AndroidBackupSchedule CreateSchedule(string password)
        => new()
        {
            DeviceSerial = "DEVICE-123",
            DeviceLabel = "Test phone",
            RemoteDirectory = "/sdcard/DCIM",
            DestinationDirectory = Path.Combine(Path.GetTempPath(), "SoftBoxScheduleTests", "output"),
            BackupPassword = password,
            Kind = AndroidBackupScheduleKind.Daily,
            LocalTime = "02:30"
        };

    private static bool ContainsSequence(byte[] source, byte[] value)
        => source.AsSpan().IndexOf(value) >= 0;
}
