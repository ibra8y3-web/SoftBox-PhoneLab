using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SoftBox.PhoneLab.Core.Diagnostics;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record QualcommQdlCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc)
{
    public bool Success => ExitCode == 0;
    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput.Trim(), StandardError.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
}

/// <summary>
/// Executes the public Qualcomm QDL command-line tool using fixed argument lists.
/// No shell, command-string interpolation, authentication bypass or generic command API is exposed.
/// Flash is only available from a structurally inspected OEM package after an explicit dry-run.
/// </summary>
public sealed class QualcommQdlRunner
{
    private const int MaximumOutputCharacters = 4 * 1024 * 1024;
    private readonly string _executable;

    public QualcommQdlRunner(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            throw new FileNotFoundException("Qualcomm QDL executable was not found.", executablePath);
        _executable = Path.GetFullPath(executablePath);
        if (!Path.GetExtension(_executable).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select the Windows qdl.exe executable built from the official Qualcomm QDL source.");
    }

    public string ExecutablePath => _executable;

    public Task<QualcommQdlCommandResult> ListDevicesAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(["list"], TimeSpan.FromSeconds(25), cancellationToken);

    public Task<QualcommQdlCommandResult> ReadChipInfoAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        return ExecuteAsync(["--serial=" + serial, "chipinfo"], TimeSpan.FromSeconds(45), cancellationToken);
    }

    public Task<QualcommQdlCommandResult> DryRunAsync(
        QualcommQdlPackageReport package,
        string serial,
        QualcommQdlStorageType storageType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!package.IsStructurallyValid)
            throw new InvalidOperationException("Qualcomm package must pass structural inspection before QDL dry-run.");
        ValidateSerial(serial);
        return ExecuteAsync(
            QualcommQdlPackageInspector.BuildFlashArguments(package, serial, storageType, dryRun: true),
            TimeSpan.FromMinutes(5),
            cancellationToken);
    }

    public Task<QualcommQdlCommandResult> FlashAsync(
        QualcommQdlPackageReport package,
        string serial,
        QualcommQdlStorageType storageType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!package.IsStructurallyValid)
            throw new InvalidOperationException("Qualcomm package must pass structural inspection before flashing.");
        ValidateSerial(serial);
        return ExecuteAsync(
            QualcommQdlPackageInspector.BuildFlashArguments(package, serial, storageType, dryRun: false),
            TimeSpan.FromMinutes(45),
            cancellationToken);
    }

    private async Task<QualcommQdlCommandResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var workingDirectory = Path.GetDirectoryName(_executable)!;
        var start = new ProcessStartInfo(_executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.Environment["PATH"] = string.Join(
            Path.PathSeparator.ToString(),
            new[] { workingDirectory, Environment.GetEnvironmentVariable("PATH") ?? "" }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        var startedAt = DateTimeOffset.UtcNow;
        if (!process.Start())
            throw new InvalidOperationException("Windows did not start qdl.exe.");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var stdoutTask = ReadBoundedAsync(process.StandardOutput, timeoutSource.Token);
        var stderrTask = ReadBoundedAsync(process.StandardError, timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new QualcommQdlCommandResult(
                process.ExitCode, stdout, stderr, startedAt, DateTimeOffset.UtcNow);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                return builder.ToString();
            if (builder.Length + read > MaximumOutputCharacters)
                throw new InvalidDataException("QDL output exceeded the 4 MiB safety limit.");
            builder.Append(buffer, 0, read);
        }
    }

    private static void ValidateSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial) || serial.Length > 128 ||
            serial.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                                    character is not ('-' or '_' or '.')))
            throw new ArgumentException("Qualcomm EDL serial is invalid.", nameof(serial));
    }
}
