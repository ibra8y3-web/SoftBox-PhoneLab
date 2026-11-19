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
    public Dictionary<string, AgentTestResult> Tests { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Sensors { get; init; } = [];
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
