using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SoftBox.PhoneLab.Core.Network;

public sealed record NetworkInterfaceSnapshot(
    string Name,
    string Description,
    string Status,
    string Type,
    IReadOnlyList<string> Ipv4Addresses,
    IReadOnlyList<string> Ipv6Addresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers);

public sealed record NetworkProbeResult(
    string Host,
    IReadOnlyList<string> ResolvedAddresses,
    string PingAddress,
    string PingStatus,
    long? RoundtripMilliseconds,
    int? TimeToLive);

/// <summary>
/// Reads the local Windows host network configuration and probes one explicitly selected host.
/// No port scanning, device-private data access, or silent background network requests are performed.
/// </summary>
public sealed class NetworkDiagnosticsService
{
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(3);

    public IReadOnlyList<NetworkInterfaceSnapshot> GetLocalInterfaces()
    {
        var snapshots = new List<NetworkInterfaceSnapshot>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var ipv4 = properties.UnicastAddresses
                .Select(item => item.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(address => address, StringComparer.Ordinal)
                .ToArray();

            var ipv6 = properties.UnicastAddresses
                .Select(item => item.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetworkV6 &&
                                  !address.IsIPv6LinkLocal)
                .Select(address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(address => address, StringComparer.Ordinal)
                .ToArray();

            var gateways = properties.GatewayAddresses
                .Select(item => item.Address)
                .Where(address => !IPAddress.Any.Equals(address) && !IPAddress.IPv6Any.Equals(address))
                .Select(address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var dnsServers = properties.DnsAddresses
                .Select(address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            snapshots.Add(new NetworkInterfaceSnapshot(
                networkInterface.Name,
                networkInterface.Description,
                networkInterface.OperationalStatus.ToString(),
                networkInterface.NetworkInterfaceType.ToString(),
                ipv4,
                ipv6,
                gateways,
                dnsServers));
        }

        return snapshots
            .OrderByDescending(item => item.Status.Equals("Up", StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<NetworkProbeResult> ProbeHostAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidHost(host))
            throw new ArgumentException("أدخل اسم نطاق أو عنوان IP صحيحًا فقط، دون رابط أو مسار.", nameof(host));

        var normalizedHost = host.Trim();
        using var dnsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        dnsCancellation.CancelAfter(DnsTimeout);

        IPAddress[] resolved;
        try
        {
            resolved = await Dns.GetHostAddressesAsync(normalizedHost, dnsCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("انتهت مهلة حل اسم النطاق DNS بعد 5 ثوانٍ.");
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"تعذر حل اسم النطاق DNS: {ex.Message}", ex);
        }

        if (resolved.Length == 0)
            throw new InvalidOperationException("لم يُرجع DNS أي عنوان لهذا المضيف.");

        var pingAddress = resolved.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
                          ?? resolved[0];

        using var pingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pingCancellation.CancelAfter(PingTimeout + TimeSpan.FromSeconds(1));
        using var ping = new Ping();

        try
        {
            var reply = await ping.SendPingAsync(pingAddress, PingTimeout, pingCancellation.Token);
            return new NetworkProbeResult(
                normalizedHost,
                resolved.Select(address => address.ToString()).ToArray(),
                pingAddress.ToString(),
                reply.Status.ToString(),
                reply.Status == IPStatus.Success ? reply.RoundtripTime : null,
                reply.Status == IPStatus.Success ? reply.Options?.Ttl : null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NetworkProbeResult(
                normalizedHost,
                resolved.Select(address => address.ToString()).ToArray(),
                pingAddress.ToString(),
                "TimedOut",
                null,
                null);
        }
        catch (PingException ex)
        {
            return new NetworkProbeResult(
                normalizedHost,
                resolved.Select(address => address.ToString()).ToArray(),
                pingAddress.ToString(),
                "Unavailable: " + ex.InnerException?.Message ?? "ICMP unavailable",
                null,
                null);
        }
    }

    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var value = host.Trim();
        if (value.Length > 253 ||
            value.Any(character => char.IsWhiteSpace(character) ||
                                   character is '/' or '\\' or '?' or '#' or '@'))
            return false;

        if (IPAddress.TryParse(value, out _))
            return true;

        return Uri.CheckHostName(value) == UriHostNameType.Dns &&
               value.Contains('.', StringComparison.Ordinal);
    }
}
