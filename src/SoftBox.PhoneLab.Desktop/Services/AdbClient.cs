using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>Executes a fixed ADB executable with argument-list APIs; no shell interpolation is used.</summary>
public sealed class AdbClient : IAdbCommandRunner
{
    private readonly string? _executable;

    public AdbClient()
    {
        var candidates = new List<string>();
        var env = Environment.GetEnvironmentVariable("SOFTBOX_ADB");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Tools", "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Tools", "adb.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "adb.exe"));
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Path.Combine(x.Trim(), "adb.exe")));
        _executable = candidates.FirstOrDefault(File.Exists);
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_executable) && File.Exists(_executable);

    public async Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        await RunAsync(["start-server"], cancellationToken);
    }
    public string Executable => _executable ?? "adb.exe not found";

    public Task<string> RunAsync(params string[] args) => RunAsync((IReadOnlyList<string>)args, CancellationToken.None);

    public async Task<string> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken, TimeSpan? commandTimeout = null)
    {
        if (!IsAvailable) throw new FileNotFoundException("ADB not found. Install official Android SDK Platform-Tools and add adb.exe to PATH or Tools/platform-tools.");
        var start = new ProcessStartInfo(_executable!)
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Could not start ADB.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(commandTimeout ?? TimeSpan.FromSeconds(16));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw;
        }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"ADB exited with {process.ExitCode}." : error.Trim());
        return output;
    }

    public Task<string> RunForDeviceAsync(string serial, params string[] args)
    {
        if (!IsSafeSerial(serial))
            throw new ArgumentException("Invalid ADB serial.", nameof(serial));
        var all = new List<string> { "-s", serial };
        all.AddRange(args);
        return RunAsync(all, CancellationToken.None);
    }

    public Task<string> RunForDeviceAsync(
        string serial,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken,
        TimeSpan? commandTimeout = null)
    {
        if (!IsSafeSerial(serial))
            throw new ArgumentException("Invalid ADB serial.", nameof(serial));
        var all = new List<string> { "-s", serial };
        all.AddRange(args);
        return RunAsync(all, cancellationToken, commandTimeout);
    }

    public async Task ForwardAgentPortAsync(string serial)
        => _ = await RunForDeviceAsync(serial, "forward", "tcp:27831", "tcp:27831");

    public static bool IsSafeSerial(string serial) =>
        !string.IsNullOrWhiteSpace(serial) &&
        !serial.StartsWith("-", StringComparison.Ordinal) &&
        Regex.IsMatch(serial, @"^[A-Za-z0-9._:-]{1,128}$");
}
