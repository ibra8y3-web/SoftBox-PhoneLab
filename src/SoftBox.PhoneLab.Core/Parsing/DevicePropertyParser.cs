using System.Globalization;
using System.Text.RegularExpressions;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Core.Parsing;

public static partial class DevicePropertyParser
{
    [GeneratedRegex(@"(?m)^\[(?<key>[^\]]+)\]:\s*\[(?<value>.*)\]\s*$")]
    private static partial Regex PropertyRegex();

    public static Dictionary<string, string> ParseProperties(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in PropertyRegex().Matches(text))
            values[match.Groups["key"].Value] = match.Groups["value"].Value.Trim();
        return values;
    }

    public static DeviceSnapshot ParseSnapshot(
        string serial, string propertiesText, string cpuInfo, string memoryInfo,
        string storageInfo, string displaySize, string displayDensity, string batteryInfo)
    {
        var p = ParseProperties(propertiesText);
        var cpuDescription = First(
            Get(p, "ro.soc.model"), Get(p, "ro.hardware.chipname"),
            FindCpuDescription(cpuInfo), Get(p, "ro.hardware"));

        var coreCount = Regex.Matches(cpuInfo, @"(?m)^processor\s*:").Count;
        if (coreCount == 0)
            coreCount = ParseInt(Get(p, "ro.vendor.qti.soc.cpu.cores")) ?? 0;

        var memoryMatch = Regex.Match(memoryInfo, @"(?im)^MemTotal:\s*(\d+)\s*kB");
        double? ram = memoryMatch.Success && long.TryParse(memoryMatch.Groups[1].Value, out var ramKiB)
            ? ramKiB / 1024d / 1024d : null;
        var storage = ParseDataStorage(storageInfo);
        var battery = ParseBattery(batteryInfo);

        return new DeviceSnapshot
        {
            Serial = serial,
            Manufacturer = First(Get(p, "ro.product.manufacturer"), Get(p, "ro.product.vendor.manufacturer")),
            Model = First(Get(p, "ro.product.marketname"), Get(p, "ro.product.model"), Get(p, "ro.product.vendor.model")),
            ModelNumber = First(Get(p, "ro.product.model"), Get(p, "ro.product.vendor.model"), Get(p, "ro.boot.product.hardware.sku")),
            Product = First(Get(p, "ro.product.name"), Get(p, "ro.product.vendor.name"), Get(p, "ro.build.product")),
            DeviceCode = First(Get(p, "ro.product.device"), Get(p, "ro.product.vendor.device"), Get(p, "ro.build.product")),
            HardwareSku = First(Get(p, "ro.boot.hardware.sku"), Get(p, "ro.boot.product.hardware.sku"), Get(p, "ro.boot.product.vendor.sku")),
            BuildFingerprint = First(Get(p, "ro.build.fingerprint"), Get(p, "ro.vendor.build.fingerprint")),
            Bootloader = First(Get(p, "ro.bootloader"), Get(p, "ro.boot.bootloader")),
            Board = First(Get(p, "ro.product.board"), Get(p, "ro.board.platform"), Get(p, "ro.hardware")),
            HardwareRevision = First(Get(p, "ro.boot.hardware.revision"), Get(p, "ro.boot.hwrev"), Get(p, "ro.boot.revision")),
            BoardRevision = First(Get(p, "ro.boot.board.revision"), Get(p, "ro.boot.board_version")),
            Soc = First(Get(p, "ro.soc.model"), Get(p, "ro.vendor.qti.soc_name")),
            CpuDescription = cpuDescription,
            CpuCores = coreCount,
            CpuAbi = First(Get(p, "ro.product.cpu.abilist"), Get(p, "ro.product.cpu.abi")),
            RamGiB = ram,
            DataTotalKiB = storage.Total,
            DataUsedKiB = storage.Used,
            DisplayResolution = ParseDisplay(displaySize),
            DisplayDensity = ParseDensity(displayDensity),
            AndroidVersion = First(Get(p, "ro.build.version.release")),
            ApiLevel = First(Get(p, "ro.build.version.sdk")),
            BuildId = First(Get(p, "ro.build.display.id"), Get(p, "ro.build.id")),
            SecurityPatch = Get(p, "ro.build.version.security_patch"),
            Baseband = Get(p, "gsm.version.baseband"),
            BatteryPercent = battery.Level,
            BatteryVoltageMillivolts = battery.Voltage,
            BatteryTemperatureCelsius = battery.Temperature,
            BatteryHealth = battery.Health,
            BatteryStatus = battery.Status,
            ReadAtUtc = DateTimeOffset.UtcNow
        };
    }

    public static (long? Total, long? Used) ParseDataStorage(string output)
    {
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Contains("/data", StringComparison.OrdinalIgnoreCase)).LastOrDefault();
        if (line is null) return (null, null);
        var c = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (c.Length < 4 ||
            !long.TryParse(c[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ||
            !long.TryParse(c[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var used))
            return (null, null);
        return (total, used);
    }

    public static (int? Level, int? Voltage, double? Temperature, string Health, string Status) ParseBattery(string output)
    {
        int? Read(string key) {
            var match = Regex.Match(output, $@"(?im)^\s*{Regex.Escape(key)}:\s*(\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
        }

        var health = Read("health") switch
        {
            2 => "GOOD reported by Android", 3 => "Overheat reported by Android",
            4 => "Dead reported by Android", 5 => "Over voltage reported by Android",
            6 => "Failure reported by Android", 7 => "Cold reported by Android", _ => "Unknown"
        };
        var status = Read("status") switch
        {
            2 => "Charging", 3 => "Discharging", 4 => "Not charging", 5 => "Full", _ => "Unknown"
        };
        var temp = Read("temperature");
        return (Read("level"), Read("voltage"), temp is null ? null : temp.Value / 10d, health, status);
    }

    private static string ParseDisplay(string output)
    {
        var matches = Regex.Matches(output, @"(?m)(?:Physical|Override) size:\s*(\d+x\d+)");
        return matches.Count == 0 ? "غير متاح" : matches[matches.Count - 1].Groups[1].Value;
    }
    private static string ParseDensity(string output)
    {
        var match = Regex.Match(output, @"(?m)(?:Physical|Override) density:\s*(\d+)");
        return match.Success ? $"{match.Groups[1].Value} dpi" : "غير متاح";
    }
    private static string FindCpuDescription(string text)
    {
        foreach (var pattern in new[] { @"(?im)^Hardware\s*:\s*(.+)$", @"(?im)^model name\s*:\s*(.+)$", @"(?im)^Processor\s*:\s*(.+)$" })
        {
            var match = Regex.Match(text, pattern);
            if (match.Success) return match.Groups[1].Value.Trim();
        }
        return "غير متاح";
    }
    private static int? ParseInt(string value) => int.TryParse(value, out var result) ? result : null;
    private static string Get(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : "غير متاح";
    private static string First(params string[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x != "غير متاح" && x != "unknown") ?? "غير متاح";
}
