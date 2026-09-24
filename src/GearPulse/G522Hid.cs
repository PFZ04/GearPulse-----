// SPDX-License-Identifier: GPL-2.0-or-later
// Read-only battery protocol adapted from Sapd/HeadsetControl's G522 implementation.
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GearPulse;

public static class G522Hid
{
    private const ushort Vendor = 0x046d;
    private const ushort Product = 0x0b18; // LIGHTSPEED receiver; 0x0b19 is USB wired.
    private const int PacketSize = 64;
    public sealed record BatteryReply(string Status, int? Battery = null, bool? Charging = null);
    private sealed record Interface(string Path, int InputLength, int OutputLength);

    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public int Size; public Guid Class; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Attributes { public int Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] private struct Caps
    {
        public ushort Usage, UsagePage, Input, Output, Feature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort Link, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Overlapped { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint needed, IntPtr device);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, uint count, IntPtr bytes, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeFileHandle handle, IntPtr buffer, uint count, IntPtr bytes, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(SafeFileHandle handle, IntPtr overlapped, out uint bytes, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);

    public static byte[] BuildBatteryRequest()
    {
        var bytes = new byte[PacketSize];
        bytes[0] = 0x50; bytes[1] = 0x23; bytes[2] = 0x0b;
        bytes[4] = 0x03; bytes[5] = 0x1a; bytes[7] = 0x03;
        bytes[9] = 0x05; bytes[10] = 0x0a;
        return bytes;
    }

    public static BatteryReply? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 3 || bytes[0] != 0x50 || bytes[1] != 0x23) return null;
        if (bytes[2] == 0x05) return bytes.Length >= 8 && bytes[7] == 0
            ? new BatteryReply("device_offline") : null;
        if (bytes[2] != 0x0b || bytes.Length < 14 || bytes[9] != 0x05) return null;
        if (bytes[11] > 100) return new BatteryReply("error");
        return new BatteryReply("ok", bytes[11], bytes[13] == 0x02);
    }

    private static IReadOnlyList<Interface> Enumerate()
    {
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        var found = new List<Interface>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var needed, IntPtr.Zero);
                if (needed < 8) continue;
                var detail = Marshal.AllocHGlobal((int)needed);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, needed, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (path is null || !path.Contains("vid_046d&pid_0b18", StringComparison.OrdinalIgnoreCase)) continue;
                    using var handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid) continue;
                    var attributes = new Attributes { Size = Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(handle, ref attributes) || attributes.Vendor != Vendor || attributes.Product != Product) continue;
                    if (!HidD_GetPreparsedData(handle, out var prepared)) continue;
                    try
                    {
                        if (HidP_GetCaps(prepared, out var caps) == 0x110000 &&
                            caps.UsagePage == 0xffa0 && caps.Usage == 1 &&
                            caps.Input >= PacketSize && caps.Output >= PacketSize)
                            found.Add(new Interface(path, caps.Input, caps.Output));
                    }
                    finally { HidD_FreePreparsedData(prepared); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return found;
    }

    private static byte[] Transfer(SafeFileHandle handle, byte[]? output, int length, int timeoutMs, Action? afterStart = null)
    {
        var buffer = Marshal.AllocHGlobal(length);
        var overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset);
        try
        {
            Marshal.StructureToPtr(new Overlapped { Event = ready.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
            if (output is not null) Marshal.Copy(output, 0, buffer, length);
            var done = output is null ? ReadFile(handle, buffer, (uint)length, IntPtr.Zero, overlapped)
                : WriteFile(handle, buffer, (uint)length, IntPtr.Zero, overlapped);
            var error = Marshal.GetLastWin32Error();
            if (!done && error != 997) throw new Win32Exception(error);
            if (afterStart is not null)
            {
                try { afterStart(); }
                catch
                {
                    if (!done) { CancelIoEx(handle, overlapped); GetOverlappedResult(handle, overlapped, out _, true); }
                    throw;
                }
            }
            if (!done && !ready.WaitOne(timeoutMs))
            {
                CancelIoEx(handle, overlapped);
                GetOverlappedResult(handle, overlapped, out _, true);
                throw new TimeoutException("G522 response timed out.");
            }
            if (!GetOverlappedResult(handle, overlapped, out var count, true)) throw new Win32Exception();
            if (output is not null && count != length) throw new IOException("Incomplete G522 HID output.");
            var result = new byte[count];
            Marshal.Copy(buffer, result, 0, (int)count);
            return result;
        }
        finally { Marshal.FreeHGlobal(overlapped); Marshal.FreeHGlobal(buffer); }
    }

    private static BatteryReply Query(SafeFileHandle handle, Interface device, int timeoutMs)
    {
        var output = new byte[device.OutputLength];
        BuildBatteryRequest().CopyTo(output, 0);
        var watch = Stopwatch.StartNew();
        var first = true;
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            var remaining = Math.Max(1, timeoutMs - (int)watch.ElapsedMilliseconds);
            var response = Transfer(handle, null, device.InputLength, remaining,
                first ? () => Transfer(handle, output, output.Length, 500) : null);
            first = false;
            var parsed = Parse(response);
            if (parsed is not null) return parsed;
        }
        throw new TimeoutException("No G522 battery reply.");
    }

    public static IReadOnlyList<DeviceState> Sample(int timeoutMs = 1000)
    {
        IReadOnlyList<Interface> devices;
        try { devices = Enumerate(); }
        catch (Exception error) { AppLog.Write("G522 interface discovery failed", error); return []; }
        var states = new List<DeviceState>();
        var container = devices.Count > 0 ? AtkDeviceDiscovery.UniqueContainer(Vendor, Product) : Guid.Empty;
        foreach (var device in devices)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(device.Path.ToUpperInvariant()));
            var id = $"g522-{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
            BatteryReply value;
            try
            {
                using var handle = CreateFileW(device.Path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                value = Query(handle, device, timeoutMs);
            }
            catch (TimeoutException) { value = new BatteryReply("unavailable"); }
            catch (Exception error) { AppLog.Write("G522 battery query failed", error); value = new BatteryReply("error"); }
            states.Add(new DeviceState(id, "Logitech G522 LIGHTSPEED", "headset", value.Battery,
                value.Charging, true, value.Status, "wireless", container));
        }
        return states;
    }
}
