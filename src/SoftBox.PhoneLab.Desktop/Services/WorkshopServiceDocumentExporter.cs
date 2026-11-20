using SoftBox.PhoneLab.Core.Models;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using SoftBox.PhoneLab.Desktop.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public enum WorkshopDocumentKind
{
    IntakeReceipt,
    EstimateQuote,
    ServiceInvoice
}

/// <summary>
/// Creates local, printable HTML workshop documents from values already saved on a repair order.
/// It does not approve quotes, record payments, calculate taxes or claim legal e-invoice compliance.
/// </summary>
public static class WorkshopServiceDocumentExporter
{
    public static Task ExportAsync(
        WorkshopData snapshot,
        RepairOrder order,
        WorkshopDocumentKind kind,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var settings = snapshot.Settings ?? new WorkshopSettings();
        var isArabic = !settings.ReportLanguage.Equals("en", StringComparison.OrdinalIgnoreCase);
        var currency = WorkshopInternationalRules.NormalizeCurrencyCode(settings.CurrencyCode);
        var culture = settings.CultureName;
        var details = new List<(string Label, string Value)>();
        string title;
        string notice;
        string? itemsTable = null;

        switch (kind)
        {
            case WorkshopDocumentKind.IntakeReceipt:
                title = isArabic ? "إيصال استلام جهاز" : "Device Intake Receipt";
                notice = isArabic
                    ? "هذا الإيصال يثبت تسجيل استلام الجهاز والبيانات المذكورة فقط. لا يثبت إتمام الإصلاح أو استلام أي مبلغ."
                    : "This receipt records intake details only. It does not certify repair completion or payment.";
                details.Add((isArabic ? "رقم الاستلام" : "Intake ticket", order.TicketNumber));
                details.Add((isArabic ? "تاريخ الاستلام UTC" : "Received UTC", order.ReceivedAtUtc.ToString("u", CultureInfo.InvariantCulture)));
                details.Add((isArabic ? "العميل" : "Customer", order.CustomerName));
                details.Add((isArabic ? "الهاتف" : "Phone", order.CustomerPhone));
                details.Add((isArabic ? "الجهاز" : "Device", order.DeviceModel));
                details.Add((isArabic ? "ملاحظة Serial / IMEI" : "Serial / IMEI note", order.SerialOrImeiNote));
                details.Add((isArabic ? "العطل المبلغ عنه" : "Reported symptoms", order.IntakeSymptoms));
                details.Add((isArabic ? "الحالة عند إنشاء المستند" : "Status at document time", order.Status));
                details.Add((isArabic ? "الفني المسؤول" : "Technician", order.TechnicianName));
                break;

            case WorkshopDocumentKind.EstimateQuote:
                title = isArabic ? "عرض سعر صيانة مبدئي" : "Preliminary Repair Estimate";
                notice = isArabic
                    ? "عرض السعر مبدئي وغير معتمد داخل النظام حتى يسجل المركز موافقة العميل. لا يتم إضافة ضريبة أو رسوم غير مسجلة في أمر الصيانة."
                    : "This is a preliminary estimate. Customer approval is not recorded by this document. No unrecorded tax or fee is added.";
                details.Add((isArabic ? "رقم العرض" : "Estimate reference", order.TicketNumber + "-EST"));
                details.Add((isArabic ? "تاريخ الإصدار UTC" : "Issued UTC", DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)));
                details.Add((isArabic ? "العميل" : "Customer", order.CustomerName));
                details.Add((isArabic ? "الهاتف" : "Phone", order.CustomerPhone));
                details.Add((isArabic ? "الجهاز" : "Device", order.DeviceModel));
                details.Add((isArabic ? "وصف المشكلة" : "Reported symptoms", order.IntakeSymptoms));
                details.Add((isArabic ? "التكلفة التقديرية المسجلة" : "Recorded estimate", WorkshopInternationalRules.FormatMoney(order.EstimatedCostEgp, currency, culture)));
                var quoteDecision = order.LatestQuoteDecision;
                var quoteDecisionLabel = quoteDecision is null
                    ? (isArabic ? "بانتظار موافقة العميل" : "Awaiting customer decision")
                    : quoteDecision.Decision == CustomerQuoteDecision.Approved
                        ? (isArabic ? "وافق العميل" : "Approved by customer")
                        : (isArabic ? "رفض العميل" : "Rejected by customer");
                details.Add((isArabic ? "حالة الموافقة" : "Approval status", quoteDecisionLabel));
                if (quoteDecision is not null)
                {
                    details.Add((isArabic ? "القيمة التي وافق عليها / ناقشها العميل" : "Recorded quote amount", WorkshopInternationalRules.FormatMoney(quoteDecision.QuotedAmountEgp, currency, culture)));
                    details.Add((isArabic ? "طريقة توثيق التواصل" : "Contact method", quoteDecision.ContactMethod));
                    details.Add((isArabic ? "تاريخ القرار UTC" : "Decision time UTC", quoteDecision.RecordedAtUtc.ToString("u", CultureInfo.InvariantCulture)));
                    details.Add((isArabic ? "سجل القرار بواسطة" : "Decision recorded by", quoteDecision.RecordedBy));
                    details.Add((isArabic ? "ملاحظة القرار" : "Decision note", quoteDecision.Note));
                }
                break;

            case WorkshopDocumentKind.ServiceInvoice:
                title = isArabic ? "فاتورة خدمة صيانة" : "Repair Service Invoice";
                notice = isArabic
                    ? "يعرض هذا المستند المبالغ والدفعات التي سجلها المركز محليًا. التسجيل اليدوي لا يتحقق من تحصيل الأموال لدى بنك أو مزود دفع. لا تُحسب الضرائب ولا يمثل المستند فاتورة إلكترونية معتمدة."
                    : "This document shows amounts and payments manually recorded by the workshop. Manual entries do not verify funds with a bank or payment provider. No tax is calculated and this is not a certified e-invoice.";
                details.Add((isArabic ? "رقم الفاتورة المرجعي" : "Invoice reference", order.TicketNumber + "-INV"));
                details.Add((isArabic ? "تاريخ الإصدار UTC" : "Issued UTC", DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)));
                details.Add((isArabic ? "رقم أمر الصيانة" : "Repair ticket", order.TicketNumber));
                details.Add((isArabic ? "العميل" : "Customer", order.CustomerName));
                details.Add((isArabic ? "الهاتف" : "Phone", order.CustomerPhone));
                details.Add((isArabic ? "الجهاز" : "Device", order.DeviceModel));
                details.Add((isArabic ? "الأعمال المسجلة" : "Recorded work performed", order.WorkPerformed));
                details.Add((isArabic ? "تكلفة قطع الغيار المسجلة" : "Recorded parts cost", WorkshopInternationalRules.FormatMoney(order.PartsCostEgp, currency, culture)));
                details.Add((isArabic ? "إجمالي الخدمة النهائي المسجل" : "Recorded final service total", WorkshopInternationalRules.FormatMoney(order.FinalCostEgp, currency, culture)));
                details.Add((isArabic ? "قيمة العرض المعتمد للدفع" : "Approved payment target", WorkshopInternationalRules.FormatMoney(order.PaymentTargetEgp, currency, culture)));
                details.Add((isArabic ? "إجمالي الدفعات المسجلة" : "Recorded payments", WorkshopInternationalRules.FormatMoney(order.TotalPaidEgp, currency, culture)));
                details.Add((isArabic ? "الرصيد المتبقي" : "Outstanding balance", WorkshopInternationalRules.FormatMoney(order.OutstandingBalanceEgp, currency, culture)));
                details.Add((isArabic ? "حالة السداد" : "Payment status", order.PaymentStatus));
                itemsTable = BuildPartsTable(order, currency, culture, isArabic) + BuildPaymentsTable(order, currency, culture, isArabic);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var html = BuildHtml(settings, order, title, notice, details, itemsTable, isArabic);
        return WriteAsync(path, html, cancellationToken);
    }

    private static string BuildHtml(
        WorkshopSettings settings,
        RepairOrder order,
        string title,
        string notice,
        IReadOnlyList<(string Label, string Value)> details,
        string? itemsTable,
        bool isArabic)
    {
        var rows = new StringBuilder();
        foreach (var (label, value) in details)
            rows.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).AppendLine("</td></tr>");

        var address = string.Join(" · ", new[] { settings.Address, settings.Country }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var direction = isArabic ? "rtl" : "ltr";
        var language = isArabic ? "ar" : "en";
        var signatures = isArabic
            ? "<div class=\"signatures\"><span>توقيع العميل: ____________________</span><span>توقيع المركز: ____________________</span></div>"
            : "<div class=\"signatures\"><span>Customer signature: ____________________</span><span>Workshop signature: ____________________</span></div>";

        return "<!doctype html><html lang=\"" + language + "\" dir=\"" + direction + "\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>" + E(title) + "</title>" +
            "<style>body{font-family:Arial,Segoe UI,sans-serif;color:#172033;margin:28px;line-height:1.55}header{border-bottom:3px solid #14517a;padding-bottom:14px;margin-bottom:18px}h1{margin:0 0 4px;color:#14517a}.muted{color:#52647a;font-size:12px}.notice{background:#fff8e8;border:1px solid #e8cf91;padding:12px;margin:16px 0}table{width:100%;border-collapse:collapse;margin:14px 0 24px;table-layout:fixed}th,td{border:1px solid #d8e0ea;padding:9px;text-align:inherit;vertical-align:top;overflow-wrap:anywhere}th{background:#eef4f9;width:28%}.signatures{display:flex;justify-content:space-between;gap:24px;margin-top:50px}@media print{body{margin:10mm}button{display:none}}</style></head><body>" +
            "<header><h1>" + E(settings.BusinessName) + "</h1><div>" + E(address) + "</div><div>" + E(settings.Phone) + " · " + E(settings.Email) + "</div>" +
            "<div class=\"muted\">" + E(title) + " · " + E(order.TicketNumber) + "</div></header>" +
            "<button onclick=\"window.print()\">" + (isArabic ? "طباعة / حفظ PDF" : "Print / Save PDF") + "</button>" +
            "<div class=\"notice\">" + E(notice) + "</div><table><tbody>" + rows + "</tbody></table>" +
            (itemsTable ?? "") + signatures + "</body></html>";
    }

    public static Task ExportPaymentReceiptAsync(
        WorkshopData snapshot,
        RepairOrder order,
        RepairPaymentRecord payment,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var savedPayment = (order.Payments ?? []).FirstOrDefault(item => item.Id == payment.Id)
            ?? throw new InvalidOperationException("دفعة الدفع المحددة غير مرتبطة بأمر الصيانة المختار.");
        var settings = snapshot.Settings ?? new WorkshopSettings();
        var isArabic = !settings.ReportLanguage.Equals("en", StringComparison.OrdinalIgnoreCase);
        var currency = WorkshopInternationalRules.NormalizeCurrencyCode(settings.CurrencyCode);
        var title = isArabic ? "إيصال تسجيل دفعة صيانة" : "Repair Payment Record";
        var notice = isArabic
            ? "هذا إيصال بسجل دفعة أدخلها المركز يدويًا. لا يؤكد الإيصال وصول الأموال إلى حساب بنكي أو محفظة أو مزود دفع."
            : "This is a receipt of a payment manually recorded by the workshop. It does not confirm funds reached a bank, wallet or payment provider.";
        var details = new List<(string Label, string Value)>
        {
            (isArabic ? "رقم الإيصال" : "Receipt reference", order.TicketNumber + "-PAY-" + savedPayment.Id.ToString("N")[..8].ToUpperInvariant()),
            (isArabic ? "أمر الصيانة" : "Repair ticket", order.TicketNumber),
            (isArabic ? "العميل" : "Customer", order.CustomerName),
            (isArabic ? "الجهاز" : "Device", order.DeviceModel),
            (isArabic ? "قيمة الدفعة المسجلة" : "Recorded payment amount", WorkshopInternationalRules.FormatMoney(savedPayment.AmountEgp, currency, settings.CultureName)),
            (isArabic ? "طريقة الدفع المدخلة" : "Entered payment method", savedPayment.Method),
            (isArabic ? "مرجع العملية المدخل" : "Entered payment reference", savedPayment.Reference),
            (isArabic ? "تاريخ التسجيل UTC" : "Recorded UTC", savedPayment.ReceivedAtUtc.ToString("u", CultureInfo.InvariantCulture)),
            (isArabic ? "سجل الدفعة بواسطة" : "Recorded by", savedPayment.RecordedBy),
            (isArabic ? "الرصيد بعد الدفعة" : "Balance after payment", WorkshopInternationalRules.FormatMoney(savedPayment.BalanceAfterPaymentEgp, currency, settings.CultureName)),
            (isArabic ? "ملاحظة" : "Note", savedPayment.Note)
        };

        var html = BuildHtml(settings, order, title, notice, details, null, isArabic);
        return WriteAsync(path, html, cancellationToken);
    }

    private static string BuildPaymentsTable(RepairOrder order, string currency, string culture, bool isArabic)
    {
        var payments = (order.Payments ?? []).OrderBy(item => item.ReceivedAtUtc).ToArray();
        if (payments.Length == 0) return "";

        var builder = new StringBuilder();
        builder.Append("<h2>").Append(isArabic ? "سجل الدفعات المدخلة" : "Recorded payments").Append("</h2>");
        builder.Append("<table><thead><tr><th>").Append(isArabic ? "التاريخ UTC" : "Date UTC")
            .Append("</th><th>").Append(isArabic ? "القيمة" : "Amount").Append("</th><th>")
            .Append(isArabic ? "الطريقة" : "Method").Append("</th><th>")
            .Append(isArabic ? "المرجع" : "Reference").Append("</th><th>")
            .Append(isArabic ? "الرصيد بعد الدفعة" : "Balance after").Append("</th></tr></thead><tbody>");
        foreach (var payment in payments)
        {
            builder.Append("<tr><td>").Append(E(payment.ReceivedAtUtc.ToString("u", CultureInfo.InvariantCulture)))
                .Append("</td><td>").Append(E(WorkshopInternationalRules.FormatMoney(payment.AmountEgp, currency, culture)))
                .Append("</td><td>").Append(E(payment.Method))
                .Append("</td><td>").Append(E(payment.Reference))
                .Append("</td><td>").Append(E(WorkshopInternationalRules.FormatMoney(payment.BalanceAfterPaymentEgp, currency, culture)))
                .Append("</td></tr>");
        }
        builder.Append("</tbody></table>");
        return builder.ToString();
    }

    private static string BuildPartsTable(RepairOrder order, string currency, string culture, bool isArabic)
    {
        var builder = new StringBuilder();
        builder.Append("<h2>").Append(isArabic ? "قطع الغيار المسجلة" : "Recorded parts").Append("</h2>");
        builder.Append("<table><thead><tr><th>SKU</th><th>").Append(isArabic ? "القطعة" : "Part")
            .Append("</th><th>").Append(isArabic ? "الكمية" : "Quantity").Append("</th><th>")
            .Append(isArabic ? "تكلفة الوحدة" : "Unit cost").Append("</th><th>")
            .Append(isArabic ? "الإجمالي" : "Total").Append("</th></tr></thead><tbody>");
        foreach (var part in (order.PartsUsed ?? []).OrderBy(item => item.UsedAtUtc))
        {
            builder.Append("<tr><td>").Append(E(part.Sku)).Append("</td><td>").Append(E(part.PartName))
                .Append("</td><td>").Append(part.Quantity.ToString(CultureInfo.InvariantCulture))
                .Append("</td><td>").Append(E(WorkshopInternationalRules.FormatMoney(part.UnitCostEgp, currency, culture)))
                .Append("</td><td>").Append(E(WorkshopInternationalRules.FormatMoney(part.TotalCostEgp, currency, culture)))
                .Append("</td></tr>");
        }
        builder.Append("</tbody></table>");
        return builder.ToString();
    }

    private static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, new UTF8Encoding(false), cancellationToken);
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
