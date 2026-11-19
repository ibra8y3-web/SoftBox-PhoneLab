using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Core.Diagnostics;

public static class DiagnosticEngine
{
    private static readonly (string Id, string Name, string Description)[] Catalog =
    [
        ("display", "الشاشة", "اختبار ألوان بصري بموافقة المستخدم؛ لا يكشف كل البكسلات آليًا."),
        ("touch", "طبقة اللمس", "اختبار شبكة تغطية لمس تفاعلي؛ لا يقيس قوة الضغط أو كل مستويات الحساسية."),
        ("front-camera", "الكاميرا الأمامية", "تحتاج فتح الكاميرا الأمامية واختبار صورة لكل عدسة."),
        ("rear-camera", "الكاميرات الخلفية", "تحتاج اختبار كل وحدة وعدسة على حدة."),
        ("mainboard", "اللوحة الأم", "بيانات اللوحة لا تثبت سلامة المسارات أو اللحام."),
        ("processor", "المعالج / SoC", "قراءة هوية المعالج لا تثبت السلامة الكهربائية."),
        ("ram", "ذاكرة RAM", "قراءة الذاكرة المتاحة لا تعادل اختبار خلايا RAM."),
        ("storage", "التخزين الداخلي", "المساحة المتاحة لا تعادل فحص NAND/UFS."),
        ("pmic", "آيسي إدارة الطاقة PMIC", "يتطلب قياسًا كهربائيًا على اللوحة."),
        ("charge-ic", "آيسي الشحن", "يتطلب اختبار مسار الشحن وقياس التيار والفولتية."),
        ("battery", "البطارية", "بيانات Android ليست اختبار سعة أو مقاومة داخلية."),
        ("usb-port", "منفذ USB", "نجاح ADB يثبت عمل جزء من مسار البيانات فقط."),
        ("speaker", "السماعة", "يتطلب نغمة اختبار وتأكيد المستخدم."),
        ("microphone", "الميكروفون", "يتطلب تسجيلًا ومراجعة عينة صوت."),
        ("vibrator", "محرك الاهتزاز", "يمكن اختباره من Agent."),
        ("sensors", "المستشعرات", "يمكن حصر الحساسات وقراءة قيمها من Agent."),
        ("rf", "الشبكة والاتصالات", "يتطلب اختبارات مستقلة لكل وسيلة اتصال."),
        ("sim", "قارئ SIM", "يتطلب شريحة وفحص التسجيل على الشبكة."),
        ("frame", "الهيكل والإطار", "يتطلب فحصًا بصريًا أو يدويًا."),
        ("charging-board", "لوحة الشحن السفلية", "موضع تقريبي، ويختلف تصميمه حسب الموديل.")
    ];

    public static List<ComponentDiagnostic> Build(DeviceSnapshot? device, AgentSnapshot? agent)
    {
        var results = Catalog.Select(item => new ComponentDiagnostic
        {
            Id = item.Id,
            Name = item.Name,
            Description = item.Description,
            Status = DiagnosticStatus.NotTested,
            Evidence = "لم يتم إجراء اختبار وظيفي موثق."
        }).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        if (device is not null)
        {
            Read(results, "processor", device.Soc != "غير متاح" || device.CpuDescription != "غير متاح",
                $"SoC: {device.Soc}; CPU: {device.CpuDescription}; cores: {device.CpuCores}. هذه قراءة تعريفية فقط.");
            Read(results, "ram", device.RamGiB.HasValue, device.RamGiB.HasValue ? $"RAM reported: {device.RamGiB.Value:0.0} GiB. لم يتم اختبار الذاكرة." : "");
            Read(results, "storage", device.DataTotalKiB.HasValue,
                device.DataTotalKiB.HasValue ? $"قسم /data: {device.DataUsedKiB / 1024d / 1024d:0.0} / {device.DataTotalKiB / 1024d / 1024d:0.0} GiB." : "");
            Read(results, "display", device.DisplayResolution != "غير متاح",
                $"الدقة: {device.DisplayResolution}، الكثافة: {device.DisplayDensity}. لا يوجد اختبار بكسلات/لمس بعد.");
            Read(results, "mainboard", device.Board != "غير متاح", $"Board identifier: {device.Board}. لا يثبت سلامة اللوحة.");
            Read(results, "battery", device.BatteryPercent.HasValue || device.BatteryVoltageMillivolts.HasValue,
                $"Level: {device.BatteryPercent?.ToString() ?? "N/A"}%; voltage: {device.BatteryVoltageMillivolts?.ToString() ?? "N/A"} mV; temperature: {device.BatteryTemperatureCelsius?.ToString("0.0") ?? "N/A"} °C; health: {device.BatteryHealth}.");
            if (device.BatteryHealth.Contains("Overheat", StringComparison.OrdinalIgnoreCase) ||
                device.BatteryHealth.Contains("Dead", StringComparison.OrdinalIgnoreCase) ||
                device.BatteryHealth.Contains("Failure", StringComparison.OrdinalIgnoreCase) ||
                device.BatteryTemperatureCelsius >= 45)
                results["battery"] = results["battery"] with { Status = DiagnosticStatus.Review, Evidence = "تنبيه من بيانات Android: " + results["battery"].Evidence };
            Read(results, "usb-port", true, "اتصال ADB يعمل؛ هذا لا يختبر قدرة الشحن أو جميع أطراف المنفذ.");
        }

        if (agent is not null)
        {
            ApplyAgent(results, agent, "display", "display");
            ApplyAgent(results, agent, "touch", "touch");
            ApplyAgent(results, agent, "speaker", "speaker");
            ApplyAgent(results, agent, "microphone", "microphone");
            ApplyAgent(results, agent, "vibration", "vibrator");
            ApplyAgent(results, agent, "camera", "rear-camera");
            if (agent.Sensors.Count > 0)
                results["sensors"] = results["sensors"] with
                {
                    Status = DiagnosticStatus.DataRead,
                    Evidence = "Sensors exposed by Android: " + string.Join(", ", agent.Sensors)
                };
        }

        return Catalog.Select(x => results[x.Id]).ToList();
    }

    private static void Read(Dictionary<string, ComponentDiagnostic> values, string id, bool available, string evidence)
    {
        if (available)
            values[id] = values[id] with { Status = DiagnosticStatus.DataRead, Evidence = evidence };
    }

    private static void ApplyAgent(Dictionary<string, ComponentDiagnostic> values, AgentSnapshot agent, string testId, string componentId)
    {
        if (!agent.Tests.TryGetValue(testId, out var test) || string.IsNullOrWhiteSpace(test.Status))
            return;

        var status = test.Status.ToLowerInvariant() switch
        {
            "passed" or "pass" => DiagnosticStatus.Pass,
            "failed" or "fail" => DiagnosticStatus.Failed,
            "review" or "warning" => DiagnosticStatus.Review,
            "data_read" => DiagnosticStatus.DataRead,
            _ => DiagnosticStatus.NotTested
        };
        if (status != DiagnosticStatus.NotTested)
            values[componentId] = values[componentId] with
            {
                Status = status,
                Evidence = $"PhoneLab Agent: {test.Evidence} (UTC: {test.CompletedAtUtc?.ToString("u") ?? "not recorded"})"
            };
    }
}
