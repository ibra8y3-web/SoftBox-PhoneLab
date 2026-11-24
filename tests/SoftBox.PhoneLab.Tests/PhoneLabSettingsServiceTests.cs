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
            Assert.Equal("ar", defaults.UiLanguage);
            Assert.Equal(string.Empty, defaults.AirPlayReceiverExecutablePath);
            Assert.Equal(string.Empty, defaults.IPhoneToolsDirectory);
            Assert.True(defaults.AutoStartAirPlayReceiver);
            Assert.Equal(string.Empty, defaults.FfmpegExecutablePath);

            var selected = defaults with
            {
                AutoDiscoverDevices = false,
                DiscoveryIntervalSeconds = 15,
                AutoInstallAgent = false,
                MaximumBatchConcurrency = 5,
                UiScale = 0.85,
                UiLanguage = "en",
                AirPlayReceiverExecutablePath = @"C:\SoftBox\AirPlay\airplay-windows.exe",
                IPhoneToolsDirectory = @"C:\SoftBox\AppleTools",
                AutoStartAirPlayReceiver = false,
                FfmpegExecutablePath = @"C:\Tools\ffmpeg.exe"
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

    [Theory]
    [InlineData(0.69)]
    [InlineData(1.06)]
    public async Task SaveRejectsOutOfRangeUiScale(double scale)
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-invalid-scale-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new PhoneLabSettingsService(path);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                store.SaveAsync(new PhoneLabSettings(UiScale: scale)));
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LegacySettingsWithoutLanguageDefaultToArabic()
    {
        var directory = Path.Combine(Path.GetTempPath(), "softbox-legacy-language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, """{"SchemaVersion":1,"DiscoveryIntervalSeconds":15,"UiScale":0.9}""");
            var loaded = await new PhoneLabSettingsService(path).LoadAsync();
            Assert.Equal("ar", loaded.UiLanguage);
            Assert.Equal(15, loaded.DiscoveryIntervalSeconds);
            Assert.Equal(0.9, loaded.UiScale);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SaveRejectsUnsupportedUiLanguage()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-invalid-language-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new PhoneLabSettingsService(path);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                store.SaveAsync(new PhoneLabSettings(UiLanguage: "fr")));
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
    [Fact]
    public async Task SaveRejectsAirPlayReceiverPathsLongerThanTheSafeBound()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-invalid-airplay-path-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new PhoneLabSettingsService(path);
            var invalid = new PhoneLabSettings(AirPlayReceiverExecutablePath: new string('x', 2049));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(invalid));
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveRejectsFfmpegExecutablePathsLongerThanTheSafeBound()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-invalid-ffmpeg-path-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new PhoneLabSettingsService(path);
            var invalid = new PhoneLabSettings(FfmpegExecutablePath: new string((char)120, 2049));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(invalid));
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveRejectsIPhoneToolsDirectoryLongerThanTheSafeBound()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-invalid-iphone-tools-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new PhoneLabSettingsService(path);
            var invalid = new PhoneLabSettings(IPhoneToolsDirectory: new string('x', 2049));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(invalid));
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

}
