using SoftBox.PhoneLab.Core.Models;
namespace SoftBox.PhoneLab.Desktop.Services;
public static class UsbRecognitionSummary
{
    private static readonly string[] PhoneMarkers = ["android", "adb", "fastboot", "samsung", "qualcomm", "qdloader", "mediatek", "preloader", "xiaomi", "redmi", "poco", "google", "pixel", "mtk", "usb serial"];
    public static string Build(IEnumerable<WindowsUsbDevice> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var relevant = source.Where(IsPhoneRelated).GroupBy(d => d.InstanceId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(6).ToArray();
        if (relevant.Length == 0) return "فحص USB نشط: لا توجد واجهة Android/صيانة معروفة حاليًا. سيظهر الهاتف هنا عند رصد واجهته من Windows.";
        var descriptions = relevant.Select(d =>
        {
            var known = !string.Equals(d.Identity.Category, "Unknown", StringComparison.OrdinalIgnoreCase);
            var label = known ? $"{d.Identity.Category} — {d.Identity.ModeHint}" : d.FriendlyName;
            var status = d.ProblemCode > 0 ? " • مشكلة تعريف" : "";
            return $"{label} [{d.VendorId}:{d.ProductId}]{status}";
        });
        return $"رصد Windows {relevant.Length} واجهة هاتف/صيانة: " + string.Join(" | ", descriptions) + ". الموديل الدقيق يتطلب تصريح ADB أو بيانات تعريف موثوقة من وضع الخدمة.";
    }
    private static bool IsPhoneRelated(WindowsUsbDevice d)
    {
        if (!string.Equals(d.Identity.Category, "Unknown", StringComparison.OrdinalIgnoreCase)) return true;
        var value = $"{d.FriendlyName} {d.Manufacturer} {d.DeviceClass} {d.Service}";
        return PhoneMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
