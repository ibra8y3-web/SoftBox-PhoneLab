using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SoftBox.PhoneLab.Desktop.Interop;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed class ScreenMirrorService(AdbClient adb) : IDisposable
{
    private const string Version = "5.0.1";
    private const string ArchiveUrl = "https://github.com/Genymobile/scrcpy/releases/download/v5.0.1/scrcpy-win64-v5.0.1.zip";
    private const string ExpectedSha256 = "b12a2c4ee8be317422451fc7dcf8ee20a71b5ea7ef9ad73ddd825a25316227a5";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "Tools", "scrcpy");
    private string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoftBox", "PhoneLab", "tools", $"scrcpy-{Version}");
    private Process? _process;
    private ScrcpyWindowHost? _host;
    private IntPtr _windowHandle;
    public string? ActiveSerial { get; private set; }
    public string? ActiveRecordingPath { get; private set; }
    public bool IsRecording => ActiveRecordingPath is not null;
    public bool IsRunning
    {
        get
        {
            try { return _process is { HasExited: false }; }
            catch (ObjectDisposedException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public async Task<string> EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        var bundled = Path.Combine(BundledDirectory, "scrcpy.exe");
        if (File.Exists(bundled)) return bundled;
        var executable = Path.Combine(InstallDirectory, "scrcpy.exe");
        if (File.Exists(executable)) return executable;
        Directory.CreateDirectory(InstallDirectory);
        var zip = Path.Combine(Path.GetTempPath(), "scrcpy-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var response = await Http.GetAsync(ArchiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(zip);
                await input.CopyToAsync(output, cancellationToken);
            }
            await using (var input = File.OpenRead(zip))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant();
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(ExpectedSha256)))
                    throw new InvalidDataException("scrcpy archive SHA-256 verification failed.");
            }
            ZipFile.ExtractToDirectory(zip, InstallDirectory, true);
            if (!File.Exists(executable)) throw new FileNotFoundException("Verified scrcpy archive did not contain scrcpy.exe.");
            return executable;
        }
        finally { try { File.Delete(zip); } catch { } }
    }

    public Task StartEmbeddedAsync(
        string serial,
        string model,
        ScrcpyWindowHost host,
        CancellationToken cancellationToken = default)
        => StartEmbeddedCoreAsync(serial, model, host, recordingPath: null, cancellationToken: cancellationToken);

    public Task StartEmbeddedRecordingAsync(
        string serial,
        string model,
        ScrcpyWindowHost host,
        string recordingPath,
        CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("Invalid ADB serial.", nameof(serial));
        if (string.IsNullOrWhiteSpace(recordingPath))
            throw new ArgumentException("حدد مسار حفظ التسجيل.", nameof(recordingPath));

        var fullPath = Path.GetFullPath(recordingPath);
        var extension = Path.GetExtension(fullPath);
        if (!extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("صيغة التسجيل المدعومة هي MP4 أو MKV.", nameof(recordingPath));
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            throw new IOException("ملف التسجيل موجود بالفعل؛ اختر اسمًا آخر حتى لا يتم استبدال تسجيل قديم.");

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return StartEmbeddedCoreAsync(serial, model, host, fullPath, cancellationToken);
    }

    private async Task StartEmbeddedCoreAsync(
        string serial,
        string model,
        ScrcpyWindowHost host,
        string? recordingPath,
        CancellationToken cancellationToken)
    {
        if (!AdbClient.IsSafeSerial(serial)) throw new ArgumentException("Invalid ADB serial.", nameof(serial));
        if (!host.IsLoaded || host.NativeHostHandle == IntPtr.Zero) throw new InvalidOperationException("Phone screen host is not ready.");
        if (recordingPath is null && ActiveRecordingPath is null &&
            ActiveSerial == serial && _process is { HasExited: false } && host.HasEmbeddedWindow) return;
        Stop();

        var exe = await EnsureInstalledAsync(cancellationToken);
        // Installation may take time; perform a last ADB authorization check before launching scrcpy.
        var adbState = await adb.RunForDeviceAsync(serial, ["get-state"], cancellationToken, TimeSpan.FromSeconds(6));
        if (!MultiScreenSessionsPolicy.IsAuthorizedAdbState(adbState))
            throw new UnauthorizedAccessException("الجهاز المحدد لم يعد مصرحًا به عبر ADB؛ تم منع تشغيل scrcpy.");
        var exeDir = Path.GetDirectoryName(exe)!;
        var adbDir = Path.GetDirectoryName(adb.Executable) ?? "";
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = exeDir };
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        start.Environment["PATH"] = string.Join(Path.PathSeparator.ToString(),
            new[] { adbDir, exeDir, path }.Where(x => !string.IsNullOrWhiteSpace(x)));
        start.ArgumentList.Add("--serial"); start.ArgumentList.Add(serial);
        start.ArgumentList.Add("--window-title"); start.ArgumentList.Add("SoftBox PhoneLab - " + model);
        start.ArgumentList.Add("--window-borderless");
        start.ArgumentList.Add("--stay-awake");
        if (recordingPath is not null)
        {
            start.ArgumentList.Add("--record");
            start.ArgumentList.Add(recordingPath);
        }

        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch scrcpy.");
        _host = host;
        ActiveSerial = serial;
        ActiveRecordingPath = recordingPath;
        var process = _process;
        var end = DateTime.UtcNow.AddSeconds(18);
        try
        {
            while (DateTime.UtcNow < end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new InvalidOperationException("scrcpy exited with code " + process.ExitCode + ". Check ADB authorization and driver.");
                var hwnd = ScrcpyWindowHost.FindTopLevelWindow(process.Id);
                if (hwnd != IntPtr.Zero)
                {
                    if (!host.AttachWindow(hwnd))
                        throw new InvalidOperationException("Could not embed scrcpy in PhoneLab.");
                    _windowHandle = hwnd;
                    return;
                }
                await Task.Delay(120, cancellationToken);
            }
            throw new TimeoutException("No scrcpy window appeared. Check USB debugging authorization.");
        }
        catch { Stop(); throw; }
    }

    public async Task<FileInfo> StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        var recordingPath = ActiveRecordingPath
            ?? throw new InvalidOperationException("لا يوجد تسجيل شاشة نشط لإيقافه.");
        var process = _process;
        if (process is null)
            throw new InvalidOperationException("عملية scrcpy المسؤولة عن التسجيل غير متاحة.");

        try
        {
            if (!process.HasExited && _windowHandle != IntPtr.Zero)
                _ = PostMessage(_windowHandle, WmClose, IntPtr.Zero, IntPtr.Zero);

            if (!process.HasExited)
            {
                var exited = await Task.Run(() => process.WaitForExit(12_000), cancellationToken);
                if (!exited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken);
                    throw new TimeoutException("لم يغلق scrcpy ملف التسجيل في الوقت المحدد؛ تم إنهاء العملية، وقد يكون الملف غير مكتمل.");
                }
            }

            var host = _host;
            try { host?.DetachWindow(); } catch { }
            _host = null;
            _windowHandle = IntPtr.Zero;
            _process = null;
            ActiveSerial = null;
            ActiveRecordingPath = null;

            process.Dispose();
            if (!File.Exists(recordingPath))
                throw new IOException("انتهى scrcpy لكن ملف التسجيل لم يتم إنشاؤه.");
            var info = new FileInfo(recordingPath);
            if (info.Length <= 0)
                throw new InvalidDataException("ملف التسجيل الناتج فارغ.");
            return info;
        }
        catch
        {
            if (process.HasExited)
            {
                try { _host?.DetachWindow(); } catch { }
                _host = null;
                _windowHandle = IntPtr.Zero;
                _process = null;
                ActiveSerial = null;
                ActiveRecordingPath = null;
                process.Dispose();
            }
            throw;
        }
    }

    public void Stop()
    {
        var process = _process;
        try
        {
            if (ActiveRecordingPath is not null && process is { HasExited: false } && _windowHandle != IntPtr.Zero)
            {
                _ = PostMessage(_windowHandle, WmClose, IntPtr.Zero, IntPtr.Zero);
                if (!process.WaitForExit(8_000))
                    process.Kill(entireProcessTree: true);
            }
            else if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch { }
        try { process?.Dispose(); } catch { }
        try { _host?.DetachWindow(); } catch { }
        _host = null;
        _windowHandle = IntPtr.Zero;
        _process = null;
        ActiveSerial = null;
        ActiveRecordingPath = null;
    }

    private const uint WmClose = 0x0010;
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public void Dispose() => Stop();
}
