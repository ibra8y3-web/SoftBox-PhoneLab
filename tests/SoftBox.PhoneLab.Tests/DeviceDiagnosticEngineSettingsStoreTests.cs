using System.Text.Json;
using SoftBox.PhoneLab.Core.Compatibility;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceDiagnosticEngineSettingsStoreTests
{
    [Fact]
    public async Task MissingSettingsFileUsesSafeDefaults()
    {
        var path = TempPath();
        try
        {
            var result = await new DeviceDiagnosticEngineSettingsStore(path).LoadAsync();
            Assert.Empty(result.Warnings);
            Assert.Equal(new DiagnosticEngineSettings(), result.Settings);
        }
        finally { DeleteIfPresent(path); }
    }

    [Fact]
    public async Task SavesAndReloadsCustomValidatedSettings()
    {
        var path = TempPath();
        var settings = new DiagnosticEngineSettings
        {
            AdbCommandTimeoutSeconds = 35,
            AgentCommandTimeoutSeconds = 120,
            MaximumOutputBytes = 524_288,
            BatteryReviewTemperatureCelsius = 43.5,
            BatteryCriticalTemperatureCelsius = 58.5
        };
        try
        {
            var store = new DeviceDiagnosticEngineSettingsStore(path);
            await store.SaveAsync(settings);
            var result = await store.LoadAsync();

            Assert.Empty(result.Warnings);
            Assert.Equal(settings, result.Settings);
        }
        finally { DeleteIfPresent(path); }
    }

    [Fact]
    public async Task InvalidLocalSettingsFallBackToDefaultsWithoutCrashingDiagnostics()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            adbCommandTimeoutSeconds = 999,
            agentTestTimeoutSeconds = 90,
            maximumOutputBytes = 262144,
            batteryReviewTemperatureCelsius = 45.0,
            batteryCriticalTemperatureCelsius = 55.0
        }));
        try
        {
            var result = await new DeviceDiagnosticEngineSettingsStore(path).LoadAsync();
            Assert.Equal(new DiagnosticEngineSettings(), result.Settings);
            Assert.NotEmpty(result.Warnings);
        }
        finally { DeleteIfPresent(path); }
    }

    [Fact]
    public async Task SaveRejectsUnboundedSettings()
    {
        var path = TempPath();
        try
        {
            var invalid = new DiagnosticEngineSettings { AdbCommandTimeoutSeconds = 999 };
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                new DeviceDiagnosticEngineSettingsStore(path).SaveAsync(invalid));
            Assert.False(File.Exists(path));
        }
        finally { DeleteIfPresent(path); }
    }

    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), "softbox-diagnostic-settings-" + Guid.NewGuid().ToString("N"), "settings.json");

    private static void DeleteIfPresent(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
