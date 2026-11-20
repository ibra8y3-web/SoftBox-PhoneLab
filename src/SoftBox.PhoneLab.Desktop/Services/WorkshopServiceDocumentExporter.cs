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
                details.Add((isArabic ? "حالة الموافقة" : "Approval status", isArabic ? "لم تسجل موافقة" : "Not recorded"));
                break;

            case WorkshopDocumentKind.ServiceInvoice:
                title = isArabic ? "فاتورة خدمة صيانة" : "Repair Service Invoice";
                notice = isArabic
                    ? "مستند بقيمة الخدمة النهائية المسجلة في أمر الصيانة. حالة السداد غير مسجلة في SoftBox. لا يحسب هذا المستند ضريبة ولا يمثل فاتورة إلكترونية معتمدة."
                    : "This document displays the final service amount saved on the order. Payment status is not recorded in SoftBox. No tax is calculated and this is not a certified e-invoice.";
                details.Add((isArabic ? "رقم الفاتورة المرجعي" : "Invoice reference", order.TicketNumber + "-INV"));
                details.Add((isArabic ? "تاريخ الإصدار UTC" : "Issued UTC", DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)));
                details.Add((isArabic ? "رقم أمر الصيانة" : "Repair ticket", order.TicketNumber));
                details.Add((isArabic ? "العميل" : "Customer", order.CustomerName));
                details.Add((isArabic ? "الهاتف" : "Phone", order.CustomerPhone));
                details.Add((isArabic ? "الجهاز" : "Device", order.DeviceModel));
                details.Add((isArabic ? "الأعمال المسجلة" : "Recorded work performed", order.WorkPerformed));
                details.Add((isArabic ? "تكلفة قطع الغيار المسجلة" : "Recorded parts cost", WorkshopInternationalRules.FormatMoney(order.PartsCostEgp, currency, culture)));
                details.Add((isArabic ? "إجمالي الخدمة النهائي المسجل" : "Recorded final service total", WorkshopInternationalRules.FormatMoney(order.FinalCostEgp, currency, culture)));
                details.Add((isArabic ? "حالة السداد" : "Payment status", isArabic ? "غير مسجل" : "Not recorded"));
                itemsTable = BuildPartsTable(order, currency, culture, isArabic);
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
