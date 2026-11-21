using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>Controls the authorized Android Agent only through an ADB-forwarded loopback socket.</summary>
public sealed class AgentBridgeClient(
    AdbClient adb,
    DeviceDiagnosticEngineSettingsStore? settingsStore = null)
{
    private readonly DeviceDiagnosticEngineSettingsStore _settingsStore =
        settingsStore ?? new DeviceDiagnosticEngineSettingsStore();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly HttpClient CommandHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<AgentSnapshot?> ReadSnapshotAsync(string serial, CancellationToken cancellationToken = default)
    {
        await adb.ForwardAgentPortAsync(serial);
        using var response = await Http.GetAsync("http://127.0.0.1:27831/snapshot", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<AgentSnapshot>(stream, JsonOptions, cancellationToken);
    }

    public async Task<string> ReadTelemetryAsync(string serial, CancellationToken cancellationToken = default)
    {
        await adb.ForwardAgentPortAsync(serial);
        using var response = await Http.GetAsync("http://127.0.0.1:27831/telemetry", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task SendCommandAsync(string serial, string command, CancellationToken cancellationToken = default)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "display", "touch", "speaker", "earpiece", "microphone", "flashlight",
            "vibration", "camera", "sensors", "hardware",
            "battery_capacity_start", "battery_capacity_finish",
            "bluetooth", "wifi", "nfc", "gps", "fingerprint", "physical_buttons", "sim"
        };
        if (!allowed.Contains(command)) throw new ArgumentException("Unsupported PhoneLab Agent command.", nameof(command));

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Settings.AgentCommandTimeoutSeconds));
        await adb.ForwardAgentPortAsync(serial);
        using var response = await CommandHttp.PostAsJsonAsync(
            "http://127.0.0.1:27831/command", new { command }, timeout.Token);
        response.EnsureSuccessStatusCode();
    }
}
