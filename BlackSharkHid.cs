// SPDX-License-Identifier: GPL-2.0-or-later
// Protocol reference: OpenRazer PR #2862 (GPL-2.0-or-later).
// This diagnostic implementation is licensed GPL-2.0-or-later.
#nullable disable
using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

public static class BlackSharkHid
{
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid Class; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Attributes { public int Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] struct Caps {
        public ushort Usage, UsagePage, Input, Output, Feature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort Link, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [StructLayout(LayoutKind.Sequential)] struct Overlapped { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attributes a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out Caps caps);
    [DllImport("hid.dll")] static extern bool HidD_FlushQueue(SafeFileHandle h);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint needed, IntPtr device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, uint count, IntPtr bytes, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, uint count, IntPtr bytes, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out uint bytes, bool wait);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);

    public sealed class Device { public string Path {get;set;} public int InputLength {get;set;} public int OutputLength {get;set;} public int UsagePage {get;set;} }
    public sealed class Trace { public string Timestamp {get;set;} public string Direction {get;set;} public string Hex {get;set;} public string Note {get;set;} }
    public sealed class Result {
        public string Timestamp {get;set;} = DateTimeOffset.UtcNow.ToString("o");
        public string Status {get;set;} = "error";
        public bool ReceiverPresent {get;set;}
        public bool? Connected {get;set;}
        public int? Battery {get;set;}
        public bool? Charging {get;set;}
        public int? ChargingRaw {get;set;}
        public string BatteryStatus {get;set;} = "not_queried";
        public string ChargingStatus {get;set;} = "not_queried";
        public string Error {get;set;}
        public Device Device {get;set;}
        public List<Trace> Reports {get;set;} = new List<Trace>();
    }
    public static Device[] Enumerate() {
        HidD_GetHidGuid(out Guid guid);
        IntPtr set=SetupDiGetClassDevsW(ref guid,null,IntPtr.Zero,0x12);
        if(set == new IntPtr(-1)) throw new Win32Exception();
        var found=new List<Device>();
        try {
            for(uint i=0;;i++) {
                var data=new InterfaceData { Size=Marshal.SizeOf<InterfaceData>() };
                if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref data)) {
                    int error=Marshal.GetLastWin32Error();
                    if(error==259) break;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetailW(set,ref data,IntPtr.Zero,0,out uint needed,IntPtr.Zero);
                IntPtr detail=Marshal.AllocHGlobal((int)needed);
                try {
                    Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                    if(!SetupDiGetDeviceInterfaceDetailW(set,ref data,detail,needed,out needed,IntPtr.Zero)) throw new Win32Exception();
                    string path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                    if(!path.ToLowerInvariant().Contains("vid_1532&pid_0555")) continue;
                    using(var h=CreateFileW(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                        if(h.IsInvalid) throw new Win32Exception();
                        var a=new Attributes {Size=Marshal.SizeOf<Attributes>()};
                        if(!HidD_GetAttributes(h,ref a)) throw new Win32Exception();
                        if(a.Vendor!=0x1532 || a.Product!=0x0555) continue;
                        if(!HidD_GetPreparsedData(h,out IntPtr p)) throw new Win32Exception();
                        try {
                            if(HidP_GetCaps(p,out Caps c)!=0x110000) throw new IOException("Cannot read HID capabilities.");
                            if(c.UsagePage==0xff00) found.Add(new Device {Path=path,InputLength=c.Input,OutputLength=c.Output,UsagePage=c.UsagePage});
                        } finally { HidD_FreePreparsedData(p); }
                    }
                } finally { Marshal.FreeHGlobal(detail); }
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
        return found.ToArray();
    }
    // Only these three documented commands can be generated.
    public static byte[] Frame(byte command, bool remoteOn=false) {
        if(command!=0xe1 && command!=0x21 && command!=0x2a) throw new ArgumentException("Command not allowed.");
        byte[] b=new byte[64]; b[0]=2; b[1]=0x80; b[5]=0x50; b[6]=0x41; b[10]=command;
        if(command==0xe1) { b[2]=7; b[7]=14; b[9]=2; b[11]=(byte)(remoteOn?1:0); }
        else { b[2]=8; b[7]=8; b[9]=3; }
        return b;
    }
    public static int? Parse(byte[] b, byte command) {
        if(command!=0x21 && command!=0x2a) throw new ArgumentException("Query not allowed.");
        if(b.Length<16 || b[0]!=2 || b[12]!=command || b[13]!=1 || b[14]!=1) return null;
        if(command==0x21 && b[15]>100) return null;
        return b[15];
    }
    static byte[] Transfer(SafeFileHandle h, byte[] output, int length, int timeout, Action afterStart=null) {
        IntPtr buffer=Marshal.AllocHGlobal(length), ov=Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());
        using(var ev=new EventWaitHandle(false,EventResetMode.ManualReset)) {
            try {
                Marshal.StructureToPtr(new Overlapped {Event=ev.SafeWaitHandle.DangerousGetHandle()},ov,false);
                if(output!=null) Marshal.Copy(output,0,buffer,length);
                bool done=output==null ? ReadFile(h,buffer,(uint)length,IntPtr.Zero,ov) : WriteFile(h,buffer,(uint)length,IntPtr.Zero,ov);
                int error=Marshal.GetLastWin32Error();
                if(!done && error!=997) throw new Win32Exception(error);
                if(afterStart!=null) {
                    try { afterStart(); }
                    catch {
                        CancelIoEx(h,ov); GetOverlappedResult(h,ov,out uint ignored,true);
                        throw;
                    }
                }
                if(!done && !ev.WaitOne(timeout)) {
                    CancelIoEx(h,ov);
                    GetOverlappedResult(h,ov,out uint ignored,true); // Drain before freeing native memory.
                    throw new TimeoutException(output==null?"Input report timed out.":"Output report timed out.");
                }
                if(!GetOverlappedResult(h,ov,out uint count,true)) throw new Win32Exception();
                if(output!=null && count!=length) throw new IOException("Incomplete output report.");
                byte[] bytes=new byte[count]; Marshal.Copy(buffer,bytes,0,(int)count); return bytes;
            } finally { Marshal.FreeHGlobal(ov); Marshal.FreeHGlobal(buffer); }
        }
    }
    static void Log(Result r,string direction,byte[] bytes,string note) {
        r.Reports.Add(new Trace {Timestamp=DateTimeOffset.UtcNow.ToString("o"),Direction=direction,Hex=BitConverter.ToString(bytes),Note=note});
    }
    static void Send(SafeFileHandle h,Result r,byte[] b) {
        Log(r,"out",b,"attempt"); Transfer(h,b,b.Length,1000);
    }
    static int Query(SafeFileHandle h, Result r, byte command, int timeout) {
        // Remove queued reports from older transactions before enabling remote mode.
        if(!HidD_FlushQueue(h)) throw new IOException("Cannot flush HID input queue.");
        try {
            var timer=Stopwatch.StartNew(); bool invalid=false; bool first=true;
            while(timer.ElapsedMilliseconds<timeout) {
                byte[] b;
                // Arm the Windows interrupt reader before the first OUT transfer,
                // including on a freshly plugged-in receiver.
                Action start=first ? (Action)(() => {
                    Send(h,r,Frame(0xe1,true)); Thread.Sleep(35);
                    Send(h,r,Frame(command));
                }) : null;
                first=false;
                try { b=Transfer(h,null,r.Device.InputLength,Math.Max(1,timeout-(int)timer.ElapsedMilliseconds),start); }
                catch(TimeoutException) { break; }
                int? value=Parse(b,command);
                Log(r,"in",b,value.HasValue?"accepted":"ignored");
                if(value.HasValue) return value.Value;
                if(b.Length>12 && b[12]==command) invalid=true;
            }
            if(invalid) throw new InvalidDataException("No valid matching response.");
            throw new TimeoutException("No matching response; headset connection is unknown.");
        } finally {
            try { Send(h,r,Frame(0xe1,false)); Thread.Sleep(35); }
            catch(Exception e) { r.Error=(r.Error??"")+"Remote release failed: "+e.Message+" "; }
        }
    }
    static int QueryWithRetry(SafeFileHandle h, Result r, byte command, int timeout) {
        try { return Query(h,r,command,timeout); }
        catch(TimeoutException) {
            Log(r,"info",Array.Empty<byte>(),"Query 0x"+command.ToString("X2")+" timed out; retrying once on the same open handle.");
            Thread.Sleep(100);
            return Query(h,r,command,timeout);
        }
    }
    public static Result Sample(int timeout) {
        var r=new Result();
        try {
            var devices=Enumerate(); r.ReceiverPresent=devices.Length>0;
            if(devices.Length==0) {r.Status="receiver_absent"; return r;}
            if(devices.Length!=1) {r.Status="ambiguous_device"; return r;}
            r.Device=devices[0];
            if(r.Device.InputLength!=64 || r.Device.OutputLength!=64) {r.Status="unsupported_report_size"; return r;}
            using(var h=CreateFileW(r.Device.Path,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero)) {
                if(h.IsInvalid) throw new Win32Exception();
                try {r.Battery=QueryWithRetry(h,r,0x21,timeout); r.BatteryStatus="ok";}
                catch(Exception e) {r.BatteryStatus=e is TimeoutException?"timeout":e is InvalidDataException?"invalid_response":"error"; r.Error+=e.Message+" ";}
                try {r.ChargingRaw=QueryWithRetry(h,r,0x2a,timeout); r.Charging=r.ChargingRaw!=0; r.ChargingStatus="ok";}
                catch(Exception e) {r.ChargingStatus=e is TimeoutException?"timeout":e is InvalidDataException?"invalid_response":"error"; r.Error+=e.Message+" ";}
            }
            if(r.Battery.HasValue || r.Charging.HasValue) r.Connected=true;
            r.Status=r.Battery.HasValue && r.Charging.HasValue ? (r.Error==null?"ok":"cleanup_error") : r.Connected==true?"partial":"unavailable";
        } catch(Exception e) {r.Status="error"; r.Error+=e.Message;}
        return r;
    }
}
