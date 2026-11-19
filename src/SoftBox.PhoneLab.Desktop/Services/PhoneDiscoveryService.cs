using SoftBox.PhoneLab.Desktop.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed class PhoneDiscoveryService(AdbClient adb)
{
    public async Task<IReadOnlyList<DeviceListItem>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var text = await adb.RunAsync(["devices", "-l"], cancellationToken);
        var found = new List<DeviceListItem>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            if (line.StartsWith('*') || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !AdbClient.IsSafeSerial(parts[0])) continue;
            found.Add(new DeviceListItem(parts[0], parts[1], "", ""));
        }
        return found;
    }
}
