namespace SoftBox.PhoneLab.Core.Models;

public enum DiagnosticStatus
{
    NotTested,
    DataRead,
    Pass,
    Review,
    Failed,
    Unavailable
}

public sealed record AgentTestResult
{
    public string Status { get; init; } = "not_tested";
    public string Evidence { get; init; } = "";
    public DateTimeOffset? CompletedAtUtc { get; init; }
}

public sealed record AgentCameraInfo
{
    public string Id { get; init; } = "";
    public string ParentId { get; init; } = "";
    public string? PhysicalId { get; init; }
    public string Facing { get; init; } = "unknown";
    public string HardwareLevel { get; init; } = "";
    public bool HasFlash { get; init; }
    public double[] FocalLengthsMm { get; init; } = [];
    public string[] AutoFocusModes { get; init; } = [];
    public double? MinimumFocusDistanceDiopters { get; init; }
    public string SensorResolution { get; init; } = "";
    public string[] PhysicalCameraIds { get; init; } = [];
    public bool IsPhysicalOnly { get; init; }
}

public sealed record AgentSnapshot
{
    public string AgentVersion { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Model { get; init; } = "";
    public string AndroidVersion { get; init; } = "";
    public string Brand { get; init; } = "";
    public string Product { get; init; } = "";
    public string Device { get; init; } = "";
    public string Board { get; init; } = "";
    public string Hardware { get; init; } = "";
    public string Bootloader { get; init; } = "";
    public string[] SupportedAbis { get; init; } = [];
    public long? BatteryChargeCounterMicroAh { get; init; }
    public int? BatteryCurrentNowMicroA { get; init; }
    public int? BatteryCurrentAverageMicroA { get; init; }
    public long? BatteryEnergyCounterNWh { get; init; }
    public int? BatteryCycleCount { get; init; }
    public int? BatteryVoltageMillivolts { get; init; }
    public double? BatteryTemperatureCelsius { get; init; }
    public double? BatteryCapacityEstimateMah { get; init; }
    public List<AgentCameraInfo> Cameras { get; init; } = [];
    public Dictionary<string, AgentTestResult> Tests { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Sensors { get; init; } = [];
    public List<string> HardwareFeatures { get; init; } = [];
}

public sealed record ComponentDiagnostic
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public DiagnosticStatus Status { get; init; }
    public string Evidence { get; init; } = "لم يتم الاختبار";
    public string Description { get; init; } = "";
    public string StatusLabel => Status switch
    {
        DiagnosticStatus.DataRead => "بيانات مقروءة",
        DiagnosticStatus.Pass => "اختبار ناجح",
        DiagnosticStatus.Review => "يحتاج مراجعة",
        DiagnosticStatus.Failed => "فشل مثبت",
        DiagnosticStatus.Unavailable => "غير متاح",
        _ => "لم يُختبر"
    };
    public string StatusColor => Status switch
    {
        DiagnosticStatus.DataRead => "#38BDF8",
        DiagnosticStatus.Pass => "#22C55E",
        DiagnosticStatus.Review => "#F59E0B",
        DiagnosticStatus.Failed => "#EF4444",
        DiagnosticStatus.Unavailable => "#475569",
        _ => "#64748B"
    };
}
