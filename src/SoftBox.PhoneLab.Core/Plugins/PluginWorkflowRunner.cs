using System.IO.Compression;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Workflow;

namespace SoftBox.PhoneLab.Core.Plugins;

public sealed record PluginWorkflowStepResult(string PresetId, bool Success, string Output, string? Error);
public sealed record PluginWorkflowRunReport(string WorkflowName, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, IReadOnlyList<PluginWorkflowStepResult> Steps)
{
    public bool Success => Steps.Count > 0 && Steps.All(step => step.Success);
}

/// <summary>Runs signed data-only plugins through SoftBox's read-only Android preset allow-list. Never loads DLLs.</summary>
public sealed class PluginWorkflowRunner
{
    private const int MaximumWorkflowBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, MaxDepth = 12 };

    public async Task<PluginWorkflowRunReport> RunAsync(
        string packagePath,
        PluginManifest manifest,
        IReadOnlySet<string> grantedPermissions,
        Func<string, CancellationToken, Task<string>> executePresetAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(grantedPermissions);
        ArgumentNullException.ThrowIfNull(executePresetAsync);
        if (!string.Equals(manifest.ExecutionMode, "read-only-workflow", StringComparison.Ordinal) ||
            !string.Equals(manifest.EntryAssembly, "workflow.json", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("مشغل الإضافات الحالي يقبل Workflow بيانات فقط، ولا يحمل DLL.");
        if (manifest.RequestedPermissions is null ||
            manifest.RequestedPermissions.Any(permission =>
                permission is not (PluginPermissionIds.DeviceRead or PluginPermissionIds.DeviceDiagnostics)))
            throw new UnauthorizedAccessException("إضافات Workflow تقبل صلاحيات القراءة والتشخيص فقط.");
        foreach (var permission in manifest.RequestedPermissions)
            if (!grantedPermissions.Contains(permission))
                throw new UnauthorizedAccessException("لم يمنح المستخدم الصلاحية المطلوبة: " + permission);

        if (!File.Exists(packagePath))
            throw new FileNotFoundException("حزمة الإضافة غير موجودة.", packagePath);

        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Where(entry => entry.FullName == "workflow.json").ToArray();
        if (entries.Length != 1 || entries[0].Length is <= 0 or > MaximumWorkflowBytes)
            throw new InvalidDataException("حزمة Workflow يجب أن تحتوي workflow.json واحدًا بحجم لا يتجاوز 64 KiB.");

        ReadOnlyWorkflowDefinition? raw;
        await using (var stream = entries[0].Open())
            raw = await JsonSerializer.DeserializeAsync<ReadOnlyWorkflowDefinition>(stream, JsonOptions, cancellationToken);
        var workflow = ReadOnlyWorkflowPolicy.Validate(raw);
        var started = DateTimeOffset.UtcNow;
        var results = new List<PluginWorkflowStepResult>();
        foreach (var step in workflow.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var output = await executePresetAsync(step.PresetId, cancellationToken);
                results.Add(new PluginWorkflowStepResult(step.PresetId, true, Limit(output), null));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                results.Add(new PluginWorkflowStepResult(step.PresetId, false, "", ex.Message));
                break;
            }
        }

        return new PluginWorkflowRunReport(workflow.Name, started, DateTimeOffset.UtcNow, results);
    }

    private static string Limit(string? value)
        => string.IsNullOrEmpty(value) ? "" : value.Length <= 32_000 ? value : value[..32_000] + "\n[truncated]";
}
