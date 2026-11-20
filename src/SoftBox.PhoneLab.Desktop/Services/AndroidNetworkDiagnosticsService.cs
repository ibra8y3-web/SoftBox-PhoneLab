using System.Net;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record AndroidNetworkDiagnosticsSnapshot(
    string DeviceSerial,
    DateTimeOffset CollectedAtUtc,
    IReadOnlyList<string> Ipv4Addresses,
    IReadOnlyList<string> Ipv6Addresses,
    string? DefaultGateway,
    string? DefaultInterface,
    bool? WifiEnabled,
    bool? AirplaneModeEnabled,
    IReadOnlyList<string> UnavailableCommands);

/// <summary>
/// Read-only Android network observations through the selected authorized ADB transport.
/// It never reads Wi-Fi SSIDs, passwords, app traffic, or arbitrary shell input.
/// </summary>
public sealed class AndroidNetworkDiagnosticsService(AdbClient adb)
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(8);

    public async Task<AndroidNetworkDiagnosticsSnapshot> InspectAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));

        var commands = new (string Id, string[] Args)[]
        {
            ("default-route", ["shell", "ip", "route", "show", "default"]),
            ("global-addresses", ["shell", "ip", "-o", "addr", "show", "scope", "global"]),
            ("wifi-state", ["shell", "settings", "get", "global", "wifi_on"]),
            ("airplane-state", ["shell", "settings", "get", "global", "airplane_mode_on"])
        };

        var results = await Task.WhenAll(commands.Select(async command =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var output = await adb.RunForDeviceAsync(
                    serial, command.Args, cancellationToken, CommandTimeout);
                return new CommandResult(command.Id, output.Trim(), null);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new CommandResult(command.Id, "", ex.Message);
            }
        }));

        string Output(string id) => results.First(x => x.Id == id).Output;
        var unavailable = results
            .Where(x => x.Error is not null || string.IsNullOrWhiteSpace(x.Output))
            .Select(x => x.Id + (x.Error is null ? ": no data returned" : ": " + x.Error))
            .ToArray();

        return AndroidNetworkDiagnosticsParser.Parse(
            serial,
            Output("default-route"),
            Output("global-addresses"),
            Output("wifi-state"),
            Output("airplane-state"),
            unavailable);
    }

    private sealed record CommandResult(string Id, string Output, string? Error);
}

public static class AndroidNetworkDiagnosticsParser
{
    private static readonly Regex AddressLine = new(
        @"^\s*\d+:\s+(?<interface>[^\s]+)\s+(?<family>inet6?)\s+(?<address>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static AndroidNetworkDiagnosticsSnapshot Parse(
        string serial,
        string defaultRouteOutput,
        string addressOutput,
        string wifiStateOutput,
        string airplaneStateOutput,
        IReadOnlyList<string>? unavailableCommands = null)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));

        var ipv4 = new List<string>();
        var ipv6 = new List<string>();

        foreach (var line in (addressOutput ?? "").Split(['\\r', '\\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = AddressLine.Match(line);
            if (!match.Success)
                continue;

            var token = match.Groups["address"].Value;
            var slash = token.IndexOf('/');
            var addressText = slash >= 0 ? token[..slash] : token;
            if (!IPAddress.TryParse(addressText, out var address) ||
                IPAddress.IsLoopback(address) ||
                address.IsIPv6LinkLocal ||
                address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.IPv6Any))
                continue;

            var item = match.Groups["interface"].Value + ": " + token;
            var target = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? ipv4 : ipv6;
            if (!target.Contains(item, StringComparer.Ordinal))
                target.Add(item);
        }

        string? gateway = null;
        string? networkInterface = null;
        foreach (var line in (defaultRouteOutput ?? "").Split(['\\r', '\\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || !tokens[0].Equals("default", StringComparison.OrdinalIgnoreCase))
                continue;

            for (var i = 1; i + 1 < tokens.Length; i++)
            {
                if (tokens[i].Equals("via", StringComparison.OrdinalIgnoreCase) &&
                    IPAddress.TryParse(tokens[i + 1], out var parsedGateway))
                    gateway = parsedGateway.ToString();

                if (tokens[i].Equals("dev", StringComparison.OrdinalIgnoreCase))
                    networkInterface = tokens[i + 1];
            }

            if (gateway is not null || networkInterface is not null)
                break;
        }

        return new AndroidNetworkDiagnosticsSnapshot(
            serial,
            DateTimeOffset.UtcNow,
            ipv4.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            ipv6.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            gateway,
            networkInterface,
            ParseToggle(wifiStateOutput),
            ParseToggle(airplaneStateOutput),
            unavailableCommands?.ToArray() ?? []);
    }

    private static bool? ParseToggle(string? value)
    {
        var normalized = (value ?? "").Trim();
        return normalized switch
        {
            "1" => true,
            "0" => false,
            _ when bool.TryParse(normalized, out var parsed) => parsed,
            _ => null
        };
    }
}
