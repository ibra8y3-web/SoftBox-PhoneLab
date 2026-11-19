using System.Text.RegularExpressions;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Core.Diagnostics;

public static class DiagnosticEngine
{
    private static readonly (string Id, string Name, string Description)[] Catalog =
    [
        ("display", "الشاشة", "اختبار ألوان بصري بموافقة المستخدم؛ تحليل صورة فعلية يعطي مؤشرات مرشحة فقط."),
        ("touch", "طبقة اللمس", "اختبار شبكة تغطية لمس تفاعلي؛ لا يقيس قوة الضغط أو كل مستويات الحساسية."),
        ("front-camera", "الكاميرا الأمامية", "Camera2 inventory and independent still-capture by camera route."),
        ("rear-camera", "الكاميرات الخلفية", "Camera2 inventory and independent still-capture by camera route."),
        ("camera-focus", "التركيز التلقائي للكاميرات", "قراءة نتيجة AF من Camera2؛ لا تعادل قياس حدة صورة على لوحة اختبار بصري."),
        ("earpiece", "سماعة المكالمات", "نغمة على قناة المكالمات مع تأكيد المستخدم."),
        ("flashlight", "فلاش LED", "نبضة Torch قصيرة مع تأكيد المستخدم."),
        ("nfc", "NFC", "إثبات وجود ميزة NFC في النظام لا يثبت قراءة البطاقة."),
        ("fingerprint", "مستشعر البصمة", "إثبات وجود الميزة لا يختبر حساس البصمة أو جودة التعرف."),
        ("biometrics", "القياسات الحيوية", "جرد ميزات النظام فقط؛ لا يتم قراءة بيانات بيومترية."),
        ("wifi", "Wi-Fi", "جرد ميزة Wi-Fi فقط؛ اختبار الاتصال الفعلي يحتاج شبكة."),
        ("bluetooth", "Bluetooth", "جرد ميزة Bluetooth فقط؛ لا يثبت الاقتران."),
        ("gps", "GPS", "جرد ميزة الموقع فقط؛ لا يثبت دقة تحديد الموقع."),
        ("mainboard", "اللوحة الأم", "بيانات اللوحة لا تثبت سلامة المسارات أو اللحام."),
        ("processor", "المعالج / SoC", "قراءة هوية المعالج لا تثبت السلامة الكهربائية."),
        ("ram", "ذاكرة RAM", "قراءة الذاكرة المتاحة لا تعادل اختبار خلايا RAM."),
        ("storage", "التخزين الداخلي", "المساحة المتاحة لا تعادل فحص NAND/UFS."),
        ("pmic", "آيسي إدارة الطاقة PMIC", "يتطلب قياسًا كهربائيًا على اللوحة عبر أداة متوافقة."),
        ("charge-ic", "آيسي الشحن", "يتطلب اختبار مسار الشحن وقياس التيار والفولتية."),
        ("battery", "البطارية", "بيانات Fuel Gauge ليست اختبار سعة أو مقاومة داخلية."),
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

    public static List<ComponentDiagnostic> Build(DeviceSnapshot? device, AgentSnapshot? agent, ExternalMeasurementSample? measurement = null)
    {
        var results = Catalog.Select(item => new ComponentDiagnostic
        {
            Id = item.Id, Name = item.Name, Description = item.Description,
            Status = DiagnosticStatus.NotTested, Evidence = "لم يتم إجراء اختبار وظيفي موثق."
        }).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        if (device is not null)
        {
            Read(results, "processor", device.Soc != "غير متاح" || device.CpuDescription != "غير متاح",
                $"SoC: {device.Soc}; CPU: {device.CpuDescription}; cores: {device.CpuCores}. قراءة تعريفية فقط.");
            Read(results, "ram", device.RamGiB.HasValue, device.RamGiB.HasValue ? $"RAM reported: {device.RamGiB.Value:0.0} GiB. لم يتم اختبار خلايا الذاكرة." : "");
            Read(results, "storage", device.DataTotalKiB.HasValue,
                device.DataTotalKiB.HasValue ? $"قسم /data: {device.DataUsedKiB / 1024d / 1024d:0.0} / {device.DataTotalKiB / 1024d / 1024d:0.0} GiB." : "");
            Read(results, "display", device.DisplayResolution != "غير متاح",
                $"الدقة: {device.DisplayResolution}، الكثافة: {device.DisplayDensity}. لا يثبت ذلك سلامة البكسلات.");
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

            if (agent.Cameras.Count > 0)
            {
                ApplyCameraGroup(results, agent, "front");
                ApplyCameraGroup(results, agent, "back");
                ApplyCameraFocusGroup(results, agent);
            }
            ApplyAgent(results, agent, "earpiece", "earpiece");
            ApplyAgent(results, agent, "flashlight", "flashlight");
            if (agent.HardwareFeatures.Count > 0)
            {
                var features = agent.HardwareFeatures;
                Read(results, "nfc", features.Contains("android.hardware.nfc", StringComparer.OrdinalIgnoreCase), "Android declares NFC support; card reading was not tested.");
                Read(results, "fingerprint", features.Contains("android.hardware.fingerprint", StringComparer.OrdinalIgnoreCase), "Android declares fingerprint support; biometric recognition was not tested.");
                Read(results, "biometrics", features.Any(x => x.Contains("biometric", StringComparison.OrdinalIgnoreCase)), "Android biometric capability was enumerated; no biometric data was accessed.");
                Read(results, "wifi", features.Contains("android.hardware.wifi", StringComparer.OrdinalIgnoreCase), "Wi-Fi feature declared; network association was not tested.");
                Read(results, "bluetooth", features.Contains("android.hardware.bluetooth", StringComparer.OrdinalIgnoreCase), "Bluetooth feature declared; pairing and RF quality were not tested.");
                Read(results, "gps", features.Contains("android.hardware.location.gps", StringComparer.OrdinalIgnoreCase), "GPS feature declared; live fix and accuracy were not tested.");
            }

            if (agent.BatteryChargeCounterMicroAh.HasValue || agent.BatteryCurrentNowMicroA.HasValue ||
                agent.BatteryCurrentAverageMicroA.HasValue || agent.BatteryCycleCount.HasValue)
            {
                var evidence = $"Charge counter: {agent.BatteryChargeCounterMicroAh?.ToString() ?? "N/A"} µAh; " +
                    $"current now: {agent.BatteryCurrentNowMicroA?.ToString() ?? "N/A"} µA; " +
                    $"average: {agent.BatteryCurrentAverageMicroA?.ToString() ?? "N/A"} µA; " +
                    $"cycles: {agent.BatteryCycleCount?.ToString() ?? "N/A"}. Fuel Gauge telemetry only; not a calibrated capacity test.";
                results["battery"] = results["battery"] with { Status = DiagnosticStatus.DataRead, Evidence = evidence };
            }
            ApplyAgent(results, agent, "battery_capacity", "battery");
            if (agent.BatteryCapacityEstimateMah is double capacityEstimate)
            {
                var currentBattery = results["battery"];
                results["battery"] = currentBattery with
                {
                    Status = currentBattery.Status == DiagnosticStatus.Failed ? DiagnosticStatus.Failed : DiagnosticStatus.Review,
                    Evidence = $"Fuel Gauge capacity estimate: {capacityEstimate:0} mAh. This remains a review-only estimate, including after Agent restart. " + currentBattery.Evidence
                };
            }
            if (agent.Sensors.Count > 0)
                results["sensors"] = results["sensors"] with
                {
                    Status = DiagnosticStatus.DataRead,
                    Evidence = "Sensors exposed by Android: " + string.Join(", ", agent.Sensors)
                };
        }
        if (measurement is not null)
        {
            var evidence = $"Instrument: {measurement.Instrument}; USB={measurement.UsbVoltageV?.ToString("0.000") ?? "N/A"} V; " +
                $"battery={measurement.BatteryVoltageV?.ToString("0.000") ?? "N/A"} V; current={measurement.BatteryCurrentA?.ToString("0.000") ?? "N/A"} A; " +
                $"board temperature={measurement.BoardTemperatureC?.ToString("0.0") ?? "N/A"} °C; power={measurement.UsbPowerW?.ToString("0.000") ?? "N/A"} W.";
            Read(results, "usb-port", measurement.UsbVoltageV.HasValue, evidence + " Meter observation; no electrical pass/fail threshold was applied.");
            if (measurement.BatteryVoltageV.HasValue || measurement.BatteryCurrentA.HasValue ||
                measurement.BatteryCapacityMah.HasValue || measurement.BatteryInternalResistanceMilliOhm.HasValue)
            {
                var currentBattery = results["battery"];
                var batteryStatus = measurement.BatteryCapacityMah.HasValue || measurement.BatteryInternalResistanceMilliOhm.HasValue ||
                    currentBattery.Status == DiagnosticStatus.Review
                    ? DiagnosticStatus.Review : DiagnosticStatus.DataRead;
                results["battery"] = currentBattery with
                {
                    Status = currentBattery.Status == DiagnosticStatus.Failed ? DiagnosticStatus.Failed : batteryStatus,
                    Evidence = evidence +
                        $" Capacity={measurement.BatteryCapacityMah?.ToString("0") ?? "N/A"} mAh; " +
                        $"internal resistance={measurement.BatteryInternalResistanceMilliOhm?.ToString("0.0") ?? "N/A"} mΩ. " +
                        "External instrument values are observations; pass/fail limits require the correct battery type and calibrated fixture."
                };
            }
            if (measurement.Rails is { Count: > 0 })
            {
                var rails = string.Join(", ", measurement.Rails.Select(x => $"{x.Key}={x.Value:0.000} V"));
                Read(results, "pmic", true, "Measured rails: " + rails + ". Values are raw; limits require exact board documentation.");
                Read(results, "charge-ic", true, "Measured rails: " + rails + ". Pin names and safe thresholds must come from the exact schematic.");
            }
            if (measurement.BoardTemperatureC.HasValue)
                Read(results, "mainboard", true, evidence + " External temperature observation; hotspot localization was not performed.");
        }
        return Catalog.Select(x => results[x.Id]).ToList();
    }

    private static void ApplyCameraGroup(Dictionary<string, ComponentDiagnostic> values, AgentSnapshot agent, string facing)
    {
        var cameras = agent.Cameras.Where(x => x.Facing.Equals(facing, StringComparison.OrdinalIgnoreCase)).ToList();
        if (cameras.Count == 0) return;
        var outcomes = new List<DiagnosticStatus>();
        var evidence = new List<string>();
        foreach (var camera in cameras)
        {
            var id = "camera_" + Regex.Replace(camera.Id, "[^A-Za-z0-9_-]", "_");
            if (agent.Tests.TryGetValue(id, out var test))
            {
                var status = MapStatus(test.Status);
                outcomes.Add(status);
                evidence.Add($"ID {camera.Id}: {status} — {test.Evidence}");
            }
            else
            {
                outcomes.Add(DiagnosticStatus.DataRead);
                evidence.Add($"ID {camera.Id}: تم التعرف على مسار الكاميرا ولم يُسجّل اختبار التقاط له.");
            }
        }

        var combined = outcomes.Contains(DiagnosticStatus.Failed) ? DiagnosticStatus.Failed :
            outcomes.Contains(DiagnosticStatus.Review) ? DiagnosticStatus.Review :
            outcomes.All(x => x == DiagnosticStatus.Pass) ? DiagnosticStatus.Pass :
            DiagnosticStatus.DataRead;
        var target = facing == "front" ? "front-camera" : "rear-camera";
        values[target] = values[target] with { Status = combined, Evidence = string.Join(" | ", evidence) };
    }

    private static void ApplyCameraFocusGroup(Dictionary<string, ComponentDiagnostic> values, AgentSnapshot agent)
    {
        var outcomes = new List<DiagnosticStatus>();
        var evidence = new List<string>();
        foreach (var camera in agent.Cameras)
        {
            var id = "camera_focus_" + Regex.Replace(camera.Id, "[^A-Za-z0-9_-]", "_");
            if (!agent.Tests.TryGetValue(id, out var test)) continue;
            var status = MapStatus(test.Status);
            if (status == DiagnosticStatus.NotTested) continue;
            outcomes.Add(status);
            evidence.Add($"Camera {camera.Id}: {status} — {test.Evidence}");
        }
        if (outcomes.Count == 0) return;
        var combined = outcomes.Contains(DiagnosticStatus.Failed) ? DiagnosticStatus.Failed :
            outcomes.Contains(DiagnosticStatus.Review) ? DiagnosticStatus.Review :
            outcomes.All(x => x == DiagnosticStatus.Pass) ? DiagnosticStatus.Pass : DiagnosticStatus.DataRead;
        values["camera-focus"] = values["camera-focus"] with { Status = combined, Evidence = string.Join(" | ", evidence) };
    }

    private static DiagnosticStatus MapStatus(string raw) => raw.ToLowerInvariant() switch
    {
        "passed" or "pass" => DiagnosticStatus.Pass,
        "failed" or "fail" => DiagnosticStatus.Failed,
        "review" or "warning" => DiagnosticStatus.Review,
        "data_read" => DiagnosticStatus.DataRead,
        _ => DiagnosticStatus.NotTested
    };

    private static void Read(Dictionary<string, ComponentDiagnostic> values, string id, bool available, string evidence)
    {
        if (available) values[id] = values[id] with { Status = DiagnosticStatus.DataRead, Evidence = evidence };
    }

    private static void ApplyAgent(Dictionary<string, ComponentDiagnostic> values, AgentSnapshot agent, string testId, string componentId)
    {
        if (!agent.Tests.TryGetValue(testId, out var test) || string.IsNullOrWhiteSpace(test.Status)) return;
        var status = MapStatus(test.Status);
        if (status != DiagnosticStatus.NotTested)
            values[componentId] = values[componentId] with
            {
                Status = status,
                Evidence = $"PhoneLab Agent: {test.Evidence} (UTC: {test.CompletedAtUtc?.ToString("u") ?? "not recorded"})"
            };
    }
}
