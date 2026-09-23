// SPDX-License-Identifier: GPL-2.0-or-later
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GearPulse;

// HID++ 2.0 reads only: device identity, feature discovery and unified battery.
public static class LogitechHid
{
    private const ushort Vendor = 0x046d;
    private const ushort DeviceInfoFeature = 0x0003;
    private const ushort DeviceNameFeature = 0x0005;
    private const ushort UnifiedBatteryFeature = 0x1004;
    private const ushort BatteryStatusFeature = 0x1000;
    private const byte SoftwareId = 0x0a;
    private static readonly Dictionary<string, DiscoveredDevice> known = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> scannedPaths = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime nextDiscoveryUtc;

    public sealed record Result(string Status, int? Battery = null, bool? Charging = null);
    public sealed record Identity(string ModelId, string Name, string Icon);
    private sealed record DiscoveredDevice(string Path, byte Slot, Identity Identity, byte BatteryIndex, ushort BatteryFeature)
    {
        public string Id => MakeId(Path, Slot, Identity.ModelId);
    }
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
                    if (path is null || !path.Contains("vid_046d", StringComparison.OrdinalIgnoreCase) ||
                        !path.Contains("&pid_", StringComparison.OrdinalIgnoreCase)) continue;
                    using var handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid) continue;
                    var attributes = new Attributes { Size = Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(handle, ref attributes) || attributes.Vendor != Vendor) continue;
                    // A wired mouse is a separate USB device; only receiver interfaces are eligible.
                    if ((attributes.Product & 0xff00) != 0xc500) continue;
                    if (!HidD_GetPreparsedData(handle, out var preparsed)) continue;
                    try
                    {
                        if (HidP_GetCaps(preparsed, out var caps) != 0x110000) continue;
                        if (caps.Input >= 20 && caps.Output >= 20 && caps.UsagePage >= 0xff00)
                            found.Add(new Interface(path, caps.Input, caps.Output));
                    }
                    finally { HidD_FreePreparsedData(preparsed); }
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
            var done = output is null
                ? ReadFile(handle, buffer, (uint)length, IntPtr.Zero, overlapped)
                : WriteFile(handle, buffer, (uint)length, IntPtr.Zero, overlapped);
            var error = Marshal.GetLastWin32Error();
            if (!done && error != 997) throw new Win32Exception(error);
            if (afterStart is not null)
            {
                try { afterStart(); }
                catch { if (!done) { CancelIoEx(handle, overlapped); GetOverlappedResult(handle, overlapped, out _, true); } throw; }
            }
            if (!done && !ready.WaitOne(timeoutMs))
            {
                CancelIoEx(handle, overlapped);
                GetOverlappedResult(handle, overlapped, out _, true);
                throw new TimeoutException("Logitech HID request timed out.");
            }
            if (!GetOverlappedResult(handle, overlapped, out var count, true)) throw new Win32Exception();
            if (output is not null && count != length) throw new IOException("Incomplete Logitech HID output report.");
            var result = new byte[count];
            Marshal.Copy(buffer, result, 0, (int)count);
            return result;
        }
        finally { Marshal.FreeHGlobal(overlapped); Marshal.FreeHGlobal(buffer); }
    }

    public static byte[] BuildRequest(byte deviceIndex, byte featureIndex, byte function, ushort parameter = 0)
    {
        if (deviceIndex is < 1 or > 6 || function > 2) throw new ArgumentException("Only paired-device read requests are allowed.");
        var request = new byte[20];
        request[0] = 0x11;
        request[1] = deviceIndex;
        request[2] = featureIndex;
        request[3] = (byte)((function << 4) | SoftwareId);
        request[4] = (byte)(parameter >> 8);
        request[5] = (byte)parameter;
        return request;
    }

    public static bool IsReply(ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte function)
    {
        return response.Length >= 7 && response[0] is 0x10 or 0x11 && response[1] == deviceIndex &&
            response[2] == featureIndex && response[3] == (byte)((function << 4) | SoftwareId);
    }

    public static Identity? ParseIdentity(ReadOnlySpan<byte> info, ReadOnlySpan<byte> type, string name)
    {
        // 0x0003 GetDeviceInfo: payload bytes 7..12 hold transport model IDs.
        if (info.Length < 17 || type.Length < 5 || string.IsNullOrWhiteSpace(name) ||
            name.Length > 64 || name.Any(char.IsControl)) return null;
        var model = info.Slice(11, 6);
        if (model.IndexOfAnyExcept((byte)0) < 0) return null;
        var icon = type[4] switch { 0 => "keyboard", 3 => "mouse", _ => null };
        if (icon is null) return null;
        return new Identity(Convert.ToHexString(model), name.Trim(), icon);
    }

    public static string MakeId(string receiverPath, byte slot, string modelId)
    {
        var pathHash = SHA256.HashData(Encoding.UTF8.GetBytes(receiverPath.ToUpperInvariant()));
        return $"logitech-{Convert.ToHexString(pathHash.AsSpan(0, 8)).ToLowerInvariant()}-{slot}-{modelId.ToLowerInvariant()}";
    }

    public static bool ReportsPercentage(ushort feature, ReadOnlySpan<byte> capabilities) =>
        feature switch
        {
            UnifiedBatteryFeature => capabilities.Length >= 6 && (capabilities[5] & 2) != 0,
            BatteryStatusFeature => capabilities.Length >= 6 && capabilities[4] >= 10 && (capabilities[5] & 2) != 0,
            _ => false
        };

    public static Result ParseBattery(ushort feature, ReadOnlySpan<byte> response)
    {
        if (response.Length < 7 || response[4] > 100 ||
            (feature == BatteryStatusFeature && response[4] == 0)) return new Result("error");
        bool? charging = response[6] switch
        {
            0 or 3 => false,
            1 or 2 => true,
            4 when feature == BatteryStatusFeature => true,
            _ => null
        };
        return new Result("ok", response[4], charging);
    }

    private static byte[] Request(SafeFileHandle handle, Interface device, byte slot, byte feature, byte function, ushort parameter, int timeoutMs)
    {
        var request = BuildRequest(slot, feature, function, parameter);
        var output = new byte[device.OutputLength];
        request.CopyTo(output, 0);
        var watch = Stopwatch.StartNew();
        var first = true;
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            var remaining = Math.Max(1, timeoutMs - (int)watch.ElapsedMilliseconds);
            var response = Transfer(handle, null, device.InputLength, remaining,
                first ? () => Transfer(handle, output, output.Length, 500) : null);
            first = false;
            if (response.Length < 7 || response[0] is not (0x10 or 0x11) || response[1] != slot) continue;
            if (IsError(response, slot, feature, function))
                throw new IOException($"HID++ error {response[5]}.");
            if (IsReply(response, slot, feature, function)) return response;
        }
        throw new TimeoutException("No matching Logitech HID response.");
    }

    public static bool IsError(ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte function) =>
        response.Length >= 6 && response[0] is (0x10 or 0x11) && response[1] == deviceIndex &&
        response[2] is (0x8f or 0xff) && response[3] == featureIndex &&
        response[4] == (byte)((function << 4) | SoftwareId);

    private static string ReadDeviceName(SafeFileHandle handle, Interface device, byte slot, int timeoutMs)
    {
        var nameFeature = Request(handle, device, slot, 0, 0, DeviceNameFeature, timeoutMs)[4];
        if (nameFeature == 0) return "";
        var length = Request(handle, device, slot, nameFeature, 0, 0, timeoutMs)[4];
        if (length is 0 or > 64) return "";
        var bytes = new byte[length];
        for (var offset = 0; offset < length;)
        {
            var response = Request(handle, device, slot, nameFeature, 1, (ushort)(offset << 8), timeoutMs);
            var count = Math.Min(length - offset, response.Length - 4);
            Array.Copy(response, 4, bytes, offset, count);
            offset += count;
        }
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static DiscoveredDevice? Discover(SafeFileHandle handle, Interface device, byte slot, int timeoutMs)
    {
        var infoIndex = Request(handle, device, slot, 0, 0, DeviceInfoFeature, timeoutMs)[4];
        var nameIndex = Request(handle, device, slot, 0, 0, DeviceNameFeature, timeoutMs)[4];
        if (infoIndex == 0 || nameIndex == 0) return null;
        var info = Request(handle, device, slot, infoIndex, 0, 0, timeoutMs);
        var type = Request(handle, device, slot, nameIndex, 2, 0, timeoutMs);
        var name = ReadDeviceName(handle, device, slot, timeoutMs);
        var identity = ParseIdentity(info, type, name);
        if (identity is null) return null;
        foreach (var feature in new[] { UnifiedBatteryFeature, BatteryStatusFeature })
        {
            try
            {
                var index = Request(handle, device, slot, 0, 0, feature, timeoutMs)[4];
                if (index == 0) continue;
                var capabilities = Request(handle, device, slot, index,
                    feature == UnifiedBatteryFeature ? (byte)0 : (byte)1, 0, timeoutMs);
                if (ReportsPercentage(feature, capabilities))
                    return new DiscoveredDevice(device.Path, slot, identity, index, feature);
            }
            catch (IOException) { /* Try the other exact-percentage battery feature. */ }
        }
        return null;
    }

    public static IReadOnlyList<DeviceState> Sample(int timeoutMs = 250)
    {
        using var mutex = new Mutex(false, @"Local\GearPulseLogitechHid");
        bool held;
        try { held = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { held = true; }
        if (!held) return known.Values.Select(x => State(x, new Result("device_busy"))).ToArray();
        try
        {
            var interfaces = Enumerate();
            var paths = interfaces.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in known.Keys.Where(x => !paths.Contains(known[x].Path)).ToArray()) known.Remove(key);
            scannedPaths.RemoveWhere(x => !paths.Contains(x));
            var discover = DateTime.UtcNow >= nextDiscoveryUtc || interfaces.Any(x => !scannedPaths.Contains(x.Path));
            if (discover) nextDiscoveryUtc = DateTime.UtcNow.AddSeconds(45);
            var states = new List<DeviceState>();
            foreach (var device in interfaces)
            {
                using var handle = CreateFileW(device.Path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    states.AddRange(known.Values.Where(x => x.Path == device.Path).Select(x => State(x, new Result("error"))));
                    continue;
                }
                if (discover)
                {
                    scannedPaths.Add(device.Path);
                    var watch = Stopwatch.StartNew();
                    for (byte slot = 1; slot <= 6 && watch.ElapsedMilliseconds < 1500; slot++)
                    {
                        try
                        {
                            var found = Discover(handle, device, slot, Math.Min(timeoutMs, 150));
                            var previous = known.Values.FirstOrDefault(x => x.Path == device.Path && x.Slot == slot);
                            if (found is not null)
                            {
                                if (previous is not null) known.Remove(previous.Id);
                                known[found.Id] = found;
                            }
                            else if (previous is not null) known.Remove(previous.Id);
                        }
                        catch (TimeoutException) { /* Sleeping slots retain their identity but no old battery. */ }
                        catch (IOException) { /* Another slot can still respond. */ }
                        catch (Win32Exception) { /* Receiver may have been unplugged. */ }
                    }
                }
                foreach (var item in known.Values.Where(x => x.Path == device.Path).OrderBy(x => x.Slot))
                {
                    try
                    {
                        var response = Request(handle, device, item.Slot, item.BatteryIndex,
                            item.BatteryFeature == UnifiedBatteryFeature ? (byte)1 : (byte)0, 0, timeoutMs);
                        states.Add(State(item, ParseBattery(item.BatteryFeature, response)));
                    }
                    catch (TimeoutException) { states.Add(State(item, new Result("device_offline"))); }
                    catch (IOException) { states.Add(State(item, new Result("device_offline"))); }
                    catch (Win32Exception) { states.Add(State(item, new Result("device_offline"))); }
                }
            }
            return states;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static DeviceState State(DiscoveredDevice device, Result result) =>
        new(device.Id, device.Identity.Name, device.Identity.Icon, result.Battery,
            result.Charging, true, result.Status);
}
