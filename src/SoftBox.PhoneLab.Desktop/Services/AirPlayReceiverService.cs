using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SoftBox.PhoneLab.Desktop.Interop;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Starts a separately installed AirPlay-Windows receiver and docks its native viewing window
/// into the existing SoftBox phone-screen host. SoftBox does not bundle or redistribute it.
/// All start/stop operations are serialized so two UI actions cannot launch or tear down
/// overlapping receiver processes.
/// </summary>
public sealed class AirPlayReceiverService : IAsyncDisposable
{
    private const uint WmClose = 0x0010;
    private static readonly TimeSpan WindowStartupTimeout = TimeSpan.FromSeconds(20);
    private readonly ConcurrentQueue<string> _recentOutput = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private Process? _process;
    private ScrcpyWindowHost? _host;
    private IntPtr _windowHandle;

    public event Action<string>? OutputReceived;

    public bool IsRunning
    {
        get
        {
            var process = Volatile.Read(ref _process);
            try { return process is { HasExited: false }; }
            catch (ObjectDisposedException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public int? ProcessId => IsRunning ? Volatile.Read(ref _process)?.Id : null;
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

    public async Task<string> StartAsync(
        string executablePath,
        ScrcpyWindowHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            return await StartCoreAsync(executablePath, host, cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<string> StartCoreAsync(
        string executablePath,
        ScrcpyWindowHost host,
        CancellationToken cancellationToken)
    {
        if (!host.IsLoaded || host.NativeHostHandle == IntPtr.Zero)
            throw new InvalidOperationException("مساحة شاشة SoftBox لم تجهز بعد. افتح Phone Link Studio ثم أعد المحاولة.");
        if (IsRunning)
            throw new InvalidOperationException("جلسة AirPlay تعمل بالفعل. أوقف الجلسة الحالية قبل تشغيل أخرى.");

        await StopCoreAsync();
        var fullPath = ValidateReceiverPath(executablePath);
        while (_recentOutput.TryDequeue(out _)) { }

        var start = new ProcessStartInfo
        {
            FileName = fullPath,
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // Upstream AirPlay-Windows documents --log as its supported logging switch.
        start.ArgumentList.Add("--log");

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) AddOutput(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) AddOutput("ERR: " + args.Data);
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Windows did not start the AirPlay receiver process.");
            Volatile.Write(ref _process, process);
            _host = host;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var end = DateTime.UtcNow + WindowStartupTimeout;
            while (DateTime.UtcNow < end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    try { process.WaitForExit(); } catch { }
                    throw new InvalidOperationException(
                        $"AirPlay receiver exited with code {process.ExitCode}. Recent output: {Limit(RecentOutput, 1200)}");
                }

                var window = ScrcpyWindowHost.FindTopLevelWindow(process.Id);
                if (window != IntPtr.Zero)
                {
                    if (!host.AttachWindow(window))
                        throw new InvalidOperationException("تعذر دمج نافذة مستقبل AirPlay داخل مساحة شاشة SoftBox.");
                    _windowHandle = window;
                    AddOutput("AirPlay receiver window is docked. Waiting for the owner to select it from iPhone Control Center.");
                    return fullPath;
                }
                await Task.Delay(120, cancellationToken);
            }

            throw new TimeoutException("AirPlay receiver did not create a visible window before timeout. Recent output: " +
                Limit(RecentOutput, 1200));
        }
        catch
        {
            await StopCoreAsync();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        var process = Interlocked.Exchange(ref _process, null);
        var host = _host;
        var window = _windowHandle;
        _host = null;
        _windowHandle = IntPtr.Zero;

        try
        {
            if (process is { HasExited: false })
            {
                if (window != IntPtr.Zero)
                    _ = PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);

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
            try { host?.DetachWindow(); } catch { }
            try { process?.Dispose(); } catch { }
        }

        if (process is not null)
            AddOutput("AirPlay receiver process stopped.");
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public async ValueTask DisposeAsync() => await StopAsync();
}
