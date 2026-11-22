using SoftBox.PhoneLab.Core.Settings;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class PhoneLabSettingsServiceTests
{
    [Fact]
    public async Task LoadsDefaultsAndPersistsUserPreferences()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-settings-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new PhoneLabSettingsService(path);
            var defaults = await store.LoadAsync();
            Assert.True(defaults.AutoDiscoverDevices);
            Assert.Equal(3, defaults.DiscoveryIntervalSeconds);
            Assert.True(defaults.AutoInstallAgent);
            Assert.Equal(1.0, defaults.UiScale);

            var selected = defaults with
            {
                AutoDiscoverDevices = false,
                DiscoveryIntervalSeconds = 15,
                AutoInstallAgent = false,
                MaximumBatchConcurrency = 5,
                UiScale = 0.85
            };
            await store.SaveAsync(selected);

            var reloaded = await new PhoneLabSettingsService(path).LoadAsync();
            Assert.Equal(selected, reloaded);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(121, 3)]
    [InlineData(3, 0)]
    [InlineData(3, 9)]
    public async Task SaveRejectsUnsafeIntervalsAndConcurrency(int interval, int concurrency)
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-invalid-settings-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new PhoneLabSettingsService(path);
            var invalid = new PhoneLabSettings(DiscoveryIntervalSeconds: interval, MaximumBatchConcurrency: concurrency);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(invalid));
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadRejectsUnsupportedSchema()
    {
        var directory = Path.Combine(Path.GetTempPath(), "softbox-settings-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, """{"SchemaVersion":99}""");
            var store = new PhoneLabSettingsService(path);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
