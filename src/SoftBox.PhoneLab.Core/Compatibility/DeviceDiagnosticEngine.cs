using System.Text.RegularExpressions;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Core.Compatibility;

public enum DiagnosticEngineIdentityConfidence
{
    ExactReportedSoc,
    VerifiedProfileFamily,
    GenericFallback,
    ConflictingSocEvidence
}

public enum DiagnosticProcedureState
{
    Available,
    NeedsSetup,
    Blocked,
    Unknown
}

public sealed record DiagnosticEngineSettings
{
    public int AdbCommandTimeoutSeconds { get; init; } = 20;
    public int AgentCommandTimeoutSeconds { get; init; } = 90;
    public int MaximumReportOutputCharacters { get; init; } = 262_144;
    public double BatteryReviewTemperatureCelsius { get; init; } = 45.0;
    public double BatteryCriticalTemperatureCelsius { get; init; } = 55.0;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (AdbCommandTimeoutSeconds is < 5 or > 90)
            errors.Add("ADB command timeout must be between 5 and 90 seconds.");
        if (AgentCommandTimeoutSeconds is < 10 or > 180)
            errors.Add("Agent command request timeout must be between 10 and 180 seconds.");
        if (MaximumReportOutputCharacters is < 16_384 or > 1_048_576)
            errors.Add("Saved diagnostic report output must be between 16,384 and 1,048,576 characters.");
        if (!double.IsFinite(BatteryReviewTemperatureCelsius) ||
            BatteryReviewTemperatureCelsius is < 35 or > 50)
            errors.Add("Battery review temperature must be between 35 and 50 °C.");
        if (!double.IsFinite(BatteryCriticalTemperatureCelsius) ||
            BatteryCriticalTemperatureCelsius is < 45 or > 70 ||
            BatteryCriticalTemperatureCelsius <= BatteryReviewTemperatureCelsius + 5)
            errors.Add("Battery critical temperature must be at least 5 °C above review and no greater than 70 °C.");
        return errors;
    }
}

public sealed record DeviceDiagnosticEngineContext(
    DeviceSnapshot? Device,
    DeviceProfile? Profile,
    bool AdbAuthorized,
    bool AgentConnected,
    bool FastbootDetected,
    bool VerifiedBoardMapAvailable = false,
    bool OemPackageSelected = false,
    DiagnosticEngineSettings? Settings = null,
    string? AuthorizedSerial = null);

public sealed record DiagnosticEngineProcedure(
    string Id,
    string DisplayName,
    string Transport,
    DiagnosticProcedureState State,
    bool CanExecute,
    string Evidence,
    string RequiredAction);

public sealed record DeviceDiagnosticEnginePlan(
    DateTimeOffset CreatedAtUtc,
    string EngineId,
    string EngineName,
    string EngineVersion,
    string HardwareFamily,
    DiagnosticEngineIdentityConfidence IdentityConfidence,
    DiagnosticEngineSettings Settings,
    IReadOnlyList<DiagnosticEngineProcedure> Procedures,
    IReadOnlyList<string> SafetyRules,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Selects a model/SoC-aware diagnostic strategy while keeping commands behind the existing
/// allow-listed ADB, Agent, Fastboot and OEM workflow services. This planner never emits raw
/// vendor packets, bootloader commands, flash commands or undocumented service protocols.
/// </summary>
public static class DeviceDiagnosticEngine
{
    private sealed record EngineDefinition(
        string Id,
        string Name,
        string HardwareFamily,
        Regex SocPattern,
        string OemReviewProcedureId,
        string OemReviewName);

    private static readonly EngineDefinition[] Engines =
    [
        new("qualcomm-qcom", "Qualcomm Platform Diagnostics", "Qualcomm Snapdragon / QCS / QCM",
            new(@"(?i)(qualcomm|snapdragon|\b(?:SM|SDM|QCM|QCS)\d{4,5}\b)", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "qualcomm-qdl-package-review", "Qualcomm QDL package / programmer review"),
        new("mediatek-mtk", "MediaTek Platform Diagnostics", "MediaTek Dimensity / Helio / MTK",
            new(@"(?i)(mediatek|\bMT\d{4,5}\b|\bhelio\b|\bdimensity\b)", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "mediatek-da-package-review", "MediaTek Scatter / DA / Auth package review"),
        new("samsung-exynos", "Samsung Exynos Diagnostics", "Samsung Exynos",
            new(@"(?i)\bexynos\s*\d{3,4}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "samsung-odin-package-review", "Samsung Odin package / UI evidence review"),
        new("google-tensor", "Google Tensor Diagnostics", "Google Tensor",
            new(@"(?i)\btensor(?:\s+g\d)?\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "google-factory-image-review", "Google Factory Image package review"),
        new("huawei-kirin", "HiSilicon / Kirin Diagnostics", "HiSilicon Kirin",
            new(@"(?i)(\bkirin\s*\d{3,4}\b|hisilicon)", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "huawei-firmware-review", "Huawei signed firmware metadata review"),
        new("unisoc-mobile", "Unisoc Platform Diagnostics", "Unisoc / Spreadtrum",
            new(@"(?i)(unisoc|spreadtrum)", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "unisoc-package-review", "Unisoc package provenance review")
    ];

    private static readonly DiagnosticEngineSettings DefaultSettings = new();

    public static DeviceDiagnosticEnginePlan Build(DeviceDiagnosticEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = context.Settings ?? DefaultSettings;
        var settingsErrors = settings.Validate();
        if (settingsErrors.Count > 0)
            throw new ArgumentOutOfRangeException(nameof(context), string.Join(" ", settingsErrors));

        var device = context.Device;
        var safeSerial = IsSafeSerial(context.AuthorizedSerial ?? device?.Serial);
        var adbReady = context.AdbAuthorized && safeSerial;
        var fastbootReady = context.FastbootDetected && safeSerial;
        var reportedSoc = device is not null && !IsUnknown(device.Soc) ? device.Soc : "";
        var verifiedProfileFamily = context.Profile is { IdentityVerified: true } profile
            ? string.Join(" ", new[] { profile.HardwareFamily }.Concat(profile.SocAliases))
            : "";
        var matchText = !string.IsNullOrWhiteSpace(reportedSoc) ? reportedSoc : verifiedProfileFamily;
        EngineDefinition[] matched = string.IsNullOrWhiteSpace(matchText)
            ? Array.Empty<EngineDefinition>()
            : Engines.Where(engine => engine.SocPattern.IsMatch(matchText)).ToArray();

        EngineDefinition? selected = matched.Length == 1 ? matched[0] : null;
        var confidence = matched.Length > 1
            ? DiagnosticEngineIdentityConfidence.ConflictingSocEvidence
            : selected is null
                ? DiagnosticEngineIdentityConfidence.GenericFallback
                : !string.IsNullOrWhiteSpace(reportedSoc)
                    ? DiagnosticEngineIdentityConfidence.ExactReportedSoc
                    : DiagnosticEngineIdentityConfidence.VerifiedProfileFamily;

        var engineId = selected?.Id ?? "android-generic";
        var engineName = selected?.Name ?? "Generic Android Diagnostic Engine";
        var family = selected?.HardwareFamily ?? "Android / SoC not conclusively identified";
        var procedures = new List<DiagnosticEngineProcedure>
        {
            ADB("android-properties", "Android properties / model identity", adbReady),
            ADB("battery-telemetry", "Battery / thermal telemetry", adbReady),
            ADB("storage-health-observations", "Storage capacity observations", adbReady),
            ADB("system-uptime", "System uptime and boot evidence", adbReady),
            ADB("installed-app-inventory", "Installed third-party app inventory", adbReady),
            new(
                "agent-hardware-diagnostics",
                "Agent hardware diagnostics (display, touch, cameras, sensors, radio, NFC and SIM)",
                "PhoneLab Agent / ADB forward",
                context.AgentConnected && adbReady
                    ? DiagnosticProcedureState.Available
                    : adbReady ? DiagnosticProcedureState.NeedsSetup : DiagnosticProcedureState.Blocked,
                context.AgentConnected && adbReady,
                context.AgentConnected && adbReady
                    ? "The Agent session is present; only implemented tests and declared Android capabilities can be invoked."
                    : "No active Agent bridge session was supplied to the planner.",
                "Install/open the official Agent, authorize the phone and establish its local bridge."),
            new(
                "fastboot-read-only",
                "Fastboot variables (read-only)",
                "Fastboot",
                fastbootReady ? DiagnosticProcedureState.Available : DiagnosticProcedureState.Unknown,
                fastbootReady,
                fastbootReady ? "The selected serial was independently detected in Fastboot." :
                    "ADB presence does not imply Fastboot mode or a Fastboot connection.",
                "Enter bootloader mode by the official device-specific method, then rediscover the exact serial.")
        };

        var oemReviewId = selected?.OemReviewProcedureId ??
            (IsSamsung(device?.Manufacturer) ? "samsung-odin-package-review" :
             IsXiaomi(device?.Manufacturer) ? "xiaomi-fastboot-package-review" : "");
        var oemReviewName = selected?.OemReviewName ??
            (IsSamsung(device?.Manufacturer) ? "Samsung Odin package / UI evidence review" :
             IsXiaomi(device?.Manufacturer) ? "Xiaomi Fastboot ROM metadata review" : "");

        if (!string.IsNullOrEmpty(oemReviewId))
        {
            procedures.Add(new DiagnosticEngineProcedure(
                oemReviewId,
                oemReviewName,
                "Local package inspector / official OEM utility",
                context.OemPackageSelected ? DiagnosticProcedureState.Available : DiagnosticProcedureState.NeedsSetup,
                false,
                "Package review is a local analysis/handoff workflow. It does not imply that a write or recovery operation is supported.",
                "Select the exact OEM package and run its dedicated package-inspection workflow."));
        }

        procedures.Add(new DiagnosticEngineProcedure(
            "private-oem-service-transport",
            "Private OEM service transport",
            "Vendor SDK / licensed service transport only",
            DiagnosticProcedureState.Blocked,
            false,
            "No licensed model-specific protocol implementation and hardware validation is registered.",
            "Register a signed OEM adapter backed by official documentation/SDK and verified physical-device tests."));

        procedures.Add(new DiagnosticEngineProcedure(
            "firmware-write",
            "Firmware write / bootloader unlock / erase",
            "OEM-specific service workflow",
            DiagnosticProcedureState.Blocked,
            false,
            "The diagnostic planner cannot elevate read-only evidence into a destructive capability.",
            "Use the existing independent fail-closed OEM workflow only after its model, signing, board and rollback gates are satisfied."));

        var warnings = new List<string>();
        if (confidence == DiagnosticEngineIdentityConfidence.GenericFallback)
            warnings.Add("No reliable SoC family match was found. Generic Android read-only procedures are used.");
        if (confidence == DiagnosticEngineIdentityConfidence.ConflictingSocEvidence)
            warnings.Add("More than one SoC family pattern matched. Vendor-specific selection was disabled.");
        if (context.Profile is { IdentityVerified: false })
            warnings.Add("The selected community model profile is an identity hint only; it is not an OEM-verified specification.");
        if (device?.BatteryTemperatureCelsius is double batteryTemperature)
        {
            if (batteryTemperature >= settings.BatteryCriticalTemperatureCelsius)
                warnings.Add($"CRITICAL BATTERY TEMPERATURE REVIEW: reported {batteryTemperature:0.0} °C is at/above the configured {settings.BatteryCriticalTemperatureCelsius:0.0} °C threshold. Stop stress testing and inspect safely; this is a telemetry alert, not a calibrated temperature certification.");
            else if (batteryTemperature >= settings.BatteryReviewTemperatureCelsius)
                warnings.Add($"BATTERY TEMPERATURE REVIEW: reported {batteryTemperature:0.0} °C is at/above the configured {settings.BatteryReviewTemperatureCelsius:0.0} °C review threshold.");
        }
        if (context.VerifiedBoardMapAvailable && context.Profile is not { IdentityVerified: true })
            warnings.Add("A BoardMap reference is not promoted to model-specific service capability without exact verified identity.");

        return new DeviceDiagnosticEnginePlan(
            DateTimeOffset.UtcNow,
            engineId,
            engineName,
            "1.0.0",
            family,
            confidence,
            settings,
            procedures,
            [
                "Every device operation is scoped to the exact selected serial; discovery is refreshed by its underlying service.",
                "Only fixed allow-listed read-only ADB presets are eligible for generic diagnostics.",
                "Agent procedures expose only tests actually implemented by the installed Agent and requested by the technician.",
                "Fastboot reads are separate from ADB and require current Fastboot discovery.",
                "SoC selection changes diagnostic guidance, not secure-boot, authorization, flashing or bootloader permissions.",
                "No factory PMIC rail, RF level, battery-health threshold or board pinout is inferred from marketing specifications.",
                "Model identity and published specifications never create PCB geometry or claim electrical health."
            ],
            warnings);
    }

    private static DiagnosticEngineProcedure ADB(string id, string name, bool authorized)
        => new(
            id, name, "ADB (fixed read-only preset)",
            authorized ? DiagnosticProcedureState.Available : DiagnosticProcedureState.Blocked,
            authorized,
            authorized ? "The current planning context confirms ADB authorization; the preset remains bounded and read-only." :
                "No current authorized ADB session was provided.",
            authorized ? "Run the matching preconfigured read-only diagnostic preset." :
                "Reconnect and approve USB debugging for this computer.");

    private static bool IsSafeSerial(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
           value.All(character => char.IsLetterOrDigit(character) ||
                                  character is '.' or '_' or ':' or '-');

    private static bool IsUnknown(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           value.Equals("غير متاح", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("n/a", StringComparison.OrdinalIgnoreCase);

    private static bool IsSamsung(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Contains("samsung", StringComparison.OrdinalIgnoreCase);

    private static bool IsXiaomi(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Contains("xiaomi", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("redmi", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("poco", StringComparison.OrdinalIgnoreCase));
}
