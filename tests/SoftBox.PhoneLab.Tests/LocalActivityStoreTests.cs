using SoftBox.PhoneLab.Core.Activity;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class LocalActivityStoreTests
{
    [Fact]
    public async Task DeviceHistoryUsesStableHashedIdentityAndMaskedSerial()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new LocalActivityStore(root);
            var first = await store.AddDeviceHistoryAsync(
                "R58N123456A", "Samsung", "SM-A525F", "Inspection", "Read Android properties",
                "Android 14, API 34");
            var history = await store.GetDeviceHistoryAsync("R58N123456A");
            var wrong = await store.GetDeviceHistoryAsync("OtherSerial");

            Assert.Equal("••••456A", first.MaskedSerial);
            Assert.Equal(LocalActivityStore.CreateDeviceFingerprint("R58N123456A"), first.DeviceFingerprint);
            Assert.Single(history);
            Assert.Empty(wrong);
            Assert.DoesNotContain("R58N123456A", await File.ReadAllTextAsync(store.DatabasePath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task NotificationsCanBeMarkedReadAndSurviveStoreRestart()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new LocalActivityStore(root);
            var notification = await store.AddNotificationAsync(
                "device", "تم اكتشاف جهاز", "جهاز جديد متصل عبر ADB.", "device-center", "ABC12345");
            Assert.Single(await store.GetNotificationsAsync(unreadOnly: true));

            await store.MarkNotificationReadAsync(notification.Id);
            Assert.Empty(await store.GetNotificationsAsync(unreadOnly: true));

            var reloaded = new LocalActivityStore(root);
            var all = await reloaded.GetNotificationsAsync();
            Assert.Single(all);
            Assert.True(all[0].IsRead);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task MarkAllNotificationsReadUpdatesEntireInbox()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new LocalActivityStore(root);
            await store.AddNotificationAsync("system", "First", "One", "dashboard");
            await store.AddNotificationAsync("system", "Second", "Two", "dashboard");
            await store.MarkAllNotificationsReadAsync();

            Assert.Empty(await store.GetNotificationsAsync(unreadOnly: true));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DeviceHistoryRejectsControlCharactersAndInvalidSerial()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new LocalActivityStore(root);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.AddDeviceHistoryAsync("serial", "Mfr", "Model", "Type", "Bad\nSummary"));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.AddDeviceHistoryAsync("invalid serial", "Mfr", "Model", "Type", "Summary"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-activity-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
