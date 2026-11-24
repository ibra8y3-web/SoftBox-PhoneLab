using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record WindowsDriverSignatureReport(
    string FilePath,
    bool IsTrusted,
    int TrustStatus,
    string TrustStatusText,
    string SignatureType,
    string SignerSubject,
    string SignerThumbprint,
    DateTimeOffset? CertificateNotBefore,
    DateTimeOffset? CertificateNotAfter,
    string Limitation);

public sealed record DriverCatalogMembershipReport(
    string CatalogPath,
    string MemberPath,
    bool IsVerified,
    int? ExitCode,
    string ToolPath,
    string StandardOutput,
    string StandardError,
    string StatusMessage);

/// <summary>
/// Verifies Windows Authenticode signatures with WinVerifyTrust. A selected CAT verifies
/// the catalog publisher signature; it does not, by itself, prove that a separate SYS file
/// is a member of that catalog.
/// </summary>
public sealed class WindowsDriverSignatureVerificationService
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
    private const uint WtdUiNone = 2;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionIgnore = 0;
    private const uint WtdCacheOnlyUrlRetrieval = 0x00001000;

    public static bool IsSupportedFileExtension(string? path)
    {
        var extension = Path.GetExtension(path ?? string.Empty);
        return extension.Equals(".cat", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".sys", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    public WindowsDriverSignatureReport Verify(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Authenticode driver verification requires Windows.");

        var fullPath = Path.GetFullPath(path);
        if (!IsSupportedFileExtension(fullPath))
            throw new ArgumentException("Choose an OEM driver .cat catalog or a .sys/.dll/.exe file.", nameof(path));
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length <= 0)
            throw new FileNotFoundException("The selected driver file does not exist or is empty.", fullPath);

        var trustStatus = VerifyWithWinTrust(fullPath);
        var trusted = trustStatus == 0;
        var subject = string.Empty;
        var thumbprint = string.Empty;
        DateTimeOffset? notBefore = null;
        DateTimeOffset? notAfter = null;
        if (trusted)
        {
            try
            {
                // WinVerifyTrust above establishes the trust verdict. This legacy API is used only
                // to extract the signer certificate embedded in an Authenticode-signed PE/CAT.
                // X509CertificateLoader.LoadCertificateFromFile reads PEM/DER certificate files;
                // it does not extract the Authenticode signer from a signed executable or catalog.
#pragma warning disable SYSLIB0057
                using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(fullPath));
#pragma warning restore SYSLIB0057
                subject = certificate.Subject;
                thumbprint = certificate.Thumbprint ?? string.Empty;
                notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);
                notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            }
            catch (CryptographicException)
            {
                // WinVerifyTrust remains authoritative for the trust verdict; certificate display is best-effort.
            }
        }

        var signatureType = Path.GetExtension(fullPath).Equals(".cat", StringComparison.OrdinalIgnoreCase)
            ? "Authenticode catalog signature (.cat)"
            : "Embedded Authenticode signature";

        var limitation = Path.GetExtension(fullPath).Equals(".cat", StringComparison.OrdinalIgnoreCase)
            ? "تحقق Windows من توقيع ملف الكتالوج نفسه. هذا الفحص لا يثبت أن ملف SYS منفصل عضو داخل الكتالوج؛ حافظ على ملفات الحزمة الأصلية واستخدم أداة Windows للتحقق من التثبيت."
            : "تحقق Windows من التوقيع المضمّن في الملف المحدد فقط. بعض تعريفات SYS موقّعة عبر CAT ولا تحتوي توقيعًا مضمّنًا؛ افحص ملف CAT الأصلي في تلك الحالة.";

        return new WindowsDriverSignatureReport(
            fullPath,
            trusted,
            trustStatus,
            GetTrustStatusText(trustStatus),
            signatureType,
            subject,
            thumbprint,
            notBefore,
            notAfter,
            limitation);
    }

    public static bool IsSupportedCatalogMemberExtension(string? path)
    {
        var extension = Path.GetExtension(path ?? string.Empty);
        return extension.Equals(".sys", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".inf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cat", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies both the CAT publisher signature and the selected package member's hash
    /// against the catalog using Microsoft's SignTool policy-aware catalog verification.
    /// Fails closed when the Windows SDK SignTool is unavailable.
    /// </summary>
    public async Task<DriverCatalogMembershipReport> VerifyCatalogMembershipAsync(
        string catalogPath,
        string memberPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberPath);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Driver catalog membership verification requires Windows.");

        var catalog = Path.GetFullPath(catalogPath);
        var member = Path.GetFullPath(memberPath);
        if (!Path.GetExtension(catalog).Equals(".cat", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The catalog file must have a .cat extension.", nameof(catalogPath));
        if (!IsSupportedCatalogMemberExtension(member))
            throw new ArgumentException("The selected package member must be SYS, DLL, EXE, INF or CAT.", nameof(memberPath));
        if (!File.Exists(catalog))
            throw new FileNotFoundException("The selected catalog file was not found.", catalog);
        if (!File.Exists(member))
            throw new FileNotFoundException("The selected driver package member was not found.", member);

        var signTool = FindWindowsSdkSignTool();
        if (signTool is null)
        {
            return new DriverCatalogMembershipReport(
                catalog, member, false, null, string.Empty, string.Empty, string.Empty,
                "لم يتم العثور على signtool.exe موثوق من Windows SDK. لم يتم اعتماد عضوية الملف داخل CAT؛ ثبّت Windows SDK أو استخدم جهاز فحص مجهزًا به.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = signTool,
            WorkingDirectory = Path.GetDirectoryName(catalog) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in BuildCatalogMembershipArguments(catalog, member))
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("تعذر تشغيل Microsoft SignTool.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new DriverCatalogMembershipReport(
                catalog, member, false, null, signTool,
                Limit(await outputTask), Limit(await errorTask),
                "انتهت مهلة تحقق Windows SignTool بعد 30 ثانية. لم يتم اعتماد توقيع التعريف.");
        }

        var standardOutput = Limit(await outputTask);
        var standardError = Limit(await errorTask);
        var success = process.ExitCode == 0;
        return new DriverCatalogMembershipReport(
            catalog,
            member,
            success,
            process.ExitCode,
            signTool,
            standardOutput,
            standardError,
            success
                ? "قبل Microsoft SignTool توقيع الكتالوج وتطابق الملف المحدد مع أحد أعضائه وفق سياسة PnP."
                : "رفض Microsoft SignTool التحقق من توقيع CAT أو عضوية الملف. لا تستخدم هذه الحزمة كتعريف موثوق.");
    }

    public static IReadOnlyList<string> BuildCatalogMembershipArguments(string catalogPath, string memberPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberPath);
        var arguments = new List<string> { "verify", "/v" };
        if (Path.GetExtension(memberPath).Equals(".sys", StringComparison.OrdinalIgnoreCase))
            arguments.Add("/kp");
        arguments.Add("/c");
        arguments.Add(Path.GetFullPath(catalogPath));
        arguments.Add(Path.GetFullPath(memberPath));
        return arguments;
    }

    private static string? FindWindowsSdkSignTool()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Kits", "10", "bin")
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;
            string[] versions;
            try
            {
                versions = Directory.GetDirectories(root)
                    .OrderByDescending(path =>
                    {
                        var name = Path.GetFileName(path);
                        return Version.TryParse(name, out var version) ? version : new Version(0, 0);
                    })
                    .ToArray();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var version in versions)
            foreach (var architecture in new[] { "x64", "x86", "arm64" })
            {
                var candidate = Path.Combine(version, architecture, "signtool.exe");
                if (!File.Exists(candidate))
                    continue;
                try
                {
                    if (VerifyWithWinTrust(candidate) == 0)
                        return candidate;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                           System.ComponentModel.Win32Exception or
                                           System.Runtime.InteropServices.COMException)
                {
                    // A missing or untrusted SDK utility must never be executed.
                }
            }
        }

        return null;
    }

    private static string Limit(string value)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= 12000 ? value : value[..12000];

    private static int VerifyWithWinTrust(string filePath)
    {
        var fileInfo = new WinTrustFileInfo
        {
            CbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = Marshal.StringToCoTaskMemUni(filePath),
            FileHandle = IntPtr.Zero,
            KnownSubject = IntPtr.Zero
        };
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var dataPointer = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var data = new WinTrustData
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                PolicyCallbackData = IntPtr.Zero,
                SipClientData = IntPtr.Zero,
                UiChoice = WtdUiNone,
                RevocationChecks = 0,
                UnionChoice = WtdChoiceFile,
                FileInfo = fileInfoPointer,
                StateAction = WtdStateActionIgnore,
                StateData = IntPtr.Zero,
                UrlReference = IntPtr.Zero,
                ProviderFlags = WtdCacheOnlyUrlRetrieval,
                UiContext = 0,
                SignatureSettings = IntPtr.Zero
            };
            dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPointer, false);
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, dataPointer);
        }
        finally
        {
            if (dataPointer != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WinTrustData>(dataPointer);
                Marshal.FreeHGlobal(dataPointer);
            }
            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
            Marshal.FreeCoTaskMem(fileInfo.FilePath);
        }
    }

    private static string GetTrustStatusText(int status)
    {
        if (status == 0) return "Windows trust provider accepted the signature.";
        return unchecked((uint)status) switch
        {
            0x800B0100 => "No Authenticode signature was found.",
            0x800B0101 => "The signing certificate or timestamp is expired.",
            0x800B0109 => "The signing certificate chain ends in an untrusted root.",
            0x80096010 => "The file digest does not match the signed digest.",
            0x800B0110 => "The certificate chain is not trusted.",
            _ => new Win32Exception(status).Message
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint CbStruct;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint CbStruct;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        IntPtr trustData);
}
