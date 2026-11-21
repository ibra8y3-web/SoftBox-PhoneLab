using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
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
                using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(fullPath));
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
