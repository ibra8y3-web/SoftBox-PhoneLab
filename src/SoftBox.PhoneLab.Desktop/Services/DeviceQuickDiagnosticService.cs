using System.IO;
using SoftBox.PhoneLab.Core.Activity;
using SoftBox.PhoneLab.Core.Compatibility;
using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Models;
using System.Text.Json;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record QuickDiagnosticStep(
    int Order,
    string PresetId,
    string DisplayName,
    bool Success,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    string Output,
    string? Error);

public sealed record OneClickDiagnosticReport(
    string DeviceFingerprint,
    string MaskedSerial,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    DeviceSnapshot? Device,
    AgentSnapshot? Agent,
    IReadOnlyList<ComponentDiagnostic> ComponentDiagnostics,
    IReadOnlyList<QuickDiagnosticStep> Steps,
    DeviceDiagnosticEnginePlan EnginePlan,
    RepairAdviceReport Advice,
    IReadOnlyList<string> Limitations);

/// <summary>
/// Runs a bounded sequence of read-only ADB presets and assembles the results with current device/Agent evidence.
/// It never installs, removes, flashes, resets, or changes device settings.
/// </summary>
public sealed class DeviceQuickDiagnosticService(
    AndroidDeviceToolsService android,
    DeviceDiagnosticEngineSettingsStore? settingsStore = null)
{
    private readonly DeviceDiagnosticEngineSettingsStore _settingsStore =
        settingsStore ?? new DeviceDiagnosticEngineSettingsStore();
    private readonly DeviceProfileCatalog _profileCatalog = new();
    private static readonly (string Id, string Label)[] Steps =
    [
        ("properties", "خصائص النظام ومعلومات البناء"),
        ("battery", "حالة البطارية والقراءات المتاحة"),
        ("storage", "مساحة التخزين المتاحة"),
        ("uptime", "وقت تشغيل النظام"),
        ("third-party-apps", "قائمة التطبيقات الخارجية")
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<OneClickDiagnosticReport> RunAsync(
        string serial,
        DeviceSnapshot? device,
        AgentSnapshot? agent,
        IReadOnlyList<ComponentDiagnostic> componentDiagnostics,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));

        var started = DateTimeOffset.UtcNow;
        var settingsLoad = await _settingsStore.LoadAsync(cancellationToken);
        var commandTimeout = TimeSpan.FromSeconds(settingsLoad.Settings.AdbCommandTimeoutSeconds);
        var maximumOutputBytes = settingsLoad.Settings.MaximumOutputBytes;
        var results = new List<QuickDiagnosticStep>();
        for (var index = 0; index < Steps.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (id, label) = Steps[index];
            var stepStarted = DateTimeOffset.UtcNow;
            progress?.Report($"الفحص {index + 1}/{Steps.Length}: {label}");

            try
            {
                var output = await android.RunReadOnlyPresetAsync(serial, id, cancellationToken, commandTimeout);
                if (output.Length > maximumOutputBytes)
                    output = output[..maximumOutputBytes] + Environment.NewLine +
                        $"[Output truncated at configured limit of {maximumOutputBytes} bytes.]";
                results.Add(new QuickDiagnosticStep(
                    index + 1, id, label, true, stepStarted, DateTimeOffset.UtcNow, output, null));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                results.Add(new QuickDiagnosticStep(
                    index + 1, id, label, false, stepStarted, DateTimeOffset.UtcNow, "", ex.Message));
            }
        }

        var immutableComponents = componentDiagnostics.ToArray();
        var advice = DeviceRepairAdvisor.Analyze(device, agent, immutableComponents, adbAuthorized: true);
        var limitations = new List<string>
        {
            "هذا الفحص يجمع قراءات ADB غير المدمرة فقط؛ لا ينفذ اختبارات شاشة أو صوت أو كاميرا أو حساسات.",
            "اختبارات العتاد تتطلب تنفيذًا منفصلًا وموافقة المستخدم في شاشة Hardware Diagnostics.",
            "فشل أمر ADB قد ينتج عن الصلاحيات أو إصدار Android أو قيود الشركة ولا يثبت عطلًا ماديًا.",
            "معرف الجهاز في التقرير بصمة SHA-256 ورقم مقنّع؛ لم يتم تضمين Serial الخام."
        };
        if (agent is null)
            limitations.Add("لم يتم إرفاق نتائج Agent؛ لا توجد نتائج اختبارات عتاد تلقائية في هذا التقرير.");

        var safeDevice = device is null ? null : device with { Serial = "" };
        limitations.AddRange(settingsLoad.Warnings);
        DeviceProfile? matchedProfile = null;
        if (device is not null)
        {
            try
            {
                var catalog = await _profileCatalog.LoadAsync();
                matchedProfile = DeviceCapabilityEngine.ResolveIdentity(device, catalog.Profiles).Profile;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
            {
                limitations.Add("تعذر تحميل ملف تعريف الموديل لمحرك التشخيص؛ استخدم المحرك المسار العام. " + ex.Message);
            }
        }
        var enginePlan = DeviceDiagnosticEngine.Build(new DeviceDiagnosticEngineContext(
            device, matchedProfile, AdbAuthorized: true, AgentConnected: agent is not null,
            FastbootDetected: false, Settings: settingsLoad.Settings, AuthorizedSerial: serial));
        limitations.Add("محرك المعالج يختار خطة تشخيص حسب SoC المبلغ عنه أو ملف موثوق؛ لا يرسل بروتوكولات OEM خاصة ولا ينفذ تفليشًا.");
        limitations.Add("حدود حرارة البطارية في هذا المحرك هي تنبيهات مراجعة محلية قابلة للتعديل وليست حدود أمان OEM.");
        return new OneClickDiagnosticReport(
            LocalActivityStore.CreateDeviceFingerprint(serial),
            LocalActivityStore.MaskSerial(serial),
            started,
            DateTimeOffset.UtcNow,
            safeDevice,
            agent,
            immutableComponents,
            results,
            enginePlan,
            advice,
            limitations);
    }

    public static string Serialize(OneClickDiagnosticReport report)
        => JsonSerializer.Serialize(report, JsonOptions);
}
