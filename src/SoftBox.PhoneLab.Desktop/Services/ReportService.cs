using System.IO;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed class ReportService
{
    public async Task ExportAsync(string path, DeviceSnapshot device, AgentSnapshot? agent, IReadOnlyList<ComponentDiagnostic> components, ExternalMeasurementSample? externalMeasurement = null)
    {
        var report = new
        {
            Product = "SoftBox PhoneLab",
            SchemaVersion = "1.1",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Device = device,
            MatchedDeviceProfile = new
            {
                Note = "The matched identity profile is a model identifier, not proof of PCB-map accuracy."
            },
            Agent = agent,
            ExternalMeasurement = externalMeasurement,
            Components = components,
            Interpretation = new
            {
                Green = "A named functional test completed successfully.",
                Yellow = "A value or test result needs review.",
                Red = "A named test reported failure.",
                Blue = "System information or a sensor/camera route was read; this is not proof of hardware health.",
                Gray = "Not tested or unavailable."
            },
            Limitations = new[]
            {
                "A model identity match does not imply an exact internal board map. Exact PCB maps require model/variant-specific source files with verified provenance.",
                "The bundled exploded view remains a generic schematic until a verified boardview/PCB asset for the precise model and hardware revision is installed.",
                "Software cannot prove solder joints, traces, PMIC pins, charging IC pins, connector damage or internal board electrical health without suitable instrumentation.",
                "Battery level, voltage, temperature, current and charge-counter values are Android fuel-gauge observations; they are not a certified full-capacity or internal-resistance test.",
                "Camera2 can only test camera IDs and physical routes exposed by the device vendor. A JPEG capture and reported autofocus state do not verify optical sharpness, stabilization, image quality or every lens mode.",
                "Physical display photo analysis reports candidate anomalies only. Reflections, moire, camera focus, perspective and exposure can create false positives.",
                "External measurement data is only as accurate as the connected, calibrated and electrically safe measurement fixture.",
                "Diagnostic results depend on model, board revision, Android version, permissions, device vendor restrictions and the connected equipment.",
                "TRACE Boardviewer is a separate MIT-licensed third-party Windows application launched by PhoneLab to open supported native board files. It does not manufacture model-specific boardviews or ship copyrighted repair data."
            }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
