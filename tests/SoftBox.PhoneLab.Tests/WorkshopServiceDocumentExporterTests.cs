using Xunit;
using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Tests;

public sealed class WorkshopServiceDocumentExporterTests
{
    [Fact]
    public async Task ExportsIntakeReceiptAndEscapesCustomerProvidedHtml()
    {
        var root = CreateTempDirectory();
        try
        {
            var order = CreateOrder();
            order.CustomerName = "<script>alert(1)</script>";
            var path = Path.Combine(root, "intake.html");

            await WorkshopServiceDocumentExporter.ExportAsync(
                new WorkshopData(), order, WorkshopDocumentKind.IntakeReceipt, path);

            var html = await File.ReadAllTextAsync(path);
            Assert.Contains("إيصال استلام جهاز", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("لا يثبت إتمام الإصلاح", html);
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task ExportsEstimateAsPreliminaryAndDoesNotClaimCustomerApproval()
    {
        var root = CreateTempDirectory();
        try
        {
            var order = CreateOrder();
            order.EstimatedCostEgp = 950m;
            var path = Path.Combine(root, "estimate.html");

            await WorkshopServiceDocumentExporter.ExportAsync(
                new WorkshopData(), order, WorkshopDocumentKind.EstimateQuote, path);

            var html = await File.ReadAllTextAsync(path);
            Assert.Contains("عرض سعر صيانة مبدئي", html);
            Assert.Contains("950", html);
            Assert.Contains("لم تسجل موافقة", html);
            Assert.Contains("غير معتمد", html);
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task ExportsServiceInvoiceWithSavedPartsAndUnrecordedPaymentStatus()
    {
        var root = CreateTempDirectory();
        try
        {
            var order = CreateOrder();
            order.FinalCostEgp = 1250m;
            order.PartsCostEgp = 350m;
            order.PartsUsed.Add(new RepairOrderPartUsage
            {
                Sku = "USB-C-01", PartName = "USB-C port", Quantity = 1, UnitCostEgp = 350m
            });
            var path = Path.Combine(root, "invoice.html");

            await WorkshopServiceDocumentExporter.ExportAsync(
                new WorkshopData(), order, WorkshopDocumentKind.ServiceInvoice, path);

            var html = await File.ReadAllTextAsync(path);
            Assert.Contains("فاتورة خدمة صيانة", html);
            Assert.Contains("USB-C-01", html);
            Assert.Contains("USB-C port", html);
            Assert.Contains("حالة السداد", html);
            Assert.Contains("غير مسجل", html);
            Assert.Contains("لا يحسب هذا المستند ضريبة", html);
        }
        finally { DeleteDirectory(root); }
    }

    private static RepairOrder CreateOrder() => new()
    {
        TicketNumber = "SB-20261120-ABC123",
        CustomerName = "Customer",
        CustomerPhone = "+201000000000",
        DeviceModel = "Android device",
        IntakeSymptoms = "No charge",
        WorkPerformed = "USB port inspection",
        EstimatedCostEgp = 900m,
        FinalCostEgp = 900m,
        ReceivedAtUtc = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero),
        Status = "قيد الإصلاح"
    };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-documents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
