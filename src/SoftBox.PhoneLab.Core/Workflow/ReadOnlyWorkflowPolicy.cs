namespace SoftBox.PhoneLab.Core.Workflow;

public sealed record ReadOnlyWorkflowStep(string PresetId);

public sealed record ReadOnlyWorkflowDefinition(
    int SchemaVersion,
    string Name,
    IReadOnlyList<ReadOnlyWorkflowStep> Steps);

/// <summary>
/// Defines the only operations allowed in the initial Android workflow engine.
/// No user-authored shell, file deletion, flashing, lock bypass or authentication operation is accepted.
/// </summary>
public static class ReadOnlyWorkflowPolicy
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumSteps = 20;
    public const int MaximumNameLength = 80;

    private static readonly IReadOnlyDictionary<string, string> AllowedSteps =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["properties"] = "قراءة خصائص Android",
            ["battery"] = "قراءة معلومات البطارية",
            ["storage"] = "قراءة مساحة التخزين",
            ["uptime"] = "قراءة وقت تشغيل النظام",
            ["third-party-apps"] = "قراءة التطبيقات الخارجية",
            ["services"] = "قراءة خدمات Android"
        };

    public static IReadOnlyDictionary<string, string> AvailableSteps => AllowedSteps;

    public static ReadOnlyWorkflowDefinition Validate(ReadOnlyWorkflowDefinition? workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (workflow.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"إصدار Workflow غير مدعوم: {workflow.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(workflow.Name) || workflow.Name.Trim().Length > MaximumNameLength)
            throw new InvalidDataException($"اسم Workflow مطلوب وبحد أقصى {MaximumNameLength} حرفًا.");
        if (workflow.Steps is null || workflow.Steps.Count is < 1 or > MaximumSteps)
            throw new InvalidDataException($"يجب أن يحتوي Workflow على 1 إلى {MaximumSteps} خطوة.");

        foreach (var step in workflow.Steps)
        {
            if (step is null || string.IsNullOrWhiteSpace(step.PresetId) ||
                !AllowedSteps.ContainsKey(step.PresetId))
                throw new InvalidDataException("يحتوي Workflow على خطوة غير مسموح بها. أوامر Shell الحرة غير مدعومة.");
        }

        return workflow with
        {
            Name = workflow.Name.Trim(),
            Steps = workflow.Steps.ToArray()
        };
    }

    public static string GetStepLabel(string presetId)
        => AllowedSteps.TryGetValue(presetId, out var label)
            ? label
            : throw new ArgumentException("معرف الخطوة غير مدعوم.", nameof(presetId));
}
