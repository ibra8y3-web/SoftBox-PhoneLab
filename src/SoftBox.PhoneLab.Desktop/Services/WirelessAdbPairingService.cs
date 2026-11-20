using System.Net;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record WirelessAdbPairingResult(
    string PairingHost,
    string ConnectionHost,
    string PairingEndpoint,
    string ConnectEndpoint,
    string PairOutput,
    string ConnectOutput);

/// <summary>
/// Uses Android Platform-Tools' authenticated wireless debugging pairing protocol.
/// The pairing code is passed to a single ADB process invocation and is never stored by SoftBox.
/// </summary>
public sealed class WirelessAdbPairingService(AdbClient adb)
{
    public static string FormatEndpoint(string host, int port)
    {
        if (!IPAddress.TryParse((host ?? "").Trim(), out var address))
            throw new ArgumentException("أدخل عنوان IP صالحًا كما يظهر في شاشة Wireless debugging.", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "منفذ الشبكة يجب أن يكون بين 1 و65535.");

        var normalizedHost = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]"
            : address.ToString();
        return $"{normalizedHost}:{port}";
    }

    public static bool IsValidPairingCode(string? pairingCode)
        => pairingCode is { Length: 6 } && pairingCode.All(char.IsAsciiDigit);

    // Kept for callers that use a single address for both Android screens.
    public Task<WirelessAdbPairingResult> PairAndConnectAsync(
        string host,
        int pairingPort,
        string pairingCode,
        int connectionPort,
        CancellationToken cancellationToken = default)
        => PairAndConnectAsync(host, pairingPort, pairingCode, host, connectionPort, cancellationToken);

    public async Task<WirelessAdbPairingResult> PairAndConnectAsync(
        string pairingHost,
        int pairingPort,
        string pairingCode,
        string connectionHost,
        int connectionPort,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidPairingCode(pairingCode))
            throw new ArgumentException("رمز الاقتران يجب أن يتكون من 6 أرقام ASCII كما يظهر على الهاتف.", nameof(pairingCode));

        var pairingEndpoint = FormatEndpoint(pairingHost, pairingPort);
        var connectEndpoint = FormatEndpoint(connectionHost, connectionPort);
        var normalizedPairingHost = IPAddress.Parse(pairingHost.Trim()).ToString();
        var normalizedConnectionHost = IPAddress.Parse(connectionHost.Trim()).ToString();

        var pairOutput = await adb.RunAsync(
            ["pair", pairingEndpoint, pairingCode],
            cancellationToken,
            TimeSpan.FromSeconds(35));

        if (pairOutput.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            (!pairOutput.Contains("successfully paired", StringComparison.OrdinalIgnoreCase) &&
             !pairOutput.Contains("already paired", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("لم يؤكد ADB نجاح الاقتران. تحقق من الرمز ومنفذ الاقتران ووقت صلاحية الرمز. مخرجات ADB: " + pairOutput);

        cancellationToken.ThrowIfCancellationRequested();
        var connectOutput = await adb.RunAsync(
            ["connect", connectEndpoint],
            cancellationToken,
            TimeSpan.FromSeconds(25));

        if (connectOutput.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            (!connectOutput.Contains("connected to", StringComparison.OrdinalIgnoreCase) &&
             !connectOutput.Contains("already connected", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "نجح الاقتران لكن لم يؤكد ADB اتصال الجهاز على منفذ Wireless debugging. مخرجات ADB: " + connectOutput);

        return new WirelessAdbPairingResult(
            normalizedPairingHost,
            normalizedConnectionHost,
            pairingEndpoint,
            connectEndpoint,
            pairOutput.Trim(),
            connectOutput.Trim());
    }
}
