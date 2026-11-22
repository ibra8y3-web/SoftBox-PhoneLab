using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;
namespace SoftBox.PhoneLab.Tests;
public sealed class UsbRecognitionSummaryTests
{
    [Fact]
    public void KnownQualcomm9008IsReportedAsEdlHintNotAsExactModel()
    {
        var identity = UsbHardwareIdentityCatalog.Classify("05C6", "9008");
        var device = new WindowsUsbDevice(@"USB\VID_05C6&PID_9008\SERIAL", "Qualcomm HS-USB QDLoader 9008", "Qualcomm", "USB", "WinUSB", "05C6", "9008", 0, "Present", identity);
        var summary = UsbRecognitionSummary.Build([device]);
        Assert.Contains("Qualcomm", summary); Assert.Contains("9008", summary);
        Assert.Contains("الموديل الدقيق يتطلب تصريح ADB", summary);
    }
    [Fact]
    public void UnknownHidDeviceIsNotReportedAsPhone()
    {
        var identity = UsbHardwareIdentityCatalog.Classify("1234", "5678");
        var device = new WindowsUsbDevice(@"USB\VID_1234&PID_5678\DEVICE", "Generic USB Input Device", "Unknown", "HID", "", "1234", "5678", 0, "Present", identity);
        Assert.Contains("لا توجد واجهة Android", UsbRecognitionSummary.Build([device]));
    }
}
