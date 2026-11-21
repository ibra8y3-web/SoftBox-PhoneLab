using System.IO;
using SoftBox.PhoneLab.Core.Network;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record AndroidNetworkSnapshot(
    string DeviceSerial,
    string InterfaceAddressOutput,
    string RouteOutput,
    DateTimeOffset CapturedAtUtc);

public sealed record AndroidNetworkProbeSnapshot(
    string DeviceSerial,
    string Target,
    string PrimaryDnsServer,
    string SecondaryDnsServer,
    string PingOutput,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Reads only public network-interface addresses and routing information exposed by Android's
/// standard ip utility through an already-authorized ADB connection.
/// </summary>
public sealed class AndroidNetworkDiagnosticsService(IAdbCommandRunner adb)
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(12);
    private const int MaximumOutputCharacters = 64 * 1024;

    public async Task<AndroidNetworkSnapshot> InspectAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف جهاز ADB غير صالح.", nameof(serial));
        if (!adb.IsAvailable)
            throw new FileNotFoundException("أداة ADB غير متاحة في تثبيت SoftBox.");

        var addressOutput = await RunForDeviceAsync(
            serial, ["shell", "ip", "address", "show"], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var routeOutput = await RunForDeviceAsync(
            serial, ["shell", "ip", "route", "show"], cancellationToken);

        if (string.IsNullOrWhiteSpace(addressOutput) && string.IsNullOrWhiteSpace(routeOutput))
            throw new InvalidOperationException("لم يُرجع Android بيانات واجهات أو مسارات شبكة قابلة للقراءة.");

        return new AndroidNetworkSnapshot(
            serial,
            Limit(addressOutput),
            Limit(routeOutput),
            DateTimeOffset.UtcNow);
    }

    public async Task<AndroidNetworkProbeSnapshot> ProbeHostAsync(
        string serial,
        string host,
        CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف جهاز ADB غير صالح.", nameof(serial));
        if (!adb.IsAvailable)
            throw new FileNotFoundException("أداة ADB غير متاحة في تثبيت SoftBox.");
        if (!NetworkDiagnosticsService.IsValidHost(host))
            throw new ArgumentException("أدخل اسم نطاق أو عنوان IP صحيحًا فقط.", nameof(host));

        var target = host.Trim();
        var primaryDns = await RunForDeviceAsync(
            serial, ["shell", "getprop", "net.dns1"], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var secondaryDns = await RunForDeviceAsync(
            serial, ["shell", "getprop", "net.dns2"], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        string pingOutput;
        try
        {
            // One ICMP echo only. The validated target is passed as a single argument;
            // SoftBox never concatenates it into a free-form shell command.
            pingOutput = await RunForDeviceAsync(
                serial,
                ["shell", "ping", "-c", "1", "-W", "3", target],
                cancellationToken,
                TimeSpan.FromSeconds(8));
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            // Keep DNS observations even when Android cannot run ping or ICMP is unavailable.
            pingOutput = "PING RESULT UNAVAILABLE: " + ex.Message;
        }

        return new AndroidNetworkProbeSnapshot(
            serial,
            target,
            Limit(primaryDns.Trim()),
            Limit(secondaryDns.Trim()),
            Limit(pingOutput.Trim()),
            DateTimeOffset.UtcNow);
    }

    private async Task<string> RunForDeviceAsync(
        string serial,
        IReadOnlyList<string> command,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var args = new List<string> { "-s", serial };
        args.AddRange(command);
        return await adb.RunAsync(args, cancellationToken, timeout ?? CommandTimeout);
    }

    private static string Limit(string value)
        => value.Length <= MaximumOutputCharacters
            ? value
            : value[..MaximumOutputCharacters] + Environment.NewLine + "[Output truncated at 64 Ki characters.]";
}
