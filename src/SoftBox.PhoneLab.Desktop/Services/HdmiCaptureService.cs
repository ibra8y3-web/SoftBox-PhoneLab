using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Captures a real HDMI-to-USB UVC capture-card feed through FFmpeg's DirectShow input.
/// SoftBox does not synthesize frames or bundle FFmpeg/capture hardware.
/// </summary>
public sealed class HdmiCaptureService : IAsyncDisposable
{
    private const int MaximumJpegBytes = 8 * 1024 * 1024;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly ConcurrentQueue<string> _recentOutput = new();
    private Process? _process;
    private CancellationTokenSource? _captureCancellation;
    private Task? _frameReaderTask;
    private Task? _errorReaderTask;
    private Task? _exitObserverTask;
    private bool _disposed;

    public event Action<byte[]>? FrameReceived;
    public event Action<string>? OutputReceived;

    public bool IsRunning
    {
        get
        {
            var process = Volatile.Read(ref _process);
            if (process is null) return false;
            try { return !process.HasExited; }
            catch { return false; }
        }
    }

    public string RecentOutput => string.Join(Environment.NewLine, _recentOutput.ToArray());

    public static string ValidateFfmpegPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DirectShow HDMI capture is supported by this SoftBox workflow on Windows only.");

        var fullPath = Path.GetFullPath(path);
        if (!Path.GetFileName(fullPath).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("اختر ffmpeg.exe من إصدار FFmpeg موثوق ومبني بدعم DirectShow.", nameof(path));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("ملف ffmpeg.exe غير موجود.", fullPath);
        return fullPath;
    }

    /// <summary>Extracts actual DirectShow video device names from FFmpeg diagnostic lines.</summary>
    public static IReadOnlyList<string> ParseVideoDeviceNames(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var match = Regex.Match(line, "\"(?<name>[^\"]+)\"\\s*\\(video\\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var name = match.Groups["name"].Value.Trim();
            if (name.Length > 0 && name.Length <= 512 && seen.Add(name))
                names.Add(name);
        }
        return names;
    }

    public async Task<IReadOnlyList<string>> EnumerateVideoDevicesAsync(
        string ffmpegPath,
        CancellationToken cancellationToken = default)
    {
        var executable = ValidateFfmpegPath(ffmpegPath);
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" })
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("تعذر تشغيل FFmpeg لفحص أجهزة التقاط الفيديو.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("انتهت مهلة فحص أجهزة DirectShow بعد 12 ثانية.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var devices = ParseVideoDeviceNames((stdout + Environment.NewLine + stderr)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries));
        if (devices.Count == 0)
        {
            var detail = (stderr + Environment.NewLine + stdout).Trim();
            if (detail.Length > 1800) detail = detail[^1800..];
            throw new InvalidOperationException(
                "لم يعرض FFmpeg أي جهاز فيديو DirectShow. تأكد من توصيل كارت الالتقاط وأنه ظاهر في Windows ككاميرا UVC، وأن إصدار FFmpeg يدعم dshow.\n" + detail);
        }
        return devices;
    }

    public async Task StartAsync(
        string ffmpegPath,
        string captureDeviceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(captureDeviceName))
            throw new ArgumentException("اختر جهاز التقاط HDMI من القائمة أولًا.", nameof(captureDeviceName));
        if (captureDeviceName.Length > 512)
            throw new ArgumentOutOfRangeException(nameof(captureDeviceName));

        var executable = ValidateFfmpegPath(ffmpegPath);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRunning)
                throw new InvalidOperationException("جلسة HDMI Capture تعمل بالفعل. أوقفها قبل بدء جلسة أخرى.");
            await StopCoreAsync();

            var start = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[]
            {
                "-hide_banner", "-loglevel", "warning",
                "-f", "dshow", "-i", "video=" + captureDeviceName,
                "-an", "-vf", "fps=15,scale=1280:-2",
                "-q:v", "5", "-f", "image2pipe", "-vcodec", "mjpeg", "pipe:1"
            })
                start.ArgumentList.Add(argument);

            var process = new Process { StartInfo = start };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("تعذر تشغيل FFmpeg لبدء التقاط HDMI.");
            }

            _process = process;
            _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _captureCancellation.Token;
            _frameReaderTask = ReadFramesAsync(process.StandardOutput.BaseStream, token);
            _errorReaderTask = ReadErrorsAsync(process.StandardError, token);
            _exitObserverTask = ObserveExitAsync(process, token);

            await Task.Delay(450, cancellationToken);
            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                var log = RecentOutput;
                await StopCoreAsync();
                throw new InvalidOperationException(
                    $"توقفت عملية FFmpeg فور بدء الالتقاط (ExitCode={exitCode}). راجع السجل: {log}");
            }

            AddOutput("FFmpeg DirectShow capture started for device: " + captureDeviceName);
            AddOutput("Waiting for the first real HDMI capture frame; no placeholder image is generated.");
        }
        catch
        {
            await StopCoreAsync();
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _lifecycleGate.Release(); }
    }

    private async Task StopCoreAsync()
    {
        var process = Interlocked.Exchange(ref _process, null);
        var cancellation = Interlocked.Exchange(ref _captureCancellation, null);
        try { cancellation?.Cancel(); } catch { }

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }

            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { }
        }

        var tasks = new[] { _frameReaderTask, _errorReaderTask, _exitObserverTask }
            .Where(task => task is not null).Cast<Task>().ToArray();
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch { }

        try { process?.Dispose(); } catch { }
        try { cancellation?.Dispose(); } catch { }
        if (process is not null)
            AddOutput("HDMI capture process stopped and its output streams were released.");
        _frameReaderTask = null;
        _errorReaderTask = null;
        _exitObserverTask = null;
    }

    private async Task ReadFramesAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var previous = -1;
        MemoryStream? frame = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;

                for (var index = 0; index < read; index++)
                {
                    var current = buffer[index];
                    if (frame is null)
                    {
                        if (previous == 0xFF && current == 0xD8)
                        {
                            frame = new MemoryStream(128 * 1024);
                            frame.WriteByte(0xFF);
                            frame.WriteByte(0xD8);
                        }
                    }
                    else
                    {
                        frame.WriteByte(current);
                        if (frame.Length > MaximumJpegBytes)
                        {
                            frame.Dispose();
                            frame = null;
                            AddOutput("Dropped an oversized MJPEG frame (over 8 MiB).");
                        }
                        else if (previous == 0xFF && current == 0xD9)
                        {
                            var jpeg = frame.ToArray();
                            frame.Dispose();
                            frame = null;
                            try { FrameReceived?.Invoke(jpeg); }
                            catch (Exception ex) { AddOutput("Frame consumer rejected a decoded JPEG: " + ex.Message); }
                        }
                    }
                    previous = current;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException ex) { AddOutput("HDMI frame stream closed: " + ex.Message); }
        catch (ObjectDisposedException) { }
        finally { frame?.Dispose(); }
    }

    private async Task ReadErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null) break;
                if (!string.IsNullOrWhiteSpace(line))
                    AddOutput(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException ex) { AddOutput("FFmpeg diagnostic stream closed: " + ex.Message); }
        catch (ObjectDisposedException) { }
    }

    private async Task ObserveExitAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync();
            if (!cancellationToken.IsCancellationRequested &&
                ReferenceEquals(Volatile.Read(ref _process), process))
            {
                Interlocked.CompareExchange(ref _process, null, process);
                AddOutput("FFmpeg capture process exited unexpectedly with code " + process.ExitCode + ".");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            AddOutput("Unable to observe FFmpeg process exit: " + ex.Message);
        }
    }

    private void AddOutput(string line)
    {
        _recentOutput.Enqueue(DateTime.Now.ToString("HH:mm:ss") + " " + line);
        while (_recentOutput.Count > 120)
            _recentOutput.TryDequeue(out _);
        try { OutputReceived?.Invoke(line); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync();
        _disposed = true;
        _lifecycleGate.Dispose();
    }
}
