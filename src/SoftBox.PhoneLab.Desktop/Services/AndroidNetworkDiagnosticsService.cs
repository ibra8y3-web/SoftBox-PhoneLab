using System.IO;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record AndroidNetworkSnapshot(
    string DeviceSerial,
    string InterfaceAddressOutput,
    string RouteOutput,
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

    private async Task<string> RunForDeviceAsync(
        string serial,
        IReadOnlyList<string> command,
        CancellationToken cancellationToken)
    {
        var args = new List<string> { "-s", serial };
        args.AddRange(command);
        return await adb.RunAsync(args, cancellationToken, CommandTimeout);
    }

    private static string Limit(string value)
        => value.Length <= MaximumOutputCharacters
            ? value
            : value[..MaximumOutputCharacters] + Environment.NewLine + "[Output truncated at 64 Ki characters.]";
}
