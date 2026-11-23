using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Starts a separately installed AirPlay-Windows receiver as an independent GPL-3 program.
/// SoftBox does not bundle, modify or redistribute that third-party executable.
/// </summary>
public sealed class AirPlayReceiverService : IAsyncDisposable
{
    private Process? _process;
    private readonly ConcurrentQueue<string> _recentOutput = new();
    public event Action<string>? OutputReceived;

    public bool IsRunning
    {
        get
        {
            try { return _process is { HasExited: false }; }
            catch (InvalidOperationException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }
    }

    public int? ProcessId => IsRunning ? _process?.Id : null;
    public string RecentOutput => string.Join(Environment.NewLine, _recentOutput.ToArray());

    public static string ValidateReceiverPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The selected AirPlay-Windows receiver runs on Windows only.");

        var fullPath = Path.GetFullPath(path);
        if (!Path.GetFileName(fullPath).Equals("airplay-windows.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("اختر airplay-windows.exe من إصدار AirPlay-Windows الرسمي، وليس ملفًا تنفيذيًا آخر.", nameof(path));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("ملف AirPlay receiver غير موجود.", fullPath);
        return fullPath;
    }

    public async Task<string> StartAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("جلسة AirPlay تعمل بالفعل. أوقف الجلسة الحالية قبل تشغيل أخرى.");

        await StopAsync();
        var fullPath = ValidateReceiverPath(executablePath);
        var start = new ProcessStartInfo
        {
            FileName = fullPath,
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--log");

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) AddOutput(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) AddOutput("ERR: " + args.Data); };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Windows did not start the AirPlay receiver process.");
            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Validate process startup only. The owner must still select the receiver from iPhone Control Center.
            await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                var output = RecentOutput;
                _process = null;
                process.Dispose();
                throw new InvalidOperationException(
                    $"AirPlay receiver exited immediately (code {exitCode}). {Limit(output, 1200)}");
            }

            AddOutput("Receiver process started. Waiting for an AirPlay client; no phone has been confirmed yet.");
            return fullPath;
        }
        catch
        {
            if (_process is not null)
                await StopAsync();
            else
                process.Dispose();
            throw;
        }
    }

    public async Task StopAsync()
    {
        var process = _process;
        if (process is null) return;
        _process = null;

        try
        {
            if (!process.HasExited)
            {
                _ = process.CloseMainWindow();
                var exited = await Task.Run(() => process.WaitForExit(3500));
                if (!exited && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally
        {
            try { process.Dispose(); } catch { }
        }
        AddOutput("Receiver process stopped.");
    }

    private void AddOutput(string line)
    {
        var safe = Limit(line, 1200);
        _recentOutput.Enqueue(safe);
        while (_recentOutput.Count > 120 && _recentOutput.TryDequeue(out _)) { }
        try { OutputReceived?.Invoke(safe); } catch { }
    }

    private static string Limit(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var safe = value.Trim();
        return safe.Length <= max ? safe : safe[..max] + "…";
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
