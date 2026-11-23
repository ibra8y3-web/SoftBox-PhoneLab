using System.Diagnostics;
using System.Text;
using SoftBox.PhoneLab.Core.Diagnostics;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record IPhoneDeviceScanResult(
    string MaskedIdentifier,
    IPhoneDeviceInfoSnapshot? Snapshot,
    string? Error)
{
    public string DisplayName => Snapshot is null
        ? $"iPhone / iPad • {MaskedIdentifier}"
        : $"{Snapshot.DeviceName} • {Snapshot.ProductType} • {MaskedIdentifier}";
    public bool IsConnectedAndReadable => Snapshot is not null && Error is null;
}

/// <summary>
/// Read-only Apple Lockdown discovery through a user-supplied libimobiledevice toolset.
/// No pairing, passcode entry, backup, restore, activation or device mutation is performed.
/// </summary>
public sealed class IPhoneDeviceDiagnosticsService
{
    private const int MaximumDevices = 8;
    private const int MaximumListOutput = 128 * 1024;
    private const int MaximumInfoOutput = 1024 * 1024;

    public static string? ResolveToolsDirectory(string? configuredDirectory)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
            candidates.Add(configuredDirectory);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "tools", "ios"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "tools", "libimobiledevice"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "libimobiledevice"));
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var directory = Path.GetFullPath(candidate);
                if (File.Exists(Path.Combine(directory, "idevice_id.exe")) &&
                    File.Exists(Path.Combine(directory, "ideviceinfo.exe")))
                    return directory;
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (PathTooLongException) { }
        }
        return null;
    }

    public static bool IsSafeDeviceIdentifier(string? identifier)
        => !string.IsNullOrWhiteSpace(identifier) &&
           identifier.Length <= 128 &&
           identifier.All(character => char.IsAsciiHexDigit(character) || character == '-');

    public async Task<IReadOnlyList<IPhoneDeviceScanResult>> ScanAsync(
        string? configuredToolsDirectory,
        CancellationToken cancellationToken = default)
    {
        var directory = ResolveToolsDirectory(configuredToolsDirectory);
        if (directory is null)
            throw new FileNotFoundException(
                "لم يتم العثور على idevice_id.exe وideviceinfo.exe. اختر مجلد أدوات libimobiledevice موثوقًا.");

        var listOutput = await RunAsync(
            Path.Combine(directory, "idevice_id.exe"),
            ["-l"],
            TimeSpan.FromSeconds(8),
            MaximumListOutput,
            cancellationToken);

        var identifiers = listOutput.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsSafeDeviceIdentifier)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumDevices + 1)
            .ToArray();

        if (identifiers.Length > MaximumDevices)
            throw new InvalidDataException($"تم العثور على أكثر من {MaximumDevices} أجهزة Apple؛ افصل الأجهزة الزائدة لتجنب الالتباس.");

        var results = new List<IPhoneDeviceScanResult>(identifiers.Length);
        foreach (var identifier in identifiers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var masked = IPhoneDeviceInfoParser.MaskIdentifier(identifier);
            try
            {
                var info = await RunAsync(
                    Path.Combine(directory, "ideviceinfo.exe"),
                    ["-u", identifier, "-x"],
                    TimeSpan.FromSeconds(12),
                    MaximumInfoOutput,
                    cancellationToken);
                var snapshot = IPhoneDeviceInfoParser.Parse(info.StandardOutput, identifier);
                results.Add(new IPhoneDeviceScanResult(masked, snapshot, null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new IPhoneDeviceScanResult(masked, null, SafeError(ex)));
            }
        }

        return results;
    }

    private static async Task<ProcessOutput> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        int maximumOutputCharacters,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(executable))
            throw new FileNotFoundException("An expected Apple device utility is missing.", executable);

        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("Windows did not start the Apple device utility.");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var stdoutTask = ReadBoundedAsync(process.StandardOutput, maximumOutputCharacters, timeoutSource.Token);
        var stderrTask = ReadBoundedAsync(process.StandardError, 16 * 1024, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            var output = await stdoutTask;
            var error = await stderrTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Apple utility exited with code {process.ExitCode}: {Limit(error, 1200)}");
            return new ProcessOutput(output, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"Apple device utility exceeded {timeout.TotalSeconds:0} seconds.");
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 16 * 1024));
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                return builder.ToString();
            if (builder.Length + read > maximumCharacters)
                throw new InvalidDataException("Apple utility output exceeded the safe size limit.");
            builder.Append(buffer, 0, read);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static string SafeError(Exception exception) => exception switch
    {
        TimeoutException => "انتهت مهلة قراءة معلومات الجهاز.",
        FileNotFoundException => "أداة قراءة Apple غير موجودة.",
        InvalidDataException => "رد الجهاز غير صالح أو تجاوز حد القراءة الآمن.",
        _ => "تعذر قراءة معلومات Apple. افتح قفل iPhone ووافق على رسالة الوثوق بهذا الكمبيوتر ثم أعد المحاولة."
    };

    private static string Limit(string value, int maximum)
        => string.IsNullOrWhiteSpace(value) ? string.Empty :
            value.Length <= maximum ? value.Trim() : value.Trim()[..maximum] + "…";

    private sealed record ProcessOutput(string StandardOutput, string StandardError);
}
