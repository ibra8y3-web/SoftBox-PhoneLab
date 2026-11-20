using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record ApkSignerTool(string JavaExecutable, string ApkSignerJarPath, string BuildToolsVersion);

public interface IApkSignerToolLocator
{
    ApkSignerTool? Locate();
}

public sealed class ApkSignerToolLocator : IApkSignerToolLocator
{
    public ApkSignerTool? Locate()
    {
        var sdkRoots = GetSdkRoots();
        foreach (var root in sdkRoots)
        {
            var buildTools = Path.Combine(root, "build-tools");
            if (!Directory.Exists(buildTools))
                continue;

            var candidates = Directory.GetDirectories(buildTools)
                .Select(path => new
                {
                    Path = path,
                    Name = Path.GetFileName(path),
                    Version = ParseStableVersion(Path.GetFileName(path))
                })
                .Where(item => item.Version is not null)
                .OrderByDescending(item => item.Version)
                .ToArray();

            foreach (var candidate in candidates)
            {
                var jarCandidates = new[]
                {
                    Path.Combine(candidate.Path, "lib", "apksigner.jar"),
                    Path.Combine(candidate.Path, "apksigner.jar")
                };
                var jar = jarCandidates.FirstOrDefault(File.Exists);
                if (jar is null)
                    continue;

                var java = FindJavaExecutable(root);
                if (!string.IsNullOrWhiteSpace(java))
                    return new ApkSignerTool(java, jar, candidate.Name);
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetSdkRoots()
    {
        var roots = new List<string>();
        Add(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"));
        Add(Environment.GetEnvironmentVariable("ANDROID_HOME"));

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Add(Path.Combine(local, "Android", "Sdk"));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            Add(Path.Combine(programFiles, "Android", "Android Studio", "sdk"));
            Add(Path.Combine(programFiles, "Android", "sdk"));
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            Add(Path.Combine(programFilesX86, "Android", "Android Studio", "sdk"));

        return roots;

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            try
            {
                var full = Path.GetFullPath(value.Trim());
                if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase))
                    roots.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
    }

    private static Version? ParseStableVersion(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName) ||
            folderName.Contains("rc", StringComparison.OrdinalIgnoreCase) ||
            folderName.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
            folderName.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
            folderName.Contains("alpha", StringComparison.OrdinalIgnoreCase))
            return null;

        return Version.TryParse(folderName, out var version) ? version : null;
    }

    private static string? FindJavaExecutable(string sdkRoot)
    {
        var candidates = new List<string>();
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));

        var sdkParent = Directory.GetParent(sdkRoot)?.FullName;
        if (!string.IsNullOrWhiteSpace(sdkParent))
            candidates.Add(Path.Combine(sdkParent, "jbr", "bin", "java.exe"));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            candidates.Add(Path.Combine(programFiles, "Android", "Android Studio", "jbr", "bin", "java.exe"));

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            candidates.Add(Path.Combine(programFilesX86, "Android", "Android Studio", "jbr", "bin", "java.exe"));

        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return candidate;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, "java.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }

        return null;
    }
}

public interface IApkSignerCommandRunner
{
    Task<ApkSignerCommandResult> RunAsync(
        string javaExecutable,
        string apksignerJarPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

public sealed record ApkSignerCommandResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class ProcessApkSignerCommandRunner : IApkSignerCommandRunner
{
    public async Task<ApkSignerCommandResult> RunAsync(
        string javaExecutable,
        string apksignerJarPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo
        {
            FileName = javaExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(apksignerJarPath) ?? Environment.CurrentDirectory
        };
        start.ArgumentList.Add("-jar");
        start.ArgumentList.Add(apksignerJarPath);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("تعذر تشغيل Java لتشغيل apksigner.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return new ApkSignerCommandResult(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            if (!cancellationToken.IsCancellationRequested)
                throw new TimeoutException("تجاوز التحقق من توقيع APK مهلة الثلاث دقائق.");
            throw;
        }
    }
}

public enum ApkSignatureVerificationStatus
{
    Verified,
    InvalidSignature,
    ToolUnavailable,
    FileChangedDuringVerification
}

public sealed record ApkSignerCertificate(
    int SignerNumber,
    string? DistinguishedName,
    string? Sha256Digest,
    string? Sha1Digest,
    string? PublicKeyAlgorithm,
    int? PublicKeySizeBits);

public sealed record ApkSignatureVerificationResult(
    ApkSignatureVerificationStatus Status,
    string FileName,
    long FileSizeBytes,
    string Sha256,
    string? BuildToolsVersion,
    IReadOnlyList<ApkSignerCertificate> Certificates,
    IReadOnlyDictionary<string, bool> VerifiedSchemes,
    IReadOnlyList<string> Warnings,
    string StandardOutput,
    string StandardError)
{
    public bool CryptographicVerificationPassed => Status == ApkSignatureVerificationStatus.Verified;
}

/// <summary>
/// Verifies APK v1/v2/v3 signatures using the official Android SDK apksigner tool.
/// It does not sign or modify the APK and does not treat a valid signature as proof of a trusted publisher.
/// </summary>
public sealed class ApkSignatureVerificationService
{
    private static readonly Regex CertificateLine = new(
        @"^Signer #(?<signer>\d+) certificate (?<field>DN|SHA-256 digest|SHA-1 digest):\s*(?<value>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex KeyLine = new(
        @"^Signer #(?<signer>\d+) key (?<field>algorithm|size \(bits\)):\s*(?<value>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SchemeLine = new(
        @"^Verified using (?<scheme>.+):\s*(?<value>true|false)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IApkSignerToolLocator _locator;
    private readonly IApkSignerCommandRunner _runner;

    public ApkSignatureVerificationService(
        IApkSignerToolLocator? locator = null,
        IApkSignerCommandRunner? runner = null)
    {
        _locator = locator ?? new ApkSignerToolLocator();
        _runner = runner ?? new ProcessApkSignerCommandRunner();
    }

    public async Task<ApkSignatureVerificationResult> VerifyAsync(
        string apkPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apkPath))
            throw new ArgumentException("حدد مسار ملف APK.", nameof(apkPath));

        var fullPath = Path.GetFullPath(apkPath);
        if (!Path.GetExtension(fullPath).Equals(".apk", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("التحقق التشفيري يدعم ملفات APK فقط؛ ملفات AAB تحتاج أداة أخرى.", nameof(apkPath));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("ملف APK غير موجود.", fullPath);

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length is <= 0 or > 8L * 1024 * 1024 * 1024)
            throw new InvalidDataException("ملف APK فارغ أو يتجاوز حد الفحص البالغ 8 GiB.");

        var before = await ComputeSha256Async(fullPath, cancellationToken);
        var tool = _locator.Locate();
        if (tool is null)
        {
            return new ApkSignatureVerificationResult(
                ApkSignatureVerificationStatus.ToolUnavailable,
                fileInfo.Name,
                fileInfo.Length,
                before,
                null,
                [],
                new Dictionary<string, bool>(),
                ["لم يتم العثور على Java وAndroid SDK Build-Tools مع apksigner.jar. ثبّت Android SDK Build-Tools من SDK Manager، ثم أعد المحاولة."],
                "",
                "");
        }

        var command = await _runner.RunAsync(
            tool.JavaExecutable,
            tool.ApkSignerJarPath,
            ["verify", "--verbose", "--print-certs", fullPath],
            cancellationToken);

        var after = await ComputeSha256Async(fullPath, cancellationToken);
        var warnings = new List<string>();
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            warnings.Add("تغير محتوى APK أثناء التحقق؛ تم رفض نتيجة الأداة لأن الملف لم يعد هو نفس الملف الذي بدأ فحصه.");
            return new ApkSignatureVerificationResult(
                ApkSignatureVerificationStatus.FileChangedDuringVerification,
                fileInfo.Name,
                new FileInfo(fullPath).Length,
                after,
                tool.BuildToolsVersion,
                ParseCertificates(command.StandardOutput + Environment.NewLine + command.StandardError),
                ParseSchemes(command.StandardOutput + Environment.NewLine + command.StandardError),
                warnings,
                command.StandardOutput,
                command.StandardError);
        }

        var schemes = ParseSchemes(command.StandardOutput + Environment.NewLine + command.StandardError);
        var certificates = ParseCertificates(command.StandardOutput + Environment.NewLine + command.StandardError);
        var verified = command.ExitCode == 0;
        if (verified && certificates.Count == 0)
            warnings.Add("نجح apksigner في التحقق، لكن لم تُرجع الأداة تفاصيل شهادة قابلة للاستخراج.");
        if (!verified)
            warnings.Add("رفض apksigner التوقيع أو لم يستطع التحقق منه. لا تثبّت الملف بناءً على بيانات Manifest أو SHA-256 فقط.");
        warnings.Add("التحقق يثبت سلامة مخططات توقيع APK المدعومة على Android حسب apksigner؛ لا يثبت أن الشهادة تخص الناشر الرسمي أو أن التطبيق آمن.");
        warnings.Add("لم يتم تعديل APK أو تثبيته أو رفعه. بصمة الشهادة SHA-256 تُعرض للمقارنة مع بصمة ناشر موثوقة تحصل عليها من قناة مستقلة.");

        return new ApkSignatureVerificationResult(
            verified ? ApkSignatureVerificationStatus.Verified : ApkSignatureVerificationStatus.InvalidSignature,
            fileInfo.Name,
            fileInfo.Length,
            before,
            tool.BuildToolsVersion,
            certificates,
            schemes,
            warnings,
            command.StandardOutput,
            command.StandardError);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IReadOnlyList<ApkSignerCertificate> ParseCertificates(string output)
    {
        var builders = new Dictionary<int, CertificateBuilder>();
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var match = CertificateLine.Match(line);
            if (match.Success)
            {
                var signer = int.Parse(match.Groups["signer"].Value, CultureInfo.InvariantCulture);
                var builder = GetBuilder(builders, signer);
                switch (match.Groups["field"].Value.ToUpperInvariant())
                {
                    case "DN": builder.DistinguishedName = match.Groups["value"].Value.Trim(); break;
                    case "SHA-256 DIGEST": builder.Sha256Digest = NormalizeDigest(match.Groups["value"].Value); break;
                    case "SHA-1 DIGEST": builder.Sha1Digest = NormalizeDigest(match.Groups["value"].Value); break;
                }
                continue;
            }

            match = KeyLine.Match(line);
            if (!match.Success)
                continue;

            var keySigner = int.Parse(match.Groups["signer"].Value, CultureInfo.InvariantCulture);
            var keyBuilder = GetBuilder(builders, keySigner);
            if (match.Groups["field"].Value.Equals("algorithm", StringComparison.OrdinalIgnoreCase))
                keyBuilder.PublicKeyAlgorithm = match.Groups["value"].Value.Trim();
            else if (int.TryParse(match.Groups["value"].Value.Trim(), NumberStyles.None,
                         CultureInfo.InvariantCulture, out var bits))
                keyBuilder.PublicKeySizeBits = bits;
        }

        return builders.OrderBy(pair => pair.Key)
            .Select(pair => pair.Value.ToRecord(pair.Key))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, bool> ParseSchemes(string output)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = SchemeLine.Match(rawLine.Trim());
            if (match.Success)
                result[match.Groups["scheme"].Value.Trim()] =
                    bool.Parse(match.Groups["value"].Value);
        }
        return result;
    }

    private static CertificateBuilder GetBuilder(IDictionary<int, CertificateBuilder> builders, int signer)
    {
        if (!builders.TryGetValue(signer, out var builder))
        {
            builder = new CertificateBuilder();
            builders.Add(signer, builder);
        }
        return builder;
    }

    private static string NormalizeDigest(string value)
        => new(value.Where(Uri.IsHexDigit).Select(char.ToLowerInvariant).ToArray());

    private sealed class CertificateBuilder
    {
        public string? DistinguishedName { get; set; }
        public string? Sha256Digest { get; set; }
        public string? Sha1Digest { get; set; }
        public string? PublicKeyAlgorithm { get; set; }
        public int? PublicKeySizeBits { get; set; }

        public ApkSignerCertificate ToRecord(int signer)
            => new(signer, DistinguishedName, Sha256Digest, Sha1Digest, PublicKeyAlgorithm, PublicKeySizeBits);
    }
}
