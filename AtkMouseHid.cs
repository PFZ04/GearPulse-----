// SPDX-License-Identifier: GPL-2.0-or-later
// Read-only battery diagnostics for ATK/VXE COMPX 2.4 GHz receivers.
// The three commands below are status queries; no settings or firmware commands are sent.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

public static class AtkMouseHid
{
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid Class; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Attributes { public int Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] struct DeviceInfoData { public int Size; public Guid Class; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] struct Caps {
        public ushort Usage, UsagePage, Input, Output, Feature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort Link, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [StructLayout(LayoutKind.Sequential)] struct Overlapped { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attributes a);
    [DllImport("hid.dll", CharSet=CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder text, int byteLength);
    [DllImport("hid.dll", CharSet=CharSet.Unicode)] static extern bool HidD_GetManufacturerString(SafeFileHandle h, StringBuilder text, int byteLength);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out Caps caps);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint needed, IntPtr device);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint needed, ref DeviceInfoData device);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref DeviceInfoData device, ref PropertyKey key, out uint type, byte[] value, uint length, out uint needed, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, uint count, IntPtr bytes, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, uint count, IntPtr bytes, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out uint bytes, bool wait);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);

    public sealed class Device { public string Path { get; set; } public int InputLength { get; set; } public int OutputLength { get; set; } public int FeatureLength { get; set; } public int UsagePage { get; set; } public int Usage { get; set; } public int Vendor { get; set; } public int Product { get; set; } public Guid ContainerId { get; set; } public string ProductName { get; set; } public string ManufacturerName { get; set; } }
    public sealed class Result { public string Status { get; set; } = "unavailable"; public bool ReceiverPresent { get; set; } public bool? Online { get; set; } public int? Battery { get; set; } public bool? Charging { get; set; } public int? Cid { get; set; } public int? Mid { get; set; } public string Name { get; set; } public string Error { get; set; } public Device Device { get; set; } }

    public static Device[] Enumerate() => EnumerateCore(true, 0x373b, 0x3554);

    // Diagnostics can inspect other ATK collections without sending a report to them.
    public static Device[] EnumerateAll() => EnumerateCore(false, 0x373b, 0x3554);

    // Shared read-only HID enumeration for Razer's standard feature-report interface.
    public static Device[] EnumerateVendor(int vendor) => vendor is 0x1532 or 0x3537 or 0x374a
        ? EnumerateCore(false, vendor) : throw new ArgumentOutOfRangeException(nameof(vendor));

    public static bool IsBatteryInterface(Device device) => device != null &&
        device.UsagePage==0xff02 && device.Usage==2 &&
        device.InputLength==17 && device.OutputLength==17;

    static Device[] EnumerateCore(bool batteryOnly, params int[] vendors)
    {
        HidD_GetHidGuid(out Guid guid);
        IntPtr set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        var found = new List<Device>();
        try {
            for (uint i=0;;i++) {
                var data = new InterfaceData { Size=Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint needed, IntPtr.Zero);
                IntPtr detail = Marshal.AllocHGlobal((int)needed);
                try {
                    Marshal.WriteInt32(detail, IntPtr.Size==8 ? 8 : 6);
                    var info = new DeviceInfoData { Size=Marshal.SizeOf<DeviceInfoData>() };
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, needed, out needed, ref info)) throw new Win32Exception();
                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (path==null || !vendors.Any(v =>
                        path.Contains($"vid_{v:x4}&pid_",StringComparison.OrdinalIgnoreCase) ||
                        path.Contains($"vid&0002{v:x4}_pid&",StringComparison.OrdinalIgnoreCase))) continue;
                    using (var h = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
                        if (h.IsInvalid) continue;
                        var a = new Attributes { Size=Marshal.SizeOf<Attributes>() };
                        if (!HidD_GetAttributes(h, ref a) || !vendors.Contains(a.Vendor)) continue;
                        if (!HidD_GetPreparsedData(h, out IntPtr p)) continue;
                        try {
                            if (HidP_GetCaps(p, out Caps c)!=0x110000) continue;
                            if (!batteryOnly || (c.UsagePage==0xff02 && c.Usage==2 && c.Input==17 && c.Output==17)) {
                                var key=new PropertyKey { Format=new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), Id=2 };
                                var bytes=new byte[16];
                                Guid container=SetupDiGetDevicePropertyW(set,ref info,ref key,out uint type,bytes,16,out uint ignored,0) && type==0x0000000d ? new Guid(bytes) : Guid.Empty;
                                var productName=new StringBuilder(256); var manufacturerName=new StringBuilder(256);
                                HidD_GetProductString(h,productName,512);
                                HidD_GetManufacturerString(h,manufacturerName,512);
                                found.Add(new Device { Path=path, InputLength=c.Input, OutputLength=c.Output, FeatureLength=c.Feature, UsagePage=c.UsagePage, Usage=c.Usage, Vendor=a.Vendor, Product=a.Product, ContainerId=container, ProductName=productName.ToString(), ManufacturerName=manufacturerName.ToString() });
                            }
                        } finally { HidD_FreePreparsedData(p); }
                    }
                } finally { Marshal.FreeHGlobal(detail); }
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
        return found.ToArray();
    }

    public static byte[] QueryFrame(byte command)
    {
        if (command!=3 && command!=4 && command!=16) throw new ArgumentException("Only status queries are allowed.");
        byte[] b = new byte[16]; b[0]=command;
        int sum=8; for (int i=0;i<15;i++) sum+=b[i];
        b[15]=(byte)((0x55-(sum&255))&255);
        return b;
    }

    static byte[] Transfer(SafeFileHandle h, byte[] output, int length, int timeout, Action afterStart=null)
    {
        IntPtr buffer=Marshal.AllocHGlobal(length), ov=Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());
        using (var ev=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.ManualReset)) {
            try {
                Marshal.StructureToPtr(new Overlapped { Event=ev.SafeWaitHandle.DangerousGetHandle() },ov,false);
                if (output!=null) Marshal.Copy(output,0,buffer,length);
                bool done=output==null ? ReadFile(h,buffer,(uint)length,IntPtr.Zero,ov) : WriteFile(h,buffer,(uint)length,IntPtr.Zero,ov);
                int error=Marshal.GetLastWin32Error();
                if (!done && error!=997) throw new Win32Exception(error);
                if (afterStart!=null) {
                    try { afterStart(); }
                    catch { CancelIoEx(h,ov); GetOverlappedResult(h,ov,out uint ignored,true); throw; }
                }
                if (!done && !ev.WaitOne(timeout)) {
                    CancelIoEx(h,ov); GetOverlappedResult(h,ov,out uint ignored,true);
                    throw new TimeoutException("ATK HID query timed out.");
                }
                if (!GetOverlappedResult(h,ov,out uint count,true)) throw new Win32Exception();
                byte[] bytes=new byte[count]; Marshal.Copy(buffer,bytes,0,(int)count); return bytes;
            } finally { Marshal.FreeHGlobal(ov); Marshal.FreeHGlobal(buffer); }
        }
    }

    static byte[] Query(SafeFileHandle h, Device device, byte command, int timeout)
    {
        if (device.OutputLength!=17 || device.InputLength<17) throw new NotSupportedException("Unexpected ATK HID report length.");
        byte[] output=new byte[device.OutputLength]; output[0]=8; QueryFrame(command).CopyTo(output,1);
        var timer=Stopwatch.StartNew(); bool first=true;
        while (timer.ElapsedMilliseconds<timeout) {
            int remaining=Math.Max(1,timeout-(int)timer.ElapsedMilliseconds);
            byte[] report=Transfer(h,null,device.InputLength,remaining,first ? (Action)(()=>Transfer(h,output,output.Length,500)) : null);
            first=false;
            if (report.Length==device.InputLength && report[0]==8 && report[1]==command) {
                byte[] payload=new byte[16]; Array.Copy(report,1,payload,0,16); return payload;
            }
        }
        throw new TimeoutException("No matching ATK response.");
    }

    public static Result Sample(Device device, int timeout=1000)
    {
        if (device==null) throw new ArgumentNullException(nameof(device));
        var r=new Result { ReceiverPresent=true, Device=device,
            Name=ReceiverName(device.Vendor,device.Product,device.ProductName) };
        string mutexName=device.Vendor==0x373b && (device.Product==0x1278 || device.Product==0x10c9)
            ? "Local\\AtkMouseBattery-373B-"+device.Product.ToString("X4")
            : "Local\\AtkMouseBattery-"+device.Vendor.ToString("X4")+"-"+device.Product.ToString("X4")+"-"+device.ContainerId.ToString("N");
        using (var mutex=new Mutex(false,mutexName)) {
            bool held=false;
            try {
                try { held=mutex.WaitOne(0); }
                catch (AbandonedMutexException) { held=true; }
                if (!held) { r.Status="device_busy"; return r; }
                return SampleCore(r,timeout);
            } finally { if (held) mutex.ReleaseMutex(); }
        }
    }

    static Result SampleCore(Result r, int timeout)
    {
        try {
            using (var h=CreateFileW(r.Device.Path,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero)) {
                if (h.IsInvalid) throw new Win32Exception();
                bool verifiedReceiver=r.Device.Vendor==0x373b && (r.Device.Product==0x1278 || r.Device.Product==0x10c9);
                if (verifiedReceiver) {
                    byte[] online=Query(h,r.Device,3,timeout);
                    if (online[1]!=0 || online[5]>1) throw new InvalidDataException("Invalid mouse online response.");
                    if (online[5]==0) { r.Online=false; r.Status="mouse_offline"; return r; }
                    r.Online=true;
                    byte[] identity=Query(h,r.Device,16,timeout);
                    if (identity[1]!=0) throw new InvalidDataException("Mouse identity query failed.");
                    r.Cid=identity[5]; r.Mid=identity[6];
                    r.Name=KnownMouseName(r.Cid.Value,r.Mid.Value) ?? r.Name;
                }
                byte[] battery=Query(h,r.Device,4,timeout);
                if (battery[1]!=0 || battery[5]>100 || (!verifiedReceiver && !ValidChecksum(battery)))
                    throw new InvalidDataException("Invalid mouse battery response.");
                r.Battery=battery[5];
                r.Charging=battery[6]==0 ? false : battery[6]==1 ? (verifiedReceiver || battery[5]<100) : (bool?)null;
                r.Status="ok";
            }
        } catch (Exception e) { r.Status="error"; r.Error=e.Message; r.Battery=null; r.Charging=null; }
        return r;
    }

    public static bool ValidChecksum(byte[] payload)
    {
        if (payload==null || payload.Length!=16) return false;
        int sum=8;
        foreach (byte value in payload) sum+=value;
        return (sum&255)==0x55;
    }

    public static string KnownMouseName(int cid, int mid) =>
        cid==1 && mid==62 ? "ATK F1 V3 ULTIMATE+" :
        cid==1 && mid==85 ? "ATK F1 V3 ULTIMATE" :
        cid==2 && mid==83 ? "ATK A9 PLUS NK" : null;

    public static bool IsReceiverDescriptor(string productName) =>
        !string.IsNullOrWhiteSpace(productName) &&
        (productName.Contains("dongle",StringComparison.OrdinalIgnoreCase) ||
         productName.Contains("receiver",StringComparison.OrdinalIgnoreCase) ||
         productName.Contains("接收器",StringComparison.OrdinalIgnoreCase));

    public static string ReceiverName(int vendor, int product, string productName)
    {
        if (vendor==0x373b && product==0x1278) return "ATK 8K Receiver";
        if (vendor==0x373b && product==0x10c9) return "ATK NANO Receiver";
        if (productName?.Contains("VXE",StringComparison.OrdinalIgnoreCase)==true) return "VXE Receiver";
        if (productName?.Contains("VGN",StringComparison.OrdinalIgnoreCase)==true) return "VGN Receiver";
        if (productName?.Contains("ATK",StringComparison.OrdinalIgnoreCase)==true) return "ATK Receiver";
        return "ATK/VXE/VGN Receiver";
    }

    public static Result[] SampleAll(int timeout=1000)
    {
        var results=new List<Result>();
        foreach (var device in Enumerate()) results.Add(Sample(device,timeout));
        return results.ToArray();
    }

    public static Result SampleByProduct(int product, int timeout=1000)
    {
        if (product!=0x1278 && product!=0x10c9) throw new ArgumentException("Unsupported ATK receiver.");
        foreach (var device in Enumerate())
            if (device.Product==product) return Sample(device,timeout);
        return new Result { Status="receiver_absent", ReceiverPresent=false,
            Name=ReceiverName(0x373b,product,null) };
    }
}
