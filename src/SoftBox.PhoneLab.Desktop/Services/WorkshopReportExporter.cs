using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Models;

namespace SoftBox.PhoneLab.Desktop.Services;

public static class WorkshopReportExporter
{
    public static async Task ExportOrderHtmlAsync(WorkshopData snapshot, RepairOrder order, string path, CancellationToken ct = default)
    {
        var settings = snapshot.Settings ?? new WorkshopSettings();
        var ar = !settings.ReportLanguage.Equals("en", StringComparison.OrdinalIgnoreCase);
        var labels = ar
            ? new[] { "رقم العملية", "العميل", "الهاتف", "الجهاز", "الرقم التسلسلي / IMEI", "العطل المبلغ عنه", "نتائج الفحص قبل الإصلاح", "الأعمال المنفذة", "نتائج الفحص بعد الإصلاح", "الحالة", "الفني المسؤول", "التكلفة التقديرية", "التكلفة النهائية", "تكلفة قطع الغيار", "مدة الضمان (شهر)", "نهاية الضمان (UTC)", "تاريخ الاستلام (UTC)" }
            : new[] { "Ticket", "Customer", "Phone", "Device", "Serial / IMEI note", "Reported symptoms", "Findings before repair", "Work performed", "Results after repair", "Status", "Technician", "Estimated cost", "Final cost", "Parts cost", "Warranty (months)", "Warranty ends (UTC)", "Received (UTC)" };
        var currency = WorkshopInternationalRules.NormalizeCurrencyCode(settings.CurrencyCode);
        var culture = settings.CultureName;
        var values = new[]
        {
            order.TicketNumber, order.CustomerName, order.CustomerPhone, order.DeviceModel, order.SerialOrImeiNote,
            order.IntakeSymptoms, order.FindingsBeforeRepair, order.WorkPerformed, order.ResultsAfterRepair, order.Status, order.TechnicianName,
            WorkshopInternationalRules.FormatMoney(order.EstimatedCostEgp, currency, culture),
            WorkshopInternationalRules.FormatMoney(order.FinalCostEgp, currency, culture),
            WorkshopInternationalRules.FormatMoney(order.PartsCostEgp, currency, culture),
            order.WarrantyMonths.ToString(CultureInfo.InvariantCulture), order.WarrantyEndsAtUtc?.ToString("u") ?? (ar ? "لا يوجد" : "Not set"), order.ReceivedAtUtc.ToString("u")
        };
        var rows = new StringBuilder();
        for (var i = 0; i < labels.Length; i++)
            rows.Append("<tr><th>").Append(E(labels[i])).Append("</th><td>").Append(E(values[i])).AppendLine("</td></tr>");
        var timelineRows = new StringBuilder();
        foreach (var item in (order.Timeline ?? []).OrderBy(x => x.AtUtc))
            timelineRows.Append("<tr><td>").Append(E(item.AtUtc.ToString("u"))).Append("</td><td>").Append(E(item.TechnicianName)).Append("</td><td>").Append(E(item.Note)).AppendLine("</td></tr>");
        var partsRows = new StringBuilder();
        foreach (var part in (order.PartsUsed ?? []).OrderBy(x => x.UsedAtUtc))
            partsRows.Append("<tr><td>").Append(E(part.Sku)).Append("</td><td>").Append(E(part.PartName)).Append("</td><td>")
                .Append(part.Quantity).Append("</td><td>").Append(E(WorkshopInternationalRules.FormatMoney(part.UnitCostEgp, currency, culture)))
                .Append("</td><td>").Append(E(WorkshopInternationalRules.FormatMoney(part.TotalCostEgp, currency, culture)))
                .Append("</td><td>").Append(E(part.TechnicianName)).AppendLine("</td></tr>");
        var address = string.Join(" · ", new[] { settings.Address, settings.Country }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var title = ar ? "تقرير أمر صيانة" : "Repair Order Report";
        var dir = ar ? "rtl" : "ltr";
        var html = "<!doctype html><html lang=\"" + (ar ? "ar" : "en") + "\" dir=\"" + dir + "\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>" + E(title) + "</title>" +
            "<style>body{font-family:Arial,Segoe UI,sans-serif;color:#172033;margin:28px;line-height:1.5}header{border-bottom:3px solid #14517a;padding-bottom:14px;margin-bottom:18px}h1{margin:0 0 4px;color:#14517a}table{width:100%;border-collapse:collapse;margin:14px 0 24px;table-layout:fixed}th,td{border:1px solid #d8e0ea;padding:9px;text-align:inherit;vertical-align:top;overflow-wrap:anywhere}th{background:#eef4f9;width:28%}.muted{color:#52647a;font-size:12px}@media print{body{margin:10mm}button{display:none}}</style></head><body>" +
            "<header><h1>" + E(settings.BusinessName) + "</h1><div>" + E(address) + "</div><div>" + E(settings.Phone) + " · " + E(settings.Email) + "</div><div class=\"muted\">" + E(title) + " · " + E(order.TicketNumber) + "</div></header>" +
            "<button onclick=\"window.print()\">" + (ar ? "طباعة التقرير" : "Print report") + "</button><table>" + rows + "</table>" +
            "<h2>" + (ar ? "قطع الغيار المستخدمة" : "Parts used") + "</h2><table><thead><tr><th>SKU</th><th>" + (ar ? "القطعة" : "Part") + "</th><th>" + (ar ? "الكمية" : "Qty") + "</th><th>" + (ar ? "تكلفة الوحدة" : "Unit cost") + "</th><th>" + (ar ? "الإجمالي" : "Total") + "</th><th>" + (ar ? "الفني" : "Technician") + "</th></tr></thead><tbody>" + partsRows + "</tbody></table>" +
            "<h2>" + (ar ? "سجل المتابعة الفنية" : "Technician timeline") + "</h2><table><thead><tr><th>UTC</th><th>" + (ar ? "الفني" : "Technician") + "</th><th>" + (ar ? "الملاحظة" : "Note") + "</th></tr></thead><tbody>" + timelineRows + "</tbody></table>" +
            "<p class=\"muted\">" + (ar ? "هذا التقرير يسجل الملاحظات المدخلة ولا يمثل شهادة مستقلة لسلامة الجهاز أو جودة الإصلاح. مدة الضمان وحدوده تخضع لشروط المركز والقانون المحلي." : "This report records technician-entered observations and is not independent certification of device safety or repair quality. Warranty terms are subject to workshop policy and applicable local law.") + "</p>" +
            "<p>" + (ar ? "توقيع العميل: ____________________　　 توقيع الفني: ____________________" : "Customer signature: ____________________　　 Technician signature: ____________________") + "</p></body></html>";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, html, new UTF8Encoding(false), ct);
    }

    public static async Task ExportOrdersCsvAsync(WorkshopData snapshot, string path, CancellationToken ct = default)
    {
        var currency = WorkshopInternationalRules.NormalizeCurrencyCode(snapshot.Settings.CurrencyCode);
        var rows = new List<string> { CsvRow("Ticket", "Customer", "Phone", "Device", "Status", "Estimated", "Final", "PartsCost", "PartsUsedCount", "Currency", "ReceivedUtc", "DeliveredUtc", "WarrantyMonths", "WarrantyEndsUtc") };
        rows.AddRange(snapshot.RepairOrders.OrderByDescending(x => x.ReceivedAtUtc).Select(o => CsvRow(o.TicketNumber, o.CustomerName, o.CustomerPhone, o.DeviceModel, o.Status,
            o.EstimatedCostEgp.ToString(CultureInfo.InvariantCulture), o.FinalCostEgp.ToString(CultureInfo.InvariantCulture), o.PartsCostEgp.ToString(CultureInfo.InvariantCulture), (o.PartsUsed?.Count ?? 0).ToString(CultureInfo.InvariantCulture), currency, o.ReceivedAtUtc.ToString("O"),
            o.DeliveredAtUtc?.ToString("O") ?? "", o.WarrantyMonths.ToString(CultureInfo.InvariantCulture), o.WarrantyEndsAtUtc?.ToString("O") ?? "")));
        await File.WriteAllTextAsync(path, string.Join(Environment.NewLine, rows), new UTF8Encoding(true), ct);
    }

    public static async Task ExportInventoryCsvAsync(WorkshopData snapshot, string path, CancellationToken ct = default)
    {
        var currency = WorkshopInternationalRules.NormalizeCurrencyCode(snapshot.Settings.CurrencyCode);
        var rows = new List<string> { CsvRow("SKU", "Name", "Category", "Supplier", "Quantity", "MinimumQuantity", "UnitCost", "SalePrice", "Currency", "Location", "UpdatedUtc") };
        rows.AddRange(snapshot.Inventory.OrderBy(x => x.Sku).Select(x => CsvRow(x.Sku, x.Name, x.Category, x.Supplier, x.Quantity.ToString(CultureInfo.InvariantCulture),
            x.MinimumQuantity.ToString(CultureInfo.InvariantCulture), x.UnitCostEgp.ToString(CultureInfo.InvariantCulture), x.SalePriceEgp.ToString(CultureInfo.InvariantCulture), currency, x.Location, x.UpdatedAtUtc.ToString("O"))));
        await File.WriteAllTextAsync(path, string.Join(Environment.NewLine, rows), new UTF8Encoding(true), ct);
    }

    private static string CsvRow(params string?[] values) => string.Join(",", values.Select(Csv));
    private static string Csv(string? value)
    {
        value ??= "";
        // Prevent spreadsheet formula execution from user-entered names, notes and identifiers.
        if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@' || value[0] == (char)9 || value[0] == (char)13))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    }
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}