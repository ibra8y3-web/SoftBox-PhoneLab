using System.IO;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidNetworkDiagnosticsServiceTests
{
    [Fact]
    public async Task ReadsRealAndroidInterfaceAndRouteCommandsForTheSelectedSerial()
    {
        var adb = new FakeAdbCommandRunner(
            "2: wlan0: <UP> mtu 1500\n    inet 192.168.1.42/24 scope global wlan0",
            "default via 192.168.1.1 dev wlan0");
        var service = new AndroidNetworkDiagnosticsService(adb);

        var result = await service.InspectAsync("R58M123456A");

        Assert.Equal("R58M123456A", result.DeviceSerial);
        Assert.Contains("192.168.1.42/24", result.InterfaceAddressOutput);
        Assert.Contains("default via 192.168.1.1", result.RouteOutput);
        Assert.True(result.CapturedAtUtc <= DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Collection(adb.Calls,
            call => Assert.Equal(new[] { "-s", "R58M123456A", "shell", "ip", "address", "show" }, call),
            call => Assert.Equal(new[] { "-s", "R58M123456A", "shell", "ip", "route", "show" }, call));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad serial with spaces")]
    [InlineData("-s")]
    public async Task RejectsInvalidDeviceSerialBeforeRunningCommands(string serial)
    {
        var adb = new FakeAdbCommandRunner();
        var service = new AndroidNetworkDiagnosticsService(adb);

        await Assert.ThrowsAsync<ArgumentException>(() => service.InspectAsync(serial));

        Assert.Empty(adb.Calls);
    }

    [Fact]
    public async Task MissingAdbIsReportedAsUnavailable()
    {
        var adb = new FakeAdbCommandRunner { IsAvailableOverride = false };
        var service = new AndroidNetworkDiagnosticsService(adb);

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.InspectAsync("R58M123456A"));

        Assert.Empty(adb.Calls);
    }

    [Fact]
    public async Task CancellationAfterAddressReadStopsBeforeRouteRead()
    {
        using var cancellation = new CancellationTokenSource();
        var adb = new FakeAdbCommandRunner("address output")
        {
            OnCommand = count =>
            {
                if (count == 1)
                    cancellation.Cancel();
            }
        };
        var service = new AndroidNetworkDiagnosticsService(adb);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.InspectAsync("R58M123456A", cancellation.Token));

        Assert.Single(adb.Calls);
    }

    [Fact]
    public async Task EmptyNetworkResultsAreNotReportedAsSuccess()
    {
        var adb = new FakeAdbCommandRunner("", "");
        var service = new AndroidNetworkDiagnosticsService(adb);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.InspectAsync("R58M123456A"));
    }

    [Fact]
    public async Task ProbesDnsPropertiesAndSendsOnePingFromTheSelectedAndroidDevice()
    {
        var adb = new FakeAdbCommandRunner(
            "device",
            "192.168.1.1",
            "8.8.8.8",
            "PING example.com (93.184.216.34): 56 data bytes");
        var service = new AndroidNetworkDiagnosticsService(adb);

        var result = await service.ProbeHostAsync("R58M123456A", "example.com");

        Assert.Equal("R58M123456A", result.DeviceSerial);
        Assert.Equal("example.com", result.Target);
        Assert.Equal("192.168.1.1", result.PrimaryDnsServer);
        Assert.Equal("8.8.8.8", result.SecondaryDnsServer);
        Assert.Contains("93.184.216.34", result.PingOutput);
        Assert.Collection(adb.Calls,
            call => Assert.Equal(new[] { "-s", "R58M123456A", "get-state" }, call),
            call => Assert.Equal(new[] { "-s", "R58M123456A", "shell", "getprop", "net.dns1" }, call),
            call => Assert.Equal(new[] { "-s", "R58M123456A", "shell", "getprop", "net.dns2" }, call),
            call => Assert.Equal(new[] { "-s", "R58M123456A", "shell", "ping", "-c", "1", "-W", "3", "example.com" }, call));
    }

    [Fact]
    public async Task RefusesNetworkProbeWhenDeviceIsNoLongerAuthorized()
    {
        var adb = new FakeAdbCommandRunner("unauthorized");
        var service = new AndroidNetworkDiagnosticsService(adb);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ProbeHostAsync("R58M123456A", "example.com"));

        Assert.Collection(adb.Calls,
            call => Assert.Equal(new[] { "-s", "R58M123456A", "get-state" }, call));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("bad host")]
    public async Task RejectsUnsafeAndroidNetworkProbeTargetBeforeAdb(string target)
    {
        var adb = new FakeAdbCommandRunner();
        var service = new AndroidNetworkDiagnosticsService(adb);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ProbeHostAsync("R58M123456A", target));

        Assert.Empty(adb.Calls);
    }

    [Fact]
    public async Task RejectsInvalidSerialBeforeAndroidNetworkProbe()
    {
        var adb = new FakeAdbCommandRunner();
        var service = new AndroidNetworkDiagnosticsService(adb);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ProbeHostAsync("bad serial", "example.com"));

        Assert.Empty(adb.Calls);
    }

    private sealed class FakeAdbCommandRunner(params string[] outputs) : IAdbCommandRunner
    {
        private readonly Queue<string> _outputs = new(outputs);
        public List<string[]> Calls { get; } = [];
        public Action<int>? OnCommand { get; init; }
        public bool? IsAvailableOverride { get; init; }
        public bool IsAvailable => IsAvailableOverride ?? true;

        public Task<string> RunAsync(
            IReadOnlyList<string> args,
            CancellationToken cancellationToken,
            TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(args.ToArray());
            OnCommand?.Invoke(Calls.Count);
            return Task.FromResult(_outputs.Count > 0 ? _outputs.Dequeue() : "");
        }
    }
}
