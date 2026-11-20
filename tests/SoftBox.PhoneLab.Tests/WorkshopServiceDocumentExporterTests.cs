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
            Assert.Contains("بانتظار موافقة العميل", html);
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
            order.EstimatedCostEgp = 1250m;
            order.PartsCostEgp = 350m;
            order.QuoteDecisionHistory.Add(new CustomerQuoteDecisionRecord
            {
                Decision = CustomerQuoteDecision.Approved,
                QuotedAmountEgp = 1250m,
                ContactMethod = "حضور العميل",
                RecordedBy = "Technician A"
            });
            order.Payments.Add(new RepairPaymentRecord
            {
                AmountEgp = 500m,
                Method = "نقدًا",
                RecordedBy = "Technician A",
                BalanceAfterPaymentEgp = 750m
            });
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
            Assert.Contains("مدفوع جزئيًا", html);
            Assert.Contains("750", html);
            Assert.Contains("لا تُحسب الضرائب", html);
        }
        finally { DeleteDirectory(root); }
    }


    [Fact]
    public async Task ExportsManualPaymentReceiptAndEscapesCustomerAndPaymentText()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new WorkshopStore(root);
            var order = await store.CreateRepairOrderAsync(new RepairOrder
            {
                CustomerName = "<script>alert(1)</script>",
                CustomerPhone = "01000000000",
                DeviceModel = "Android device",
                IntakeSymptoms = "Charging issue",
                EstimatedCostEgp = 600m
            });
            await store.RecordQuoteDecisionAsync(
                order.Id, CustomerQuoteDecision.Approved, "مكالمة", "Technician A");
            await store.RecordPaymentAsync(
                order.Id, 200m, "نقدًا <img src=x>", "Technician A", "REF-01");

            var snapshot = await store.GetSnapshotAsync();
            var savedOrder = Assert.Single(snapshot.RepairOrders);
            var payment = Assert.Single(savedOrder.Payments);
            var path = Path.Combine(root, "payment.html");

            await WorkshopServiceDocumentExporter.ExportPaymentReceiptAsync(
                snapshot, savedOrder, payment, path);

            var html = await File.ReadAllTextAsync(path);
            Assert.Contains("إيصال تسجيل دفعة صيانة", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.Contains("&lt;img src=x&gt;", html);
            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("200", html);
            Assert.Contains("لا يؤكد الإيصال وصول الأموال", html);
            Assert.Contains("400", html);
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
