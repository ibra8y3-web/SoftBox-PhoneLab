using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class WirelessAdbPairingServiceTests
{
    [Theory]
    [InlineData("192.168.1.25", 37099, "192.168.1.25:37099")]
    [InlineData("127.0.0.1", 1, "127.0.0.1:1")]
    [InlineData("2001:db8::5", 4567, "[2001:db8::5]:4567")]
    public void FormatEndpointAcceptsIpAddressesAndValidPorts(string host, int port, string expected)
        => Assert.Equal(expected, WirelessAdbPairingService.FormatEndpoint(host, port));

    [Theory]
    [InlineData("", 37099)]
    [InlineData("phone.local", 37099)]
    [InlineData("192.168.1.25", 0)]
    [InlineData("192.168.1.25", 65536)]
    public void FormatEndpointRejectsInvalidHostOrPort(string host, int port)
        => Assert.ThrowsAny<ArgumentException>(() => WirelessAdbPairingService.FormatEndpoint(host, port));

    [Theory]
    [InlineData("123456", true)]
    [InlineData("000001", true)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("12A456", false)]
    [InlineData("١٢٣٤٥٦", false)]
    [InlineData(null, false)]
    public void PairingCodeMustBeSixAsciiDigits(string? code, bool expected)
        => Assert.Equal(expected, WirelessAdbPairingService.IsValidPairingCode(code));

    [Fact]
    public async Task PairAndConnectUsesSeparatePairingAndConnectionPorts()
    {
        var adb = new FakeAdbCommandRunner(
            "Successfully paired to 192.168.1.25:37099",
            "connected to 192.168.1.25:44123");
        var service = new WirelessAdbPairingService(adb);

        var result = await service.PairAndConnectAsync(
            "192.168.1.25", 37099, "123456", 44123);

        Assert.Equal("192.168.1.25", result.Host);
        Assert.Equal("192.168.1.25:37099", result.PairingEndpoint);
        Assert.Equal("192.168.1.25:44123", result.ConnectEndpoint);
        Assert.Collection(adb.Calls,
            call => Assert.Equal(new[] { "pair", "192.168.1.25:37099", "123456" }, call),
            call => Assert.Equal(new[] { "connect", "192.168.1.25:44123" }, call));
    }

    [Fact]
    public async Task PairAndConnectAcceptsSeparatePairingAndConnectionHosts()
    {
        var adb = new FakeAdbCommandRunner(
            "Successfully paired to 192.168.1.25:37099",
            "connected to 192.168.1.26:44123");
        var service = new WirelessAdbPairingService(adb);

        var result = await service.PairAndConnectAsync(
            "192.168.1.25", 37099, "123456", "192.168.1.26", 44123);

        Assert.Equal("192.168.1.25:37099", result.PairingEndpoint);
        Assert.Equal("192.168.1.26:44123", result.ConnectEndpoint);
        Assert.Equal("192.168.1.25", result.PairingHost);
        Assert.Equal("192.168.1.26", result.ConnectionHost);
        Assert.Collection(adb.Calls,
            call => Assert.Equal(new[] { "pair", "192.168.1.25:37099", "123456" }, call),
            call => Assert.Equal(new[] { "connect", "192.168.1.26:44123" }, call));
    }

    [Fact]
    public async Task FailedPairingNeverAttemptsConnection()
    {
        var adb = new FakeAdbCommandRunner("failed: incorrect pairing code");
        var service = new WirelessAdbPairingService(adb);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PairAndConnectAsync("192.168.1.25", 37099, "123456", 44123));

        Assert.Single(adb.Calls);
        Assert.Equal("pair", adb.Calls[0][0]);
    }

    [Fact]
    public async Task FailedConnectionIsReportedAfterSuccessfulPairing()
    {
        var adb = new FakeAdbCommandRunner(
            "Successfully paired to 192.168.1.25:37099",
            "failed to connect to 192.168.1.25:44123");
        var service = new WirelessAdbPairingService(adb);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PairAndConnectAsync("192.168.1.25", 37099, "123456", 44123));

        Assert.Contains("نجح الاقتران", error.Message);
        Assert.Equal(2, adb.Calls.Count);
    }

    [Fact]
    public async Task InvalidPairingCodeDoesNotInvokeAdb()
    {
        var adb = new FakeAdbCommandRunner();
        var service = new WirelessAdbPairingService(adb);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.PairAndConnectAsync("192.168.1.25", 37099, "not-code", 44123));

        Assert.Empty(adb.Calls);
    }

    [Fact]
    public async Task CancellationAfterPairingPreventsConnectionCommand()
    {
        using var cancellation = new CancellationTokenSource();
        var adb = new FakeAdbCommandRunner("Successfully paired to 192.168.1.25:37099")
        {
            OnCommand = count =>
            {
                if (count == 1)
                    cancellation.Cancel();
            }
        };
        var service = new WirelessAdbPairingService(adb);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.PairAndConnectAsync("192.168.1.25", 37099, "123456", 44123, cancellation.Token));

        Assert.Single(adb.Calls);
    }

    private sealed class FakeAdbCommandRunner(params string[] outputs) : IAdbCommandRunner
    {
        private readonly Queue<string> _outputs = new(outputs);
        public List<string[]> Calls { get; } = [];
        public Action<int>? OnCommand { get; init; }
        public bool IsAvailable => true;

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
