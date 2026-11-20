using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
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
    public string? ActiveSerial { get; private set; }

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

    public async Task StartEmbeddedAsync(string serial, string model, ScrcpyWindowHost host, CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial)) throw new ArgumentException("Invalid ADB serial.", nameof(serial));
        if (!host.IsLoaded || host.NativeHostHandle == IntPtr.Zero) throw new InvalidOperationException("Phone screen host is not ready.");
        if (ActiveSerial == serial && _process is { HasExited: false } && host.HasEmbeddedWindow) return;
        Stop();
        var exe = await EnsureInstalledAsync(cancellationToken);
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
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch scrcpy.");
        _host = host; ActiveSerial = serial;
        var process = _process;
        var end = DateTime.UtcNow.AddSeconds(18);
        try
        {
            while (DateTime.UtcNow < end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException("scrcpy exited with code " + process.ExitCode + ". Check ADB authorization and driver.");
                var hwnd = ScrcpyWindowHost.FindTopLevelWindow(process.Id);
                if (hwnd != IntPtr.Zero)
                {
                    if (!host.AttachWindow(hwnd)) throw new InvalidOperationException("Could not embed scrcpy in PhoneLab.");
                    return;
                }
                await Task.Delay(120, cancellationToken);
            }
            throw new TimeoutException("No scrcpy window appeared. Check USB debugging authorization.");
        }
        catch { Stop(); throw; }
    }

    public void Stop()
    {
        try { _host?.DetachWindow(); } catch { }
        _host = null;
        var process = _process; _process = null; ActiveSerial = null;
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(true); process.Dispose(); } catch { }
    }
    public void Dispose() => Stop();
}
