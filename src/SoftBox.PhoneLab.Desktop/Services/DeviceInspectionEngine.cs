using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Core.Parsing;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed class DeviceInspectionEngine(AdbClient adb)
{
    public async Task<DeviceSnapshot> InspectAsync(string serial, CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial)) throw new ArgumentException("Invalid device serial.", nameof(serial));

        var properties = Run("getprop");
        var cpu = Run("cat", "/proc/cpuinfo");
        var memory = Run("cat", "/proc/meminfo");
        var storage = Run("df", "-k", "/data");
        var display = Run("wm", "size");
        var density = Run("wm", "density");
        var battery = Run("dumpsys", "battery");

        await Task.WhenAll(properties, cpu, memory, storage, display, density, battery);
        return DevicePropertyParser.ParseSnapshot(serial, properties.Result, cpu.Result, memory.Result,
            storage.Result, display.Result, density.Result, battery.Result);

        async Task<string> Run(params string[] args)
        {
            try
            {
                var all = new List<string> { "-s", serial, "shell" };
                all.AddRange(args);
                return await adb.RunAsync(all, cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                return "";
            }
        }
    }
}
