using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SoftBox.PhoneLab.Core.Security;

/// <summary>
/// Encrypts local SoftBox data with Windows DPAPI in CurrentUser scope.
/// Files are bound to both the current Windows user and a purpose-specific entropy value.
/// This is local data-at-rest protection, not a portable backup format.
/// </summary>
public static class WindowsUserDataProtection
{
    private static ReadOnlySpan<byte> Magic => "SBXDPAPI1"u8;
    private const int CryptProtectUiForbidden = 0x1;

    public static byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose)
    {
        if (plaintext.IsEmpty)
            throw new ArgumentException("Protected local data cannot be empty.", nameof(plaintext));

        var protectedPayload = Transform(plaintext.ToArray(), CreateEntropy(purpose), protect: true);
        var result = new byte[Magic.Length + protectedPayload.Length];
        Magic.CopyTo(result);
        protectedPayload.CopyTo(result, Magic.Length);
        CryptographicOperations.ZeroMemory(protectedPayload);
        return result;
    }

    /// <summary>
    /// Decrypts a DPAPI file or returns an unmodified legacy plaintext payload.
    /// The caller must immediately re-save legacy plaintext through Protect.
    /// </summary>
    public static byte[] UnprotectOrLegacy(
        ReadOnlySpan<byte> storedBytes,
        string purpose,
        out bool wasProtected)
    {
        if (storedBytes.Length < Magic.Length ||
            !storedBytes[..Magic.Length].SequenceEqual(Magic))
        {
            wasProtected = false;
            return storedBytes.ToArray();
        }

        wasProtected = true;
        var payload = storedBytes[Magic.Length..].ToArray();
        try
        {
            return Transform(payload, CreateEntropy(purpose), protect: false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static bool IsProtected(ReadOnlySpan<byte> storedBytes)
        => storedBytes.Length >= Magic.Length && storedBytes[..Magic.Length].SequenceEqual(Magic);

    private static byte[] CreateEntropy(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (purpose.Length > 120 || purpose.Any(char.IsControl))
            throw new ArgumentException("Protection purpose is invalid.", nameof(purpose));

        return SHA256.HashData(Encoding.UTF8.GetBytes("SoftBox.PhoneLab.DPAPI.v1|" + purpose));
    }

    private static byte[] Transform(byte[] inputBytes, byte[] entropyBytes, bool protect)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("SoftBox local database protection requires Windows DPAPI.");

        GCHandle inputHandle = default;
        GCHandle entropyHandle = default;
        DataBlob output = default;
        IntPtr description = IntPtr.Zero;

        try
        {
            inputHandle = GCHandle.Alloc(inputBytes, GCHandleType.Pinned);
            entropyHandle = GCHandle.Alloc(entropyBytes, GCHandleType.Pinned);

            var input = new DataBlob(inputBytes.Length, inputHandle.AddrOfPinnedObject());
            var entropy = new DataBlob(entropyBytes.Length, entropyHandle.AddrOfPinnedObject());

            var succeeded = protect
                ? CryptProtectData(
                    ref input,
                    "SoftBox PhoneLab local data",
                    ref entropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output)
                : CryptUnprotectData(
                    ref input,
                    out description,
                    ref entropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output);

            if (!succeeded)
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error());
                throw new CryptographicException(
                    "Windows DPAPI could not protect or unlock this user's local SoftBox data.",
                    error);
            }

            if (output.cbData <= 0 || output.pbData == IntPtr.Zero)
                throw new CryptographicException("Windows DPAPI returned an empty data buffer.");

            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (output.pbData != IntPtr.Zero)
                _ = LocalFree(output.pbData);
            if (description != IntPtr.Zero)
                _ = LocalFree(description);
            if (inputHandle.IsAllocated)
                inputHandle.Free();
            if (entropyHandle.IsAllocated)
                entropyHandle.Free();
            CryptographicOperations.ZeroMemory(inputBytes);
            CryptographicOperations.ZeroMemory(entropyBytes);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;

        public DataBlob(int size, IntPtr data)
        {
            cbData = size;
            pbData = data;
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string szDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        out IntPtr ppszDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
