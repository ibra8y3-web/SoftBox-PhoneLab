using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Core.Diagnostics;

public enum AdviceSeverity
{
    Information,
    Review,
    Urgent
}

public enum AdviceConfidence
{
    Observed,
    TestEvidence,
    Heuristic
}

public sealed record RepairAdvice(
    string Id,
    string Title,
    string Assessment,
    AdviceSeverity Severity,
    AdviceConfidence Confidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> SafeNextSteps,
    string Limitation);

public sealed record RepairAdviceReport(
    string DeviceName,
    DateTimeOffset GeneratedAtUtc,
    int EvidenceCount,
    IReadOnlyList<RepairAdvice> Advice,
    IReadOnlyList<string> Limitations);

/// <summary>
/// Deterministic, evidence-linked local repair advisor. It is not an LLM and never infers a hidden
/// hardware defect from missing data. It recommends non-destructive checks only.
/// </summary>
public static class DeviceRepairAdvisor
{
    public static RepairAdviceReport Analyze(
        DeviceSnapshot? device,
        AgentSnapshot? agent,
        IReadOnlyList<ComponentDiagnostic>? componentDiagnostics,
        bool adbAuthorized)
    {
        var advice = new List<RepairAdvice>();
        var evidenceCount = 0;
        var limitations = new List<string>();

        if (!adbAuthorized)
        {
            advice.Add(new RepairAdvice(
                "adb-not-authorized",
                "تصريح ADB غير متاح",
                "لا يمكن قراءة معلومات النظام أو تشغيل الاختبارات قبل موافقة الهاتف على تصحيح USB.",
                AdviceSeverity.Review,
                AdviceConfidence.Observed,
                ["حالة الاتصال الحالية ليست ADB device مصرحًا به."],
                [
                    "استخدم كابل بيانات موثوقًا ومنفذ USB سليمًا.",
                    "افتح الهاتف ووافق على نافذة Allow USB debugging.",
                    "أعد تحديث الأجهزة في SoftBox."
                ],
                "هذه مشكلة اتصال/صلاحية وليست دليلًا على عطل مادي في الهاتف."));
            evidenceCount++;
        }

        if (device is not null)
        {
            evidenceCount++;
            var battery = device.BatteryPercent;
            if (battery is <= 5)
            {
                advice.Add(new RepairAdvice(
                    "battery-low-charge",
                    "مستوى شحن البطارية منخفض جدًا",
                    $"النظام يقرأ مستوى شحن {battery}%. اشحن الهاتف قبل الاختبارات الطويلة أو عمليات النقل.",
                    AdviceSeverity.Review,
                    AdviceConfidence.Observed,
                    [$"BatteryPercent={battery}%"],
                    [
                        "استخدم شاحنًا وكابلًا مناسبين للجهاز.",
                        "أعد قراءة مستوى الشحن بعد عدة دقائق.",
                        "لا تفسر انخفاض الشحن وحده على أنه تلف بطارية."
                    ],
                    "هذه قراءة مستوى الشحن وليست قياسًا لسعة البطارية أو مقاومتها الداخلية."));
            }

            if (device.BatteryTemperatureCelsius is double temperature)
            {
                if (temperature >= 50)
                {
                    advice.Add(new RepairAdvice(
                        "battery-temperature-high",
                        "درجة حرارة البطارية مرتفعة وتحتاج مراجعة",
                        $"قراءة النظام الحالية {temperature:0.0}°C. أوقف الاختبارات غير الضرورية واترك الهاتف يبرد.",
                        AdviceSeverity.Urgent,
                        AdviceConfidence.Observed,
                        [$"BatteryTemperature={temperature:0.0}°C"],
                        [
                            "افصل الشاحن وأوقف التطبيقات أو العمليات الثقيلة.",
                            "أبعد الهاتف عن الحرارة المباشرة واتركه يبرد في مكان آمن.",
                            "لا تفتح الهاتف أو تثقب البطارية؛ اطلب فحصًا فنيًا إذا استمرت الحرارة."
                        ],
                        "القراءة قادمة من واجهة النظام وقد تختلف عن قياس مختبري مباشر."));
                }
                else if (temperature >= 43)
                {
                    advice.Add(new RepairAdvice(
                        "battery-temperature-review",
                        "درجة الحرارة أعلى من المعتاد وتحتاج متابعة",
                        $"قراءة النظام الحالية {temperature:0.0}°C. أعد القياس بعد إيقاف الحمل والانتظار.",
                        AdviceSeverity.Review,
                        AdviceConfidence.Heuristic,
                        [$"BatteryTemperature={temperature:0.0}°C"],
                        [
                            "أوقف الاختبارات الثقيلة مؤقتًا.",
                            "أعد قراءة درجة الحرارة بعد أن يبرد الجهاز.",
                            "قارن القراءة مع ظروف التشغيل وحرارة البيئة."
                        ],
                        "هذه عتبة تنبيه تقريبية وليست تشخيصًا مؤكدًا لعيب البطارية."));
                }
            }

            if (device.DataTotalKiB is > 0 && device.DataUsedKiB is >= 0)
            {
                var usedPercent = 100d * device.DataUsedKiB.Value / device.DataTotalKiB.Value;
                evidenceCount++;
                if (usedPercent >= 95)
                {
                    advice.Add(new RepairAdvice(
                        "storage-critical",
                        "مساحة تخزين البيانات شبه ممتلئة",
                        $"الاستخدام المقروء يقارب {usedPercent:0.0}% من مساحة قسم البيانات.",
                        AdviceSeverity.Review,
                        AdviceConfidence.Observed,
                        [$"DataUsedKiB={device.DataUsedKiB}", $"DataTotalKiB={device.DataTotalKiB}"],
                        [
                            "اعرض الملفات الكبيرة من مدير الملفات بعد تصريح ADB.",
                            "راجع النسخ الاحتياطية قبل حذف أي ملف.",
                            "لا تحذف مجلدات النظام أو بيانات التطبيقات الخاصة."
                        ],
                        "التقدير مبني على بيانات df المتاحة ولا يحدد الملفات المسؤولة عن الاستهلاك."));
                }
            }
        }
        else
        {
            limitations.Add("لم تتوفر لقطة DeviceSnapshot؛ تم تعطيل تحليل خصائص الجهاز.");
        }

        if (agent is not null)
        {
            foreach (var test in agent.Tests)
            {
                var status = NormalizeStatus(test.Value.Status);
                if (status is "failed" or "fail")
                {
                    evidenceCount++;
                    advice.Add(new RepairAdvice(
                        "agent-failed-" + SafeId(test.Key),
                        "نتيجة اختبار تحتاج فحصًا: " + test.Key,
                        "أرسل Agent نتيجة اختبار فاشل. راجع دليل الاختبار وأعده مرة واحدة بعد التحقق من طريقة التنفيذ.",
                        AdviceSeverity.Review,
                        AdviceConfidence.TestEvidence,
                        [$"Agent test={test.Key}", $"Status={test.Value.Status}", test.Value.Evidence],
                        [
                            "راجع خطوات الاختبار وتأكد من تنفيذها على المكوّن الصحيح.",
                            "أعد الاختبار مرة واحدة بعد التأكد من الظروف والصلاحيات.",
                            "نفّذ فحصًا ماديًا مستقلًا قبل استبدال أي قطعة."
                        ],
                        "نتيجة Agent دليل على مسار الاختبار فقط، وليست وحدها إثباتًا نهائيًا لسبب العطل."));
                }
                else if (status is "review" or "warning")
                {
                    evidenceCount++;
                    advice.Add(new RepairAdvice(
                        "agent-review-" + SafeId(test.Key),
                        "نتيجة اختبار تحتاج مراجعة: " + test.Key,
                        "أبلغ Agent عن حالة مراجعة وليست نجاحًا أو فشلًا مؤكدًا.",
                        AdviceSeverity.Review,
                        AdviceConfidence.TestEvidence,
                        [$"Agent test={test.Key}", $"Status={test.Value.Status}", test.Value.Evidence],
                        ["راجع الدليل المعروض وأعد الاختبار في ظروف مناسبة."],
                        "لا يتم تحويل حالات review إلى عطل مؤكد."));
                }
            }
        }
        else if (adbAuthorized)
        {
            limitations.Add("Agent لم يقدم نتائج اختبارات بعد؛ بيانات النظام وحدها لا تكفي لإصدار حكم شامل على العتاد.");
        }

        foreach (var component in componentDiagnostics ?? [])
        {
            if (component.Status is DiagnosticStatus.Failed or DiagnosticStatus.Review)
            {
                evidenceCount++;
                var failed = component.Status == DiagnosticStatus.Failed;
                advice.Add(new RepairAdvice(
                    "component-" + SafeId(component.Id),
                    (failed ? "فشل مسجل في: " : "مكوّن يحتاج مراجعة: ") + component.Name,
                    component.Description,
                    failed ? AdviceSeverity.Review : AdviceSeverity.Information,
                    failed ? AdviceConfidence.TestEvidence : AdviceConfidence.Heuristic,
                    [$"Component={component.Id}", $"Status={component.Status}", component.Evidence],
                    [
                        "راجع الأدلة المرتبطة بالمكوّن.",
                        "أعد الاختبار المناسب قبل اتخاذ قرار إصلاح.",
                        "لا تنفذ حذفًا أو تفليشًا بناءً على هذه النتيجة وحدها."
                    ],
                    "التوصية تعتمد على حالة التشخيص التي سجلها الفني أو Agent."));
            }
        }

        if (advice.Count == 0)
        {
            advice.Add(new RepairAdvice(
                "no-observed-issue",
                "لا توجد مشكلة مؤكدة في الأدلة المتاحة",
                "لم تصل قواعد المراجعة إلى نتيجة فشل أو تحذير من البيانات الحالية. هذا لا يعني أن الجهاز سليم بالكامل.",
                AdviceSeverity.Information,
                AdviceConfidence.Observed,
                ["لا توجد حالات Failed أو Review أو قراءة تتجاوز عتبات التنبيه الحالية."],
                ["أكمل الاختبارات المناسبة يدويًا، واطلب فحصًا ماديًا عند استمرار الشكوى."],
                "الفحص محدود بما أتاحه النظام والاختبارات؛ قد توجد أعطال لا يمكن اكتشافها برمجيًا."));
        }

        return new RepairAdviceReport(
            device is null ? "Unknown device" : $"{device.Manufacturer} {device.Model}".Trim(),
            DateTimeOffset.UtcNow,
            evidenceCount,
            advice.OrderByDescending(item => item.Severity)
                .ThenBy(item => item.Title, StringComparer.Ordinal)
                .ToArray(),
            limitations);
    }

    private static string NormalizeStatus(string? status)
        => (status ?? "").Trim().ToLowerInvariant();

    private static string SafeId(string value)
    {
        var id = new string(value.ToLowerInvariant()
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Take(40).ToArray());
        return string.IsNullOrWhiteSpace(id) ? "unknown" : id;
    }
}
