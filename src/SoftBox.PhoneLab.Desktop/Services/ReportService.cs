using System.IO;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed class ReportService
{
    public async Task ExportAsync(string path, DeviceSnapshot device, AgentSnapshot? agent, IReadOnlyList<ComponentDiagnostic> components)
    {
        var report = new
        {
            Product = "SoftBox PhoneLab",
            SchemaVersion = "1.0",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Device = device,
            Agent = agent,
            Components = components,
            Interpretation = new
            {
                Green = "A named functional test completed successfully.",
                Yellow = "A value or test result needs review.",
                Red = "A named test reported failure.",
                Blue = "System information was read; this is not proof of hardware health.",
                Gray = "Not tested or unavailable."
            },
            Limitations = new[]
            {
                "Software cannot prove solder joint, trace, PMIC, charging IC, or internal board electrical health without suitable instrumentation.",
                "Battery level, voltage and temperature are Android-reported observations, not a capacity test.",
                "A generic board diagram is not an exact PCB layout for this device.",
                "Diagnostic results depend on device model, Android version, permissions and vendor restrictions."
            }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
