using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>Launches one worker in a no-capability AppContainer and limits it using a Job Object.</summary>
public sealed class WindowsAppContainerProcessLauncher
{
    private const int AlreadyExists = unchecked((int)0x800700B7);
    private const int SecurityCapabilitiesAttribute = 0x00020009;
    private const uint ExtendedStartupInfo = 0x00080000;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint UnicodeEnvironment = 0x00000400;
    private const int JobExtendedLimits = 9;
    private const int JobCpuRate = 15;
    private const uint LimitActiveProcess = 0x8;
    private const uint LimitProcessMemory = 0x100;
    private const uint KillOnClose = 0x2000;
    private const uint CpuEnable = 0x1;
    private const uint CpuHardCap = 0x4;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 0x102;
    private const uint WaitFailed = 0xFFFFFFFF;

    public async Task<NativePluginSandboxProcessResult> RunAsync(
        string rootPath, string executablePath, string requestPath, string responsePath,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows AppContainer is required.");
        if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var root = Path.GetFullPath(rootPath);
        var exe = Path.GetFullPath(executablePath);
        var request = Path.GetFullPath(requestPath);
        var response = Path.GetFullPath(responsePath);
        EnsureUnderRoot(root, exe);
        EnsureUnderRoot(root, request);
        EnsureUnderRoot(root, response);
        if (!Directory.Exists(root) || !File.Exists(exe))
            throw new DirectoryNotFoundException("The private AppContainer staging directory or worker is missing.");

        var name = "SoftBox.PluginSandbox." + Guid.NewGuid().ToString("N");
        IntPtr sid = IntPtr.Zero, attributes = IntPtr.Zero, capabilities = IntPtr.Zero;
        IntPtr job = IntPtr.Zero, environment = IntPtr.Zero;
        PROCESS_INFORMATION process = default;
        var created = false;

        try
        {
            var hr = CreateAppContainerProfile(name, name, "SoftBox native plugin worker", IntPtr.Zero, 0, out var createdSid);
            if (hr == 0) sid = createdSid;
            else if (hr == AlreadyExists)
            {
                if (createdSid != IntPtr.Zero) FreeSid(createdSid);
                Check(DeriveAppContainerSidFromAppContainerName(name, out sid));
            }
            else
            {
                if (createdSid != IntPtr.Zero) FreeSid(createdSid);
                Check(hr);
            }

            GrantRootOnlyAccess(root, new SecurityIdentifier(sid));
            var startup = BuildStartupInfo(sid, out attributes, out capabilities);
            job = CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw LastError("CreateJobObject");
            ConfigureJob(job);
            environment = BuildMinimalEnvironment(root);

            var command = new StringBuilder(Quote(exe) + " --request " + Quote(request) + " --response " + Quote(response));
            if (!CreateProcessW(exe, command, IntPtr.Zero, IntPtr.Zero, false,
                    ExtendedStartupInfo | CreateSuspended | CreateNoWindow | UnicodeEnvironment,
                    environment, Path.GetDirectoryName(exe)!, ref startup, out process))
                throw LastError("CreateProcessW");
            created = true;

            if (!AssignProcessToJobObject(job, process.Process)) throw LastError("AssignProcessToJobObject");
            if (ResumeThread(process.Thread) == uint.MaxValue) throw LastError("ResumeThread");

            using var registration = cancellationToken.Register(() =>
            {
                try { TerminateJobObject(job, 0x53425831); } catch { }
            });
            var wait = await Task.Run(() => WaitForSingleObject(process.Process,
                checked((uint)Math.Clamp(timeout.TotalMilliseconds, 1, 120_000))), CancellationToken.None);
            if (cancellationToken.IsCancellationRequested)
                return new(true, false, true, null, "Cancelled; the AppContainer Job Object was terminated.", name);
            if (wait == WaitTimeout)
            {
                TerminateJobObject(job, 0x53425831);
                WaitForSingleObject(process.Process, 5_000);
                return new(true, true, false, null, "Native extension exceeded its 120-second execution limit.", name);
            }
            if (wait == WaitFailed) throw LastError("WaitForSingleObject");
            if (wait != WaitObject0) throw new InvalidOperationException("Unexpected worker wait result.");
            if (!GetExitCodeProcess(process.Process, out var exit)) throw LastError("GetExitCodeProcess");
            return new(true, false, false, unchecked((int)exit), null, name);
        }
        finally
        {
            if (created)
            {
                if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
                if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
            }
            if (job != IntPtr.Zero) CloseHandle(job);
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
            if (capabilities != IntPtr.Zero) Marshal.FreeHGlobal(capabilities);
            if (attributes != IntPtr.Zero) { DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); }
            if (sid != IntPtr.Zero) FreeSid(sid);
            try { DeleteAppContainerProfile(name); } catch { }
        }
    }

    private static STARTUPINFOEX BuildStartupInfo(IntPtr sid, out IntPtr attributeList, out IntPtr capabilities)
    {
        attributeList = IntPtr.Zero;
        capabilities = IntPtr.Zero;
        nuint required = 0;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref required);
        if (required == 0) throw LastError("InitializeProcThreadAttributeList(size)");
        attributeList = Marshal.AllocHGlobal(checked((int)required));
        if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref required))
            throw LastError("InitializeProcThreadAttributeList");

        var security = new SECURITY_CAPABILITIES
        {
            AppContainerSid = sid,
            Capabilities = IntPtr.Zero,
            CapabilityCount = 0,
            Reserved = 0
        };
        capabilities = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_CAPABILITIES>());
        Marshal.StructureToPtr(security, capabilities, false);
        if (!UpdateProcThreadAttribute(attributeList, 0, (IntPtr)SecurityCapabilitiesAttribute,
                capabilities, (nuint)Marshal.SizeOf<SECURITY_CAPABILITIES>(), IntPtr.Zero, IntPtr.Zero))
            throw LastError("UpdateProcThreadAttribute");
        var result = new STARTUPINFOEX();
        result.StartupInfo.cb = (uint)Marshal.SizeOf<STARTUPINFOEX>();
        result.AttributeList = attributeList;
        return result;
    }

    private static void ConfigureJob(IntPtr job)
    {
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = LimitActiveProcess | LimitProcessMemory | KillOnClose,
                ActiveProcessLimit = 1
            },
            ProcessMemoryLimit = new UIntPtr(256UL * 1024 * 1024)
        };
        SetJobInformation(job, JobExtendedLimits, limits);
        var cpu = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            ControlFlags = CpuEnable | CpuHardCap,
            CpuRate = 1500
        };
        SetJobInformation(job, JobCpuRate, cpu);
    }

    private static void SetJobInformation<T>(IntPtr job, int klass, T data) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(data, pointer, false);
            if (!SetInformationJobObject(job, klass, pointer, (uint)size))
                throw LastError("SetInformationJobObject");
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static void GrantRootOnlyAccess(string root, SecurityIdentifier sid)
    {
        var info = new DirectoryInfo(root);
        var security = info.GetAccessControl(AccessControlSections.Access);
        security.AddAccessRule(new FileSystemAccessRule(sid,
            FileSystemRights.ReadAndExecute | FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    private static IntPtr BuildMinimalEnvironment(string root)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SYSTEMROOT"] = windows,
            ["WINDIR"] = windows,
            ["PATH"] = Path.Combine(windows, "System32"),
            ["TEMP"] = root,
            ["TMP"] = root
        };
        return Marshal.StringToHGlobalUni(string.Join('\0', values.Select(x => x.Key + "=" + x.Value)) + "\0\0");
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
    private static void EnsureUnderRoot(string root, string path)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escaped native plugin sandbox root.");
    }
    private static void Check(int hresult) { if (hresult < 0) Marshal.ThrowExceptionForHR(hresult); }
    private static Win32Exception LastError(string operation)
        => new(Marshal.GetLastWin32Error(), operation + " failed.");

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_CAPABILITIES { public IntPtr AppContainerSid; public IntPtr Capabilities; public uint CapabilityCount; public uint Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public uint cb; public IntPtr lpReserved; public IntPtr lpDesktop; public IntPtr lpTitle;
        public uint dwX; public uint dwY; public uint dwXSize; public uint dwYSize;
        public uint dwXCountChars; public uint dwYCountChars; public uint dwFillAttribute; public uint dwFlags;
        public ushort wShowWindow; public ushort cbReserved2; public IntPtr lpReserved2;
        public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr AttributeList; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr Process; public IntPtr Thread; public uint ProcessId; public uint ThreadId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize; public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass; public uint SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount; public ulong WriteOperationCount; public ulong OtherOperationCount;
        public ulong ReadTransferCount; public ulong WriteTransferCount; public ulong OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit; public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed; public UIntPtr PeakJobMemoryUsed;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION { public uint ControlFlags; public uint CpuRate; }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int CreateAppContainerProfile(string name, string display, string description, IntPtr capabilities, uint count, out IntPtr sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeleteAppContainerProfile(string name);
    [DllImport("advapi32.dll")] private static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        bool inherit, uint flags, IntPtr environment, string currentDirectory, ref STARTUPINFOEX startup, out PROCESS_INFORMATION info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetInformationJobObject")]
    private static extern bool SetInformationJobObject(IntPtr job, int klass, IntPtr information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(IntPtr job, uint exitCode);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
