using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Installs the MIT-licensed TRACE offline board viewer on demand.
/// Board files remain local; only the signed-by-hash application binary is downloaded.
/// </summary>
public sealed class TraceBoardViewerService
{
    private const string Version = "1.3.3";
    private const string DownloadUrl = "https://github.com/trace-boardviewer/trace-boardviewer/releases/download/v1.3.3/TRACE-Boardviewer-1.3.3.exe";
    private const string ExpectedSha256 = "88a3bace7a3e71cb1e8136561acd4fbb86d8dd8568da6b179bdc46024c8aacd8";
    private const long MaximumInstallerBytes = 150L * 1024 * 1024;
    private const long MaximumBoardFileBytes = 512L * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(8) };
    private string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoftBox", "PhoneLab", "tools", $"TRACE-Boardviewer-{Version}");
    private string ExecutablePath => Path.Combine(InstallDirectory, $"TRACE-Boardviewer-{Version}.exe");

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".brd", ".brd2", ".bdv", ".bvr", ".bv", ".bv2", ".gr", ".cad", ".gcd",
        ".pcb", ".kicad_pcb", ".pcbdoc", ".asc", ".fz", ".tvw", ".cae", ".epro", ".epro2",
        ".f2b", ".farc", ".faz", ".ict", ".tgz", ".tar", ".gz", ".zip"
    };

    public async Task OpenAsync(string boardFile, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(boardFile);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("BoardView file not found.", fullPath);
        if (!SupportedExtensions.Contains(info.Extension))
            throw new InvalidDataException("امتداد الملف ليس من صيغ BoardView/PCB المعروفة.");
        if (info.Length <= 0 || info.Length > MaximumBoardFileBytes)
            throw new InvalidDataException("حجم الملف فارغ أو يتجاوز حد 512 MB.");

        var executable = await EnsureInstalledAsync(cancellationToken);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = InstallDirectory
        };
        start.ArgumentList.Add(fullPath);
        if (Process.Start(start) is null)
            throw new InvalidOperationException("لم يتمكن PhoneLab من تشغيل عارض BoardView.");
    }

    public async Task<string> EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "Tools", $"TRACE-Boardviewer-{Version}.exe");
        if (File.Exists(bundledPath))
        {
            if (await VerifyAsync(bundledPath, cancellationToken)) return bundledPath;
            throw new InvalidDataException("SHA-256 verification failed for bundled TRACE Boardviewer.");
        }
        Directory.CreateDirectory(InstallDirectory);
        if (File.Exists(ExecutablePath))
        {
            if (await VerifyAsync(ExecutablePath, cancellationToken)) return ExecutablePath;
            File.Delete(ExecutablePath);
        }

        var tempPath = ExecutablePath + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var response = await Http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumInstallerBytes)
                throw new InvalidDataException("عارض BoardView تجاوز الحجم المتوقع.");

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1024 * 128];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaximumInstallerBytes)
                        throw new InvalidDataException("عارض BoardView تجاوز حد 150 MB.");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            if (!await VerifyAsync(tempPath, cancellationToken))
                throw new InvalidDataException("فشل التحقق من SHA-256 لعارض BoardView.");

            File.Move(tempPath, ExecutablePath, overwrite: true);
            return ExecutablePath;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static async Task<bool> VerifyAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var expected = Convert.FromHexString(ExpectedSha256);
        return CryptographicOperations.FixedTimeEquals(hash, expected);
    }
}
