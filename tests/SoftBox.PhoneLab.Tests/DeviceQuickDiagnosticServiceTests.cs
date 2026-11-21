using SoftBox.PhoneLab.Core.Compatibility;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceQuickDiagnosticServiceTests
{
    [Fact]
    public async Task DoesNotClaimAdbAuthorizationWhenEveryReadOnlyCommandFails()
    {
        var path = TempSettingsPath();
        var runner = new FakeAdbCommandRunner(_ => throw new UnauthorizedAccessException("device unauthorized"));
        try
        {
            var store = new DeviceDiagnosticEngineSettingsStore(path);
            var report = await new DeviceQuickDiagnosticService(
                new AndroidDeviceToolsService(runner), store)
                .RunAsync("SOFTBOX-TEST-01", null, null, []);

            Assert.All(report.Steps, step => Assert.False(step.Success));
            Assert.False(Assert.Single(report.EnginePlan.Procedures,
                procedure => procedure.Id == "android-properties").CanExecute);
            Assert.False(Assert.Single(report.EnginePlan.Procedures,
                procedure => procedure.Id == "agent-hardware-diagnostics").CanExecute);
            Assert.Contains(report.Advice.Advice, advice => advice.Id == "adb-not-authorized");
            Assert.Contains(report.Limitations, item => item.Contains("لم ينجح أي أمر ADB", StringComparison.Ordinal));
        }
        finally { DeleteSettingsPath(path); }
    }

    [Fact]
    public async Task SuccessfulReadOnlyCommandIsRequiredBeforeAdbAndAgentPrerequisitesAreMarkedReady()
    {
        var path = TempSettingsPath();
        var runner = new FakeAdbCommandRunner(_ => "read-only-result");
        try
        {
            var settings = new DiagnosticEngineSettings
            {
                AdbCommandTimeoutSeconds = 27,
                AgentCommandTimeoutSeconds = 90,
                MaximumReportOutputCharacters = 16_384
            };
            await new DeviceDiagnosticEngineSettingsStore(path).SaveAsync(settings);

            var report = await new DeviceQuickDiagnosticService(
                new AndroidDeviceToolsService(runner), new DeviceDiagnosticEngineSettingsStore(path))
                .RunAsync("SOFTBOX-TEST-02", null, null, []);

            Assert.All(report.Steps, step => Assert.True(step.Success));
            Assert.True(Assert.Single(report.EnginePlan.Procedures,
                procedure => procedure.Id == "android-properties").CanExecute);
            Assert.All(runner.Timeouts, timeout => Assert.Equal(TimeSpan.FromSeconds(27), timeout));
        }
        finally { DeleteSettingsPath(path); }
    }

    [Fact]
    public async Task EnforcesOneAggregateOutputBudgetAcrossAllReadOnlyCommands()
    {
        var path = TempSettingsPath();
        var runner = new FakeAdbCommandRunner(_ => new string('x', 6_000));
        try
        {
            var settings = new DiagnosticEngineSettings
            {
                AdbCommandTimeoutSeconds = 20,
                AgentCommandTimeoutSeconds = 90,
                MaximumReportOutputCharacters = 16_384
            };
            await new DeviceDiagnosticEngineSettingsStore(path).SaveAsync(settings);

            var report = await new DeviceQuickDiagnosticService(
                new AndroidDeviceToolsService(runner), new DeviceDiagnosticEngineSettingsStore(path))
                .RunAsync("SOFTBOX-TEST-03", null, null, []);

            var savedOutputCharacters = report.Steps.Sum(step => step.Output.Length);
            Assert.True(savedOutputCharacters <= settings.MaximumReportOutputCharacters);
            Assert.Contains(report.Limitations, item => item.Contains("حد إجمالي", StringComparison.Ordinal));
            Assert.Equal(5, runner.Timeouts.Count);
        }
        finally { DeleteSettingsPath(path); }
    }

    private static string TempSettingsPath()
        => Path.Combine(Path.GetTempPath(), "softbox-quick-diagnostic-" + Guid.NewGuid().ToString("N"), "settings.json");

    private static void DeleteSettingsPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private sealed class FakeAdbCommandRunner(Func<IReadOnlyList<string>, string> responder) : IAdbCommandRunner
    {
        public bool IsAvailable => true;
        public List<TimeSpan?> Timeouts { get; } = [];

        public Task<string> RunAsync(
            IReadOnlyList<string> args,
            CancellationToken cancellationToken,
            TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Timeouts.Add(commandTimeout);
            return Task.FromResult(responder(args));
        }
    }
}
