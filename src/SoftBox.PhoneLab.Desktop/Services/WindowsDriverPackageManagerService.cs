using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record DriverPackageInventoryReport(
    string ExecutablePath,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    DateTimeOffset ReadAtUtc);

public sealed record DriverPackageInstallValidation(
    bool IsReady,
    string InfPath,
    string CatalogPath,
    DriverInfCompatibilityReport? Compatibility,
    WindowsDriverSignatureReport? CatalogSignature,
    DriverCatalogMembershipReport? CatalogMembership,
    IReadOnlyList<string> Blockers);

public sealed record DriverPackageOperationResult(
    string Operation,
    string Target,
    bool Succeeded,
    int? ExitCode,
    string Message,
    DateTimeOffset CompletedAtUtc);

/// <summary>
/// Explicit Windows PnPUtil package management. Installation is fail-closed until exact device
/// Hardware IDs, Windows target, CAT signature and INF catalog membership have been verified.
/// Removal never uses PnPUtil's force option.
/// </summary>
public sealed class WindowsDriverPackageManagerService
{
    private static readonly Regex PublishedInfPattern = new(
        @"^oem[0-9]{1,6}\.inf$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly WindowsDriverSignatureVerificationService _signatureService;

    public WindowsDriverPackageManagerService(WindowsDriverSignatureVerificationService? signatureService = null)
    {
        _signatureService = signatureService ?? new WindowsDriverSignatureVerificationService();
    }

    public static bool IsValidPublishedInfName(string? value)
        => !string.IsNullOrWhiteSpace(value) && PublishedInfPattern.IsMatch(value.Trim());

    public static IReadOnlyList<string> BuildAddDriverArguments(string infPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(infPath);
        var fullPath = Path.GetFullPath(infPath);
        if (!Path.GetExtension(fullPath).Equals(".inf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A Windows driver INF file is required.", nameof(infPath));
        return ["/add-driver", fullPath, "/install"];
    }

    public static IReadOnlyList<string> BuildExportDriverArguments(string publishedInfName, string destinationFolder)
    {
        ValidatePublishedInfName(publishedInfName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);
        return ["/export-driver", publishedInfName.Trim(), Path.GetFullPath(destinationFolder)];
    }

    public static IReadOnlyList<string> BuildDeleteDriverArguments(string publishedInfName)
    {
        ValidatePublishedInfName(publishedInfName);
        // Deliberately omit /force; Windows may refuse deletion while dependencies remain.
        return ["/delete-driver", publishedInfName.Trim(), "/uninstall"];
    }

    public async Task<DriverPackageInventoryReport> ListInstalledPackagesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        var executable = ResolvePnPUtil();
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("/enum-drivers");

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("Windows PnPUtil did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new DriverPackageInventoryReport(
            executable, process.ExitCode, await stdout, await stderr, DateTimeOffset.UtcNow);
    }

    public async Task<DriverPackageInstallValidation> ValidateInstallCandidateAsync(
        string infPath,
        string catalogPath,
        IReadOnlyCollection<string>? targetHardwareIds,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        var blockers = new List<string>();
        var inf = Path.GetFullPath(infPath);
        var cat = Path.GetFullPath(catalogPath);

        if (!File.Exists(inf) || !Path.GetExtension(inf).Equals(".inf", StringComparison.OrdinalIgnoreCase))
            blockers.Add("اختر ملف INF موجودًا وصحيح الامتداد.");
        if (!File.Exists(cat) || !Path.GetExtension(cat).Equals(".cat", StringComparison.OrdinalIgnoreCase))
            blockers.Add("اختر ملف CAT موجودًا وصحيح الامتداد.");
        if (blockers.Count > 0)
            return new(false, inf, cat, null, null, null, blockers);

        var infDirectory = Path.GetDirectoryName(inf);
        var catDirectory = Path.GetDirectoryName(cat);
        if (!string.Equals(infDirectory, catDirectory, StringComparison.OrdinalIgnoreCase))
            blockers.Add("يجب اختيار INF وCAT من مجلد الحزمة نفسها؛ لا يتم دمج ملفات تعريف من مصادر مختلفة.");

        var safeHardwareIds = targetHardwareIds ?? Array.Empty<string>();
        if (safeHardwareIds.Count == 0)
            blockers.Add("لا توجد Hardware IDs فعلية للجهاز المستهدف. لا يمكن إثبات توافق تعريف عام باسم الشركة أو الموديل فقط.");

        DriverInfCompatibilityReport? compatibility = null;
        if (blockers.Count == 0)
        {
            compatibility = await DriverInfCompatibilityAnalyzer.AnalyzeFileAsync(
                inf, safeHardwareIds, WindowsDriverTarget.Current, cancellationToken);
            if (compatibility.Status != DriverInfMatchStatus.Compatible)
                blockers.Add("لم يثبت توافق INF مع Hardware IDs وإصدار Windows والمعمارية الحالية: " +
                    compatibility.Status + " — " + compatibility.Explanation);
        }

        WindowsDriverSignatureReport? signature = null;
        DriverCatalogMembershipReport? membership = null;
        if (blockers.Count == 0)
        {
            signature = _signatureService.Verify(cat);
            if (!signature.IsTrusted)
                blockers.Add("رفض Windows الثقة في توقيع CAT: " + signature.TrustStatusText);
        }
        if (blockers.Count == 0)
        {
            membership = await _signatureService.VerifyCatalogMembershipAsync(cat, inf, cancellationToken);
            if (!membership.IsVerified)
                blockers.Add("لم يتم إثبات أن INF عضو في CAT موقع وموثوق: " + membership.StatusMessage);
        }

        return new(blockers.Count == 0, inf, cat, compatibility, signature, membership, blockers);
    }

    public async Task<DriverPackageOperationResult> InstallVerifiedAsync(
        string infPath,
        string catalogPath,
        IReadOnlyCollection<string>? targetHardwareIds,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateInstallCandidateAsync(
            infPath, catalogPath, targetHardwareIds, cancellationToken);
        if (!validation.IsReady)
            return new("Install", validation.InfPath, false, null,
                "تم منع التثبيت: " + string.Join(" | ", validation.Blockers), DateTimeOffset.UtcNow);

        var command = await RunElevatedAsync(
            BuildAddDriverArguments(validation.InfPath), cancellationToken);
        return new("Install", validation.InfPath, command.ExitCode == 0, command.ExitCode,
            command.ExitCode == 0
                ? "قبل PnPUtil طلب إضافة الحزمة وتثبيتها على الأجهزة المتطابقة. تحقق من حالة الجهاز بعد إعادة الفحص."
                : "رفض PnPUtil تثبيت الحزمة. راجع سجل Windows وسبب الخروج؛ لم يتم اعتبار التعريف مثبتًا.",
            DateTimeOffset.UtcNow);
    }

    public async Task<DriverPackageOperationResult> ExportInstalledPackageAsync(
        string publishedInfName,
        string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        ValidatePublishedInfName(publishedInfName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);
        var folder = Path.GetFullPath(destinationFolder);
        Directory.CreateDirectory(folder);
        var command = await RunElevatedAsync(
            BuildExportDriverArguments(publishedInfName, folder), cancellationToken);
        return new("Export", publishedInfName.Trim(), command.ExitCode == 0, command.ExitCode,
            command.ExitCode == 0
                ? "اكتمل طلب تصدير حزمة التعريف إلى مجلد النسخة الاحتياطية."
                : "فشل تصدير حزمة التعريف. تحقق من اسم oemNN.inf والصلاحيات ومساحة التخزين.",
            DateTimeOffset.UtcNow);
    }

    public async Task<DriverPackageOperationResult> RemoveInstalledPackageAsync(
        string publishedInfName,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        ValidatePublishedInfName(publishedInfName);
        var command = await RunElevatedAsync(
            BuildDeleteDriverArguments(publishedInfName), cancellationToken);
        return new("Remove", publishedInfName.Trim(), command.ExitCode == 0, command.ExitCode,
            command.ExitCode == 0
                ? "قبل PnPUtil إزالة حزمة التعريف من Driver Store وإلغاء ربطها بالأجهزة. افحص الأجهزة المتأثرة قبل المتابعة."
                : "رفض Windows إزالة الحزمة؛ قد تكون مستخدمة أو مطلوبة لجهاز آخر. لم يتم استخدام /force.",
            DateTimeOffset.UtcNow);
    }

    private sealed record ElevatedCommandResult(int ExitCode);

    private static async Task<ElevatedCommandResult> RunElevatedAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        EnsureWindows();
        var start = new ProcessStartInfo
        {
            FileName = ResolvePnPUtil(),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(start) ??
                throw new InvalidOperationException("Windows PnPUtil did not start.");
            await process.WaitForExitAsync(cancellationToken);
            return new ElevatedCommandResult(process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new UnauthorizedAccessException("ألغى المستخدم طلب صلاحيات المسؤول؛ لم ينفذ PnPUtil العملية.", ex);
        }
    }

    private static string ResolvePnPUtil()
    {
        var path = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
        if (!File.Exists(path))
            throw new FileNotFoundException("لم يتم العثور على أداة Windows PnPUtil.", path);
        return path;
    }

    private static void ValidatePublishedInfName(string? value)
    {
        if (!IsValidPublishedInfName(value))
            throw new ArgumentException("اكتب اسم حزمة منشورًا صالحًا بالشكل oemNN.inf كما يظهر في pnputil /enum-drivers.");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows driver package management is available only on Windows.");
    }
}
