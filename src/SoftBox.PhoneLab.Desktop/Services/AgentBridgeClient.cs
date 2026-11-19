using System.Net;
using System.Net.Http;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>Reads the foreground PhoneLab Agent over an ADB-forwarded loopback TCP port.</summary>
public sealed class AgentBridgeClient(AdbClient adb)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    public async Task<AgentSnapshot?> ReadSnapshotAsync(string serial, CancellationToken cancellationToken = default)
    {
        await adb.ForwardAgentPortAsync(serial);
        using var response = await Http.GetAsync("http://127.0.0.1:27831/snapshot", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<AgentSnapshot>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
    }
}
