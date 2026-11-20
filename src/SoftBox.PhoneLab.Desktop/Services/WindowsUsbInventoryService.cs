using SoftBox.PhoneLab.Core.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record WindowsUsbDevice(
    string InstanceId,
    string FriendlyName,
    string Manufacturer,
    string DeviceClass,
    string Service,
    string VendorId,
    string ProductId,
    int ProblemCode,
    string PnpStatus,
    UsbHardwareIdentity Identity);

/// <summary>
/// Read-only inventory of present Windows PnP devices using SetupAPI and Configuration Manager.
/// Does not install, remove, restart or change drivers/devices.
/// </summary>
public sealed class WindowsUsbInventoryService
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const uint SpdrpDevDesc = 0x00000000;
    private const uint SpdrpClass = 0x00000007;
    private const uint SpdrpMfg = 0x0000000B;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private const uint SpdrpService = 0x00000004;
    private const uint DnHasProblem = 0x00000400;
    private const int ErrorNoMoreItems = 259;

    public Task<IReadOnlyList<WindowsUsbDevice>> EnumeratePresentDevicesAsync(
        CancellationToken cancellationToken = default)
        => Task.Run(() => EnumeratePresentDevices(cancellationToken), cancellationToken);

    private static IReadOnlyList<WindowsUsbDevice> EnumeratePresentDevices(CancellationToken cancellationToken)
    {
        var handle = SetupDiGetClassDevs(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (handle == InvalidHandleValue)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows SetupAPI could not enumerate present devices.");

        try
        {
            var results = new List<WindowsUsbDevice>();
            for (uint index = 0; index < 100_000; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var data = new SpDevInfoData { CbSize = (uint)Marshal.SizeOf<SpDevInfoData>() };
                if (!SetupDiEnumDeviceInfo(handle, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                        break;
                    throw new Win32Exception(error, "Windows SetupAPI failed while enumerating device records.");
                }

                var instanceId = ReadInstanceId(handle, ref data);
                if (string.IsNullOrWhiteSpace(instanceId) ||
                    !instanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
                    continue;

                var (vendorId, productId) = ExtractHardwareIds(instanceId);
                var friendly = ReadProperty(handle, ref data, SpdrpFriendlyName);
                if (string.IsNullOrWhiteSpace(friendly))
                    friendly = ReadProperty(handle, ref data, SpdrpDevDesc);
                var manufacturer = ReadProperty(handle, ref data, SpdrpMfg);
                var deviceClass = ReadProperty(handle, ref data, SpdrpClass);
                var service = ReadProperty(handle, ref data, SpdrpService);

                var status = "Unknown";
                var problemCode = 0;
                if (CM_Get_DevNode_Status(out var nodeStatus, out var problem, data.DevInst, 0) == 0)
                {
                    problemCode = (nodeStatus & DnHasProblem) != 0 ? checked((int)problem) : 0;
                    status = problemCode == 0 ? "Present" : "Problem";
                }

                var identity = UsbHardwareIdentityCatalog.Classify(vendorId, productId);
                results.Add(new WindowsUsbDevice(
                    instanceId,
                    string.IsNullOrWhiteSpace(friendly) ? instanceId : friendly,
                    manufacturer ?? "",
                    deviceClass ?? "",
                    service ?? "",
                    vendorId ?? "----",
                    productId ?? "----",
                    problemCode,
                    status,
                    identity));
            }

            return results
                .OrderByDescending(device => device.ProblemCode > 0)
                .ThenBy(device => device.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(handle);
        }
    }

    private static string? ReadInstanceId(IntPtr handle, ref SpDevInfoData data)
    {
        var builder = new StringBuilder(4096);
        if (!SetupDiGetDeviceInstanceId(handle, ref data, builder, (uint)builder.Capacity, out _))
            return null;
        return builder.ToString();
    }

    private static string? ReadProperty(IntPtr handle, ref SpDevInfoData data, uint property)
    {
        _ = SetupDiGetDeviceRegistryProperty(handle, ref data, property,
            out var dataType, IntPtr.Zero, 0, out var requiredBytes);
        if (requiredBytes == 0 || requiredBytes > 64 * 1024)
            return null;

        var buffer = Marshal.AllocHGlobal((int)requiredBytes);
        try
        {
            if (!SetupDiGetDeviceRegistryProperty(handle, ref data, property,
                    out dataType, buffer, requiredBytes, out _))
                return null;

            if (dataType is 1 or 2)
                return Marshal.PtrToStringUni(buffer)?.TrimEnd('\0', ' ', '\r', '\n');

            if (dataType == 7)
            {
                var multi = Marshal.PtrToStringUni(buffer) ?? "";
                return multi.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (string? VendorId, string? ProductId) ExtractHardwareIds(string instanceId)
    {
        var vendor = Regex.Match(instanceId, @"VID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var product = Regex.Match(instanceId, @"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return (
            vendor.Success ? vendor.Groups[1].Value.ToUpperInvariant() : null,
            product.Success ? product.Groups[1].Value.ToUpperInvariant() : null);
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        StringBuilder deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        IntPtr propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(
        out uint status,
        out uint problemNumber,
        uint devInst,
        uint flags);
}
