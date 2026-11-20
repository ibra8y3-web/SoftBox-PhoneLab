using System.IO;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Workflow;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record AndroidWorkflowStepResult(
    int Order,
    string PresetId,
    string DisplayName,
    bool Success,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    string Output,
    string? Error);

public sealed record AndroidWorkflowRunReport(
    string WorkflowName,
    string DeviceSerial,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    bool Success,
    IReadOnlyList<AndroidWorkflowStepResult> Steps);

/// <summary>
/// Stores and runs user-created workflows made only from the Core read-only ADB preset allow-list.
/// </summary>
public sealed class AndroidWorkflowService(AndroidDeviceToolsService devices)
{
    private const long MaximumWorkflowFileBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task SaveAsync(
        string path,
        ReadOnlyWorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        var validated = ReadOnlyWorkflowPolicy.Validate(workflow);
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("حدد مسار حفظ Workflow.", nameof(path));

        var target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 16 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, validated, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }

    public async Task<ReadOnlyWorkflowDefinition> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("ملف Workflow غير موجود.", path);
        if (info.Length > MaximumWorkflowFileBytes)
            throw new InvalidDataException("ملف Workflow أكبر من الحد الآمن.");

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var workflow = await JsonSerializer.DeserializeAsync<ReadOnlyWorkflowDefinition>(
            stream, JsonOptions, cancellationToken);
        return ReadOnlyWorkflowPolicy.Validate(workflow);
    }

    public async Task<AndroidWorkflowRunReport> RunAsync(
        string serial,
        ReadOnlyWorkflowDefinition workflow,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        var validated = ReadOnlyWorkflowPolicy.Validate(workflow);
        var started = DateTimeOffset.UtcNow;
        var results = new List<AndroidWorkflowStepResult>();
        var success = true;

        for (var index = 0; index < validated.Steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = validated.Steps[index];
            var displayName = ReadOnlyWorkflowPolicy.GetStepLabel(step.PresetId);
            var stepStarted = DateTimeOffset.UtcNow;
            progress?.Report($"الخطوة {index + 1}/{validated.Steps.Count}: {displayName}");

            try
            {
                var output = await devices.RunReadOnlyPresetAsync(serial, step.PresetId, cancellationToken);
                const int maximumOutputCharacters = 256 * 1024;
                if (output.Length > maximumOutputCharacters)
                    output = output[..maximumOutputCharacters] + Environment.NewLine + "[Output truncated at 256 KiB.]";

                results.Add(new AndroidWorkflowStepResult(
                    index + 1, step.PresetId, displayName, true, stepStarted,
                    DateTimeOffset.UtcNow, output, null));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                success = false;
                results.Add(new AndroidWorkflowStepResult(
                    index + 1, step.PresetId, displayName, false, stepStarted,
                    DateTimeOffset.UtcNow, "", ex.Message));
                break;
            }
        }

        return new AndroidWorkflowRunReport(
            validated.Name,
            serial,
            started,
            DateTimeOffset.UtcNow,
            success && results.Count == validated.Steps.Count,
            results);
    }
}
