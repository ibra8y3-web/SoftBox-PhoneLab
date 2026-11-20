using System.Diagnostics;
using System.IO;
using System.Text;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record FastbootDevice(string Serial, string Details)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(Details) ? Serial : $"{Serial}  |  {Details}";
}

/// <summary>
/// Runs a deliberately restricted set of Fastboot operations. No flashing, unlocking,
/// erase, format, boot, or arbitrary command API is exposed by this service.
/// </summary>
public sealed class FastbootService
{
    private readonly string? _executable;
    private const int MaximumOutputCharacters = 1_000_000;

    public FastbootService()
    {
        var candidates = new List<string>();
        var environmentPath = Environment.GetEnvironmentVariable("SOFTBOX_FASTBOOT");
        if (!string.IsNullOrWhiteSpace(environmentPath))
            candidates.Add(environmentPath);

        var adbOverride = Environment.GetEnvironmentVariable("SOFTBOX_ADB");
        if (!string.IsNullOrWhiteSpace(adbOverride))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(adbOverride));
            if (!string.IsNullOrWhiteSpace(directory))
                candidates.Add(Path.Combine(directory, "fastboot.exe"));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Tools", "platform-tools", "fastboot.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Tools", "fastboot.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "fastboot.exe"));
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim(), "fastboot.exe")));
        _executable = candidates.FirstOrDefault(File.Exists);
    }

    public FastbootService(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Fastboot executable path is required.", nameof(executablePath));
        _executable = Path.GetFullPath(executablePath);
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_executable) && File.Exists(_executable);
    public string Executable => _executable ?? "fastboot.exe not found";

    public async Task<IReadOnlyList<FastbootDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var output = await ExecuteAsync(["devices", "-l"], cancellationToken, TimeSpan.FromSeconds(20));
        return ParseDevices(output);
    }

    public Task<string> ReadAllVariablesAsync(string serial, CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        return ExecuteAsync(["-s", serial, "getvar", "all"], cancellationToken, TimeSpan.FromSeconds(25));
    }

    public Task<string> RebootSystemAsync(string serial, CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        return ExecuteAsync(["-s", serial, "reboot"], cancellationToken, TimeSpan.FromSeconds(20));
    }

    public Task<string> RebootRecoveryAsync(string serial, CancellationToken cancellationToken = default)
    {
        var arguments = BuildRebootRecoveryArguments(serial);
        return ExecuteAsync(arguments, cancellationToken, TimeSpan.FromSeconds(20));
    }

    public static IReadOnlyList<string> BuildRebootRecoveryArguments(string serial)
    {
        ValidateSerial(serial);
        return ["-s", serial, "reboot", "recovery"];
    }

    public Task<string> RebootFastbootdAsync(string serial, CancellationToken cancellationToken = default)
    {
        var arguments = BuildRebootFastbootdArguments(serial);
        return ExecuteAsync(arguments, cancellationToken, TimeSpan.FromSeconds(20));
    }

    public static IReadOnlyList<string> BuildRebootFastbootdArguments(string serial)
    {
        ValidateSerial(serial);
        return ["-s", serial, "reboot", "fastboot"];
    }

    public static IReadOnlyList<FastbootDevice> ParseDevices(string output)
    {
        var devices = new List<FastbootDevice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (output ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 ||
                !AdbClient.IsSafeSerial(parts[0]) ||
                !parts[1].Equals("fastboot", StringComparison.OrdinalIgnoreCase) ||
                !seen.Add(parts[0]))
                continue;

            devices.Add(new FastbootDevice(parts[0], string.Join(" ", parts.Skip(2))));
        }
        return devices;
    }

    private async Task<string> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        if (!IsAvailable)
            throw new FileNotFoundException(
                "fastboot.exe غير موجود. ثبّت Android SDK Platform-Tools أو ضعه في Tools/platform-tools بجوار adb.exe.");

        var start = new ProcessStartInfo(_executable!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("تعذر تشغيل fastboot.exe.");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var output = string.Join(Environment.NewLine, new[] { stdout.Trim(), stderr.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            if (output.Length > MaximumOutputCharacters)
                throw new InvalidDataException("مخرجات Fastboot تجاوزت الحد الآمن.");

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(output) ? $"Fastboot exited with code {process.ExitCode}." : output);

            return output;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    private static void ValidateSerial(string serial)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف Fastboot غير صالح.", nameof(serial));
    }
}
