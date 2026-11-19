using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>Installs official scrcpy for Android screen mirroring, verifying the published archive hash.</summary>
public sealed class ScreenMirrorService
{
    private const string Version = "5.0.1";
    private const string ArchiveUrl = "https://github.com/Genymobile/scrcpy/releases/download/v5.0.1/scrcpy-win64-v5.0.1.zip";
    private const string ExpectedSha256 = "b12a2c4ee8be317422451fc7dcf8ee20a71b5ea7ef9ad73ddd825a25316227a5";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoftBox", "PhoneLab", "tools", $"scrcpy-{Version}");

    public async Task<string> EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        var executable = Path.Combine(InstallDirectory, "scrcpy.exe");
        if (File.Exists(executable)) return executable;
        Directory.CreateDirectory(InstallDirectory);
        var zipPath = Path.Combine(Path.GetTempPath(), $"scrcpy-{Version}-{Guid.NewGuid():N}.zip");
        try
        {
            using (var response = await Http.GetAsync(ArchiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(zipPath);
                await source.CopyToAsync(target, cancellationToken);
            }

            await using (var stream = File.OpenRead(zipPath))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(ExpectedSha256)))
                    throw new InvalidDataException("scrcpy archive SHA-256 verification failed.");
            }

            ZipFile.ExtractToDirectory(zipPath, InstallDirectory, overwriteFiles: true);
            if (!File.Exists(executable))
                throw new FileNotFoundException("Verified scrcpy archive did not contain scrcpy.exe.");
            return executable;
        }
        finally
        {
            try { File.Delete(zipPath); } catch { }
        }
    }

    public async Task StartAsync(string serial, string model, CancellationToken cancellationToken = default)
    {
        if (!AdbClient.IsSafeSerial(serial)) throw new ArgumentException("Invalid device serial.", nameof(serial));
        var executable = await EnsureInstalledAsync(cancellationToken);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = InstallDirectory
        };
        start.ArgumentList.Add("--serial"); start.ArgumentList.Add(serial);
        start.ArgumentList.Add("--window-title"); start.ArgumentList.Add($"SoftBox PhoneLab — {model}");
        start.ArgumentList.Add("--stay-awake");
        if (Process.Start(start) is null)
            throw new InvalidOperationException("Could not launch scrcpy.");
    }
}
