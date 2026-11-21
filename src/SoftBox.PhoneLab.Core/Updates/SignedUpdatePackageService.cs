using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Updates;

public sealed record SignedUpdateFile(string Path, long SizeBytes, string Sha256);
public sealed record SignedUpdateManifest(
    int SchemaVersion,
    string Version,
    string MinimumSupportedVersion,
    string Publisher,
    string MainExecutable,
    IReadOnlyList<SignedUpdateFile> Files,
    string ReleaseNotes);
public sealed record SignedUpdateStageInfo(
    string Version,
    string StageDirectory,
    string MainExecutable,
    string Publisher,
    IReadOnlyList<SignedUpdateFile> Files,
    DateTimeOffset StagedAtUtc);
public sealed record SignedUpdateRollbackState(
    string InstallDirectory,
    string ExecutablePath,
    string BackupDirectory,
    string PreviousVersion,
    string InstalledVersion,
    DateTimeOffset InstalledAtUtc);

/// <summary>
/// Downloads only official SoftBox release assets, validates a detached RSA publisher signature
/// and per-file SHA-256 hashes, then stages the verified package. A separate PowerShell helper
/// performs the file swap after the current app exits and keeps a complete rollback snapshot.
/// </summary>
public sealed class SignedUpdatePackageService
{
    public const string ManifestEntryName = "softbox-update.json";
    public const string SignatureEntryName = PluginSignaturePayload.SignatureEntryName;
    private const string TrustedPublisherName = "SoftBox Release Publisher";
    private const long MaximumArchiveBytes = 1024L * 1024 * 1024;
    private const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumFiles = 2000;
    private static readonly Regex VersionPattern = new(@"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, MaxDepth = 20 };
    private readonly HttpClient _http;
    private readonly PluginPublisherTrustStore _trustStore;
    private readonly string _updatesRoot;

    public SignedUpdatePackageService(HttpClient? httpClient = null, PluginPublisherTrustStore? trustStore = null, string? rootDirectory = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _trustStore = trustStore ?? new PluginPublisherTrustStore();
        _updatesRoot = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "Updates"));
        Directory.CreateDirectory(_updatesRoot);
    }

    public string UpdatesRoot => _updatesRoot;
    public string RollbackStatePath => Path.Combine(_updatesRoot, "last-update.json");

    public async Task<SignedUpdateStageInfo> DownloadAndStageAsync(
        ReleaseAssetInfo asset,
        string expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!ReleaseUpdateService.IsOfficialAssetUrl(asset.DownloadUrl))
            throw new UnauthorizedAccessException("رابط أصل التحديث ليس من مستودع SoftBox الرسمي.");
        if (asset.SizeBytes is <= 0 or > MaximumArchiveBytes)
            throw new InvalidDataException("حجم أصل التحديث فارغ أو يتجاوز 1 GiB.");
        if (!VersionPattern.IsMatch(NormalizeVersion(expectedVersion)))
            throw new ArgumentException("رقم إصدار التحديث غير صالح.", nameof(expectedVersion));

        var tempArchive = Path.Combine(_updatesRoot, "download-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null || finalUri.Scheme != Uri.UriSchemeHttps ||
                !(finalUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                  finalUri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                throw new UnauthorizedAccessException("تم تحويل تنزيل أصل التحديث إلى مضيف غير مسموح.");
            if (response.Content.Headers.ContentLength is long declared && declared != asset.SizeBytes)
                throw new InvalidDataException("حجم أصل التحديث لا يطابق بيانات GitHub Releases.");

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(tempArchive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0) break;
                    total += read;
                    if (total > MaximumArchiveBytes)
                        throw new InvalidDataException("تجاوز تنزيل التحديث حد الحجم 1 GiB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
                if (total != asset.SizeBytes)
                    throw new InvalidDataException("الحجم الفعلي للتنزيل لا يطابق حجم الأصل المنشور.");
            }

            return await VerifyAndStageAsync(tempArchive, NormalizeVersion(expectedVersion), cancellationToken);
        }
        finally
        {
            try { File.Delete(tempArchive); } catch { }
        }
    }

    public async Task<SignedUpdateStageInfo> VerifyAndStageAsync(
        string archivePath,
        string expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("ملف التحديث غير موجود.", archivePath);
        var info = new FileInfo(archivePath);
        if (info.Length is <= 0 or > MaximumArchiveBytes)
            throw new InvalidDataException("حجم حزمة التحديث غير مسموح.");

        SignedUpdateManifest manifest;
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            ValidateArchiveEntries(archive.Entries);
            var manifestEntry = SingleEntry(archive, ManifestEntryName, 512 * 1024);
            var signatureEntry = SingleEntry(archive, SignatureEntryName, 16 * 1024);

            await using (var stream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<SignedUpdateManifest>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("Manifest تحديث SoftBox فارغ.");
            }

            ValidateManifest(manifest, expectedVersion);

            PluginPackageSignature signature;
            await using (var stream = signatureEntry.Open())
                signature = await JsonSerializer.DeserializeAsync<PluginPackageSignature>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("توقيع تحديث SoftBox فارغ.");

            if (!string.Equals(signature.Algorithm, PluginSignaturePayload.AlgorithmName, StringComparison.Ordinal) ||
                signature.KeyId is null || signature.KeyId.Length != 64 || !signature.KeyId.All(Uri.IsHexDigit))
                throw new InvalidDataException("بيانات توقيع تحديث SoftBox غير صالحة.");

            var trusted = await _trustStore.FindAsync(signature.KeyId, cancellationToken);
            if (trusted is null || !trusted.PublisherName.Equals(TrustedPublisherName, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("مفتاح ناشر تحديث SoftBox غير موجود في قائمة الثقة.");

            var payload = await PluginSignaturePayload.ComputeAsync(archive.Entries, cancellationToken);
            byte[] signatureBytes;
            try { signatureBytes = Convert.FromBase64String(signature.SignatureBase64); }
            catch (FormatException ex) { throw new InvalidDataException("توقيع التحديث ليس Base64 صالحًا.", ex); }
            if (!PluginSignaturePayload.Verify(payload, signatureBytes, trusted.PublicKeyPem))
                throw new CryptographicException("توقيع ناشر تحديث SoftBox غير صحيح.");

            var contentEntries = archive.Entries
                .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
                .Where(entry => entry.FullName != ManifestEntryName && entry.FullName != SignatureEntryName)
                .ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
            var manifestFiles = manifest.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
            if (contentEntries.Count != manifestFiles.Count ||
                contentEntries.Keys.Any(path => !manifestFiles.ContainsKey(path)))
                throw new InvalidDataException("قائمة الملفات داخل Manifest لا تطابق محتويات أرشيف التحديث.");

            long expanded = 0;
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = contentEntries[file.Path];
                if (entry.Length != file.SizeBytes)
                    throw new InvalidDataException("حجم ملف التحديث لا يطابق Manifest: " + file.Path);
                expanded += entry.Length;
                if (expanded > MaximumExpandedBytes)
                    throw new InvalidDataException("الحجم الإجمالي بعد فك الضغط يتجاوز 2 GiB.");
                var hash = await CalculateSha256Async(entry, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(file.Sha256)))
                    throw new CryptographicException("بصمة ملف التحديث غير مطابقة: " + file.Path);
            }

            var main = manifest.Files.FirstOrDefault(file =>
                file.Path.Equals(manifest.MainExecutable, StringComparison.OrdinalIgnoreCase));
            if (main is null || !manifest.MainExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ملف التشغيل الرئيسي غير موجود في قائمة الملفات الموقعة.");

            var stageName = NormalizeVersion(manifest.Version) + "-" + Guid.NewGuid().ToString("N");
            var stagePath = Path.Combine(_updatesRoot, "Staged", stageName);
            var tempStage = stagePath + ".tmp";
            Directory.CreateDirectory(tempStage);
            try
            {
                foreach (var file in manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = ResolveSafeChildPath(tempStage, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var source = contentEntries[file.Path].Open();
                    await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
                    await source.CopyToAsync(output, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }

                await File.WriteAllTextAsync(Path.Combine(tempStage, ManifestEntryName),
                    JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
                Directory.CreateDirectory(Path.GetDirectoryName(stagePath)!);
                Directory.Move(tempStage, stagePath);
            }
            catch
            {
                try { Directory.Delete(tempStage, recursive: true); } catch { }
                throw;
            }

            return new SignedUpdateStageInfo(
                manifest.Version, stagePath, manifest.MainExecutable, trusted.PublisherName,
                manifest.Files.ToArray(), DateTimeOffset.UtcNow);
        }
    }

    public string BuildInstallScript(
        SignedUpdateStageInfo stage,
        string installDirectory,
        string executablePath,
        int parentProcessId,
        string previousVersion)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var installRoot = Path.GetFullPath(installDirectory);
        var executable = Path.GetFullPath(executablePath);
        var stageRoot = Path.GetFullPath(stage.StageDirectory);
        if (!Directory.Exists(installRoot) || !File.Exists(executable) ||
            !IsChildPath(installRoot, executable) ||
            !IsChildPath(Path.Combine(_updatesRoot, "Staged"), stageRoot) ||
            parentProcessId <= 0)
            throw new InvalidOperationException("مسار التثبيت أو ملف التشغيل أو مجلد التحديث غير صالح.");

        var rollbackRoot = Path.Combine(_updatesRoot, "Rollback",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        var statePath = RollbackStatePath;
        var configuration = JsonSerializer.Serialize(new
        {
            installRoot,
            executable,
            stageRoot,
            rollbackRoot,
            statePath,
            previousVersion = NormalizeVersion(previousVersion),
            installedVersion = NormalizeVersion(stage.Version),
            parentProcessId,
            files = stage.Files
        });
        return BuildEncodedPowerShell($@"
$ErrorActionPreference = 'Stop'
$config = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(configuration))}')) | ConvertFrom-Json
Wait-Process -Id ([int]$config.parentProcessId) -Timeout 90 -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $config.rollbackRoot | Out-Null
try {{
    Copy-Item -Path (Join-Path $config.installRoot '*') -Destination $config.rollbackRoot -Recurse -Force
    $stagePrefix = [IO.Path]::GetFullPath($config.stageRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $installPrefix = [IO.Path]::GetFullPath($config.installRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($file in $config.files) {{
        $source = [IO.Path]::GetFullPath((Join-Path $config.stageRoot $file.Path))
        if (!$source.StartsWith($stagePrefix, [StringComparison]::OrdinalIgnoreCase)) {{ throw 'Unsafe staged update path' }}
        $actual = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $file.Sha256.ToLowerInvariant()) {{ throw ('Staged file hash changed: ' + $file.Path) }}
        if ((Get-Item -LiteralPath $source).Length -ne [long]$file.SizeBytes) {{ throw ('Staged file size changed: ' + $file.Path) }}
        $target = [IO.Path]::GetFullPath((Join-Path $config.installRoot $file.Path))
        if (!$target.StartsWith($installPrefix, [StringComparison]::OrdinalIgnoreCase)) {{ throw 'Unsafe install target' }}
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Force
    }}
    $state = @{{ InstallDirectory=$config.installRoot; ExecutablePath=$config.executable; BackupDirectory=$config.rollbackRoot; PreviousVersion=$config.previousVersion; InstalledVersion=$config.installedVersion; InstalledAtUtc=[DateTime]::UtcNow.ToString('o') }}
    $state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $config.statePath -Encoding UTF8
    Start-Process -FilePath $config.executable -WorkingDirectory $config.installRoot
}}
catch {{
    if (Test-Path -LiteralPath $config.rollbackRoot) {{ Copy-Item -Path (Join-Path $config.rollbackRoot '*') -Destination $config.installRoot -Recurse -Force }}
    throw
}}
");
    }

    public async Task<SignedUpdateRollbackState?> ReadRollbackStateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(RollbackStatePath))
            return null;
        var json = await File.ReadAllTextAsync(RollbackStatePath, cancellationToken);
        var state = JsonSerializer.Deserialize<SignedUpdateRollbackState>(json, JsonOptions);
        if (state is null)
            throw new InvalidDataException("بيانات الرجوع إلى الإصدار السابق غير صالحة.");
        var expectedRoot = Path.GetFullPath(Path.Combine(_updatesRoot, "Rollback"));
        var backup = Path.GetFullPath(state.BackupDirectory);
        var install = Path.GetFullPath(state.InstallDirectory);
        var executable = Path.GetFullPath(state.ExecutablePath);
        if (!IsChildPath(expectedRoot, backup) || !Directory.Exists(backup) ||
            !Directory.Exists(install) || !IsChildPath(install, executable) || !File.Exists(executable))
            throw new InvalidDataException("مسارات النسخة السابقة غير صالحة أو لم تعد موجودة.");
        return state;
    }

    public string BuildRollbackScript(SignedUpdateRollbackState state, int parentProcessId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (parentProcessId <= 0)
            throw new ArgumentOutOfRangeException(nameof(parentProcessId));
        var configuration = JsonSerializer.Serialize(new
        {
            installRoot = Path.GetFullPath(state.InstallDirectory),
            executable = Path.GetFullPath(state.ExecutablePath),
            backupRoot = Path.GetFullPath(state.BackupDirectory),
            parentProcessId,
            statePath = RollbackStatePath
        });
        return BuildEncodedPowerShell($@"
$ErrorActionPreference = 'Stop'
$config = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(configuration))}')) | ConvertFrom-Json
Wait-Process -Id ([int]$config.parentProcessId) -Timeout 90 -ErrorAction SilentlyContinue
$backupPrefix = [IO.Path]::GetFullPath($config.backupRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$installPrefix = [IO.Path]::GetFullPath($config.installRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!(Test-Path -LiteralPath $config.backupRoot)) {{ throw 'Rollback snapshot is missing' }}
Get-ChildItem -LiteralPath $config.backupRoot -Force | ForEach-Object {{
    $destination = [IO.Path]::GetFullPath((Join-Path $config.installRoot $_.Name))
    if (!$destination.StartsWith($installPrefix, [StringComparison]::OrdinalIgnoreCase)) {{ throw 'Unsafe rollback target' }}
    Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
}}
Remove-Item -LiteralPath $config.statePath -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $config.executable -WorkingDirectory $config.installRoot
");
    }

    private static async Task<string> CalculateSha256Async(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = entry.Open();
        var buffer = new byte[128 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static ZipArchiveEntry SingleEntry(ZipArchive archive, string name, long maxBytes)
    {
        var matches = archive.Entries.Where(entry => entry.FullName == name).ToArray();
        if (matches.Length != 1 || matches[0].Length is <= 0 || matches[0].Length > maxBytes)
            throw new InvalidDataException("ملف الحزمة مطلوب مرة واحدة وبحجم صحيح: " + name);
        return matches[0];
    }

    private static void ValidateArchiveEntries(IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        if (entries.Count is < 3 or > MaximumFiles + 2)
            throw new InvalidDataException("عدد ملفات أرشيف التحديث غير مدعوم.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var raw = entry.FullName;
            var normalized = raw.EndsWith("/", StringComparison.Ordinal) ? raw[..^1] : raw;
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 512 ||
                normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains('\\') ||
                normalized.Contains(':') || normalized.Any(char.IsControl) ||
                normalized.Split('/').Any(segment => segment is "" or "." or ".."))
                throw new InvalidDataException("مسار ملف داخل التحديث غير آمن: " + raw);
            if (!names.Add(raw))
                throw new InvalidDataException("اسم ملف مكرر داخل التحديث: " + raw);
            if (entry.Length > 512L * 1024 * 1024)
                throw new InvalidDataException("أحد ملفات التحديث يتجاوز 512 MiB: " + raw);
        }
    }

    private static void ValidateManifest(SignedUpdateManifest manifest, string expectedVersion)
    {
        if (manifest.SchemaVersion != 1 ||
            !VersionPattern.IsMatch(NormalizeVersion(manifest.Version)) ||
            !VersionPattern.IsMatch(NormalizeVersion(manifest.MinimumSupportedVersion)) ||
            !NormalizeVersion(manifest.Version).Equals(expectedVersion, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(manifest.Publisher) || manifest.Publisher.Length > 160 ||
            manifest.Files is null || manifest.Files.Count is < 1 or > MaximumFiles ||
            string.IsNullOrWhiteSpace(manifest.MainExecutable) ||
            string.IsNullOrWhiteSpace(manifest.ReleaseNotes) && manifest.ReleaseNotes is null)
            throw new InvalidDataException("Manifest تحديث SoftBox غير صالح أو لا يطابق الإصدار المطلوب.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            ValidateRelativePath(file.Path);
            if (!seen.Add(file.Path) || file.SizeBytes is < 0 or > 512L * 1024 * 1024 ||
                file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("قائمة ملفات التحديث تحتوي مسارًا مكررًا أو حجمًا أو بصمة غير صالحة.");
        }
        if (!seen.Contains(manifest.MainExecutable))
            throw new InvalidDataException("ملف التشغيل الرئيسي غير موجود في قائمة Manifest.");
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || path.StartsWith("/", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl) ||
            path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("مسار ملف تحديث غير آمن: " + path);
    }

    private static string ResolveSafeChildPath(string root, string relative)
    {
        ValidateRelativePath(relative);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("مسار ملف تحديث خرج عن مجلد المرحلة.");
        return full;
    }

    private static bool IsChildPath(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string value)
        => value.Trim().TrimStart('v', 'V');

    private static string BuildEncodedPowerShell(string script)
        => Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
}
