using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SoftBox.PluginHost;

internal static class Program
{
    private const int TokenIsAppContainer = 29;
    private const uint TokenQuery = 0x0008;
    private const int ApiVersion = 1;
    private const int MaxRequest = 64 * 1024;
    private const int MaxResponse = 1024 * 1024;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Execute(IntPtr input, int inputLength, IntPtr output, int outputCapacity, out int outputLength);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Shutdown();

    private sealed record Request(string PluginPath, string Json);
    private sealed record Response(bool Success, int ApiVersion, string? Json, string? Error, DateTimeOffset CompletedAtUtc);

    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || !IsAppContainer())
            return 20;

        string? responsePath = null;
        try
        {
            if (args.Length == 2 && args[0] == "--self-test")
            {
                responsePath = Path.GetFullPath(args[1]);
                await WriteAsync(responsePath, new(true, ApiVersion,
                    JsonSerializer.Serialize(new { appContainer = true, apiVersion = ApiVersion }), null, DateTimeOffset.UtcNow));
                return 0;
            }

            if (args.Length != 4 || args[0] != "--request" || args[2] != "--response")
                throw new InvalidDataException("Invalid PluginHost command line.");
            var requestPath = Path.GetFullPath(args[1]);
            responsePath = Path.GetFullPath(args[3]);
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            Under(root, requestPath);
            Under(root, responsePath);

            var info = new FileInfo(requestPath);
            if (!info.Exists || info.Length is <= 0 or > MaxRequest)
                throw new InvalidDataException("Host request is missing or too large.");
            var request = JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(requestPath))
                ?? throw new InvalidDataException("Host request JSON is invalid.");
            var plugin = Path.GetFullPath(request.PluginPath);
            Under(root, plugin);
            if (!File.Exists(plugin) || !plugin.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native plugin DLL is missing.");
            if (Encoding.UTF8.GetByteCount(request.Json) > MaxRequest)
                throw new InvalidDataException("Native plugin request exceeds 64 KiB.");
            using (JsonDocument.Parse(request.Json)) { }

            var output = Invoke(plugin, request.Json);
            await WriteAsync(responsePath, new(true, ApiVersion, output, null, DateTimeOffset.UtcNow));
            return 0;
        }
        catch (Exception ex)
        {
            if (responsePath is not null)
            {
                try { await WriteAsync(responsePath, new(false, ApiVersion, null, Limit(ex.Message), DateTimeOffset.UtcNow)); }
                catch { }
            }
            return 10;
        }
    }

    private static string Invoke(string path, string json)
    {
        IntPtr lib = IntPtr.Zero, input = IntPtr.Zero, output = IntPtr.Zero;
        byte[]? inputBytes = null;
        Shutdown? shutdown = null;
        try
        {
            lib = NativeLibrary.Load(path, DllImportSearchPath.SafeDirectories);
            if (!NativeLibrary.TryGetExport(lib, "SoftBoxPluginGetApiVersion", out var versionPtr) ||
                !NativeLibrary.TryGetExport(lib, "SoftBoxPluginExecute", out var executePtr))
                throw new InvalidDataException("Required SoftBox C ABI exports are missing.");
            var version = Marshal.GetDelegateForFunctionPointer<GetVersion>(versionPtr);
            if (version() != ApiVersion) throw new InvalidDataException("Unsupported plugin API version.");
            if (NativeLibrary.TryGetExport(lib, "SoftBoxPluginShutdown", out var shutdownPtr))
                shutdown = Marshal.GetDelegateForFunctionPointer<Shutdown>(shutdownPtr);

            inputBytes = Encoding.UTF8.GetBytes(json);
            input = Marshal.AllocHGlobal(inputBytes.Length);
            Marshal.Copy(inputBytes, 0, input, inputBytes.Length);
            output = Marshal.AllocHGlobal(MaxResponse);
            var execute = Marshal.GetDelegateForFunctionPointer<Execute>(executePtr);
            var status = execute(input, inputBytes.Length, output, MaxResponse, out var length);
            if (status != 0) throw new InvalidOperationException("Plugin execution returned code " + status);
            if (length is < 1 or > MaxResponse) throw new InvalidDataException("Plugin response length is invalid.");
            var bytes = new byte[length];
            Marshal.Copy(output, bytes, 0, length);
            try
            {
                var result = new UTF8Encoding(false, true).GetString(bytes);
                using (JsonDocument.Parse(result)) { }
                return result;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally
        {
            try { shutdown?.Invoke(); } catch { }
            if (inputBytes is not null) CryptographicOperations.ZeroMemory(inputBytes);
            if (output != IntPtr.Zero) { Zero(output, MaxResponse); Marshal.FreeHGlobal(output); }
            if (input != IntPtr.Zero) Marshal.FreeHGlobal(input);
            if (lib != IntPtr.Zero) NativeLibrary.Free(lib);
        }
    }

    private static void Zero(IntPtr pointer, int length)
    {
        var zeroes = new byte[Math.Min(length, 64 * 1024)];
        for (var offset = 0; offset < length; offset += zeroes.Length)
            Marshal.Copy(zeroes, 0, IntPtr.Add(pointer, offset), Math.Min(zeroes.Length, length - offset));
    }

    private static bool IsAppContainer()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!GetTokenInformation(token, TokenIsAppContainer, out var result, sizeof(uint), out _))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return result != 0;
        }
        finally { CloseHandle(token); }
    }

    private static void Under(string root, string path)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("File path escaped AppContainer staging root.");
    }

    private static string Limit(string value) => value.Length > 2048 ? value[..2048] : value;
    private static Task WriteAsync(string path, Response response)
        => File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(response));

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int klass, out uint value, int length, out int returned);
}
