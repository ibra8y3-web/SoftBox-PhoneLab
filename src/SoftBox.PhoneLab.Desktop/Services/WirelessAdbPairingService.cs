using System.Net;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record WirelessAdbPairingResult(
    string Host,
    string PairingEndpoint,
    string ConnectEndpoint,
    string PairOutput,
    string ConnectOutput);

/// <summary>
/// Uses Android Platform-Tools' authenticated wireless debugging pairing protocol.
/// Pairing codes are validated and passed only to the adb process; they are never persisted.
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

    public async Task<WirelessAdbPairingResult> PairAndConnectAsync(
        string host,
        int pairingPort,
        string pairingCode,
        int connectionPort,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidPairingCode(pairingCode))
            throw new ArgumentException("رمز الاقتران يجب أن يتكون من 6 أرقام كما يظهر على الهاتف.", nameof(pairingCode));

        var pairingEndpoint = FormatEndpoint(host, pairingPort);
        var connectEndpoint = FormatEndpoint(host, connectionPort);

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
            IPAddress.Parse(host.Trim()).ToString(),
            pairingEndpoint,
            connectEndpoint,
            pairOutput.Trim(),
            connectOutput.Trim());
    }
}
