using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AgentSnapshotTelemetryTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void ScheduledBatterySampleAndAgentTelemetryDeserializeFromAgentSnapshot()
    {
        const string json = """
        {
          "scheduledBatteryLatest": {
            "timestampUtc": "2026-11-21T12:00:00Z",
            "batteryPercent": 83,
            "voltageMillivolts": 4100,
            "temperatureCelsius": 32.4,
            "chargeCounterMicroAh": 3240000,
            "currentNowMicroA": -250000,
            "plugged": true,
            "cycleCount": 250
          },
          "scheduledBatterySamples": 24,
          "agentTelemetrySummary": {
            "eventCountSampled": 12,
            "crashCountSampled": 1,
            "lastEvent": "scheduled_battery_sample",
            "lastEventAtUtc": "2026-11-21T12:00:00Z",
            "lastCrashType": "java.lang.IllegalStateException",
            "lastCrashAtUtc": "2026-11-21T11:30:00Z"
          }
        }
        """;

        var snapshot = JsonSerializer.Deserialize<AgentSnapshot>(json, Options);

        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.ScheduledBatteryLatest);
        Assert.Equal(83, snapshot.ScheduledBatteryLatest.BatteryPercent);
        Assert.Equal(4100, snapshot.ScheduledBatteryLatest.VoltageMillivolts);
        Assert.Equal(32.4, snapshot.ScheduledBatteryLatest.TemperatureCelsius);
        Assert.Equal(250, snapshot.ScheduledBatteryLatest.CycleCount);
        Assert.True(snapshot.ScheduledBatteryLatest.Plugged);
        Assert.Equal(24, snapshot.ScheduledBatterySamples);
        Assert.NotNull(snapshot.AgentTelemetrySummary);
        Assert.Equal(12, snapshot.AgentTelemetrySummary.EventCountSampled);
        Assert.Equal(1, snapshot.AgentTelemetrySummary.CrashCountSampled);
        Assert.Equal("java.lang.IllegalStateException", snapshot.AgentTelemetrySummary.LastCrashType);
    }

    [Fact]
    public void MissingScheduledTelemetryRemainsOptionalForOlderAgentVersions()
    {
        var snapshot = JsonSerializer.Deserialize<AgentSnapshot>(
            """{"agentVersion":"0.5.0","manufacturer":"Samsung","model":"SM-A315G"}""",
            Options);

        Assert.NotNull(snapshot);
        Assert.Null(snapshot.ScheduledBatteryLatest);
        Assert.Null(snapshot.AgentTelemetrySummary);
        Assert.Equal(0, snapshot.ScheduledBatterySamples);
    }
}
