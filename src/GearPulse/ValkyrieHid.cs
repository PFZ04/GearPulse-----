// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GearPulse;

public static class ValkyrieHid
{
    public const int ReceiverVendor = 0x374a;
    public const int ReceiverProduct = 0xa223;
    public const int WiredProduct = 0xa222;

    public sealed record ReceiverStatus(bool Online, int? Battery);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] data, int length);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] data, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    public static bool IsReceiverStatusInterface(AtkMouseHid.Device device) =>
        device.Vendor == ReceiverVendor && device.Product == ReceiverProduct &&
        string.Equals(device.ProductName, "VKMAG75Max", StringComparison.OrdinalIgnoreCase) &&
        device.UsagePage == 0xffff && device.Usage == 2 && device.FeatureLength == 65;

    public static byte[]? ProbeStatus(AtkMouseHid.Device device)
    {
        if (!IsReceiverStatusInterface(device)) return null;
        using var handle = CreateFileW(device.Path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var request = new byte[65];
        request[1] = 0xf7; // Read-only 2.4G receiver status.
        if (!HidD_SetFeature(handle, request, request.Length)) return null;
        var response = new byte[65];
        return HidD_GetFeature(handle, response, response.Length) ? response : null;
    }

    public static ReceiverStatus? ParseStatus(byte[]? response)
    {
        // Feature report byte 0 is the HID report ID. The receiver's F7 reply
        // starts at byte 1: battery at 2, keyboard-online flag at 4.
        if (response is not { Length: 65 } || response[0] != 0 ||
            response[1] > 1 || response[4] > 1 || response[6] > 1 ||
            response[7] is not (1 or 3)) return null;
        if (response[4] != 0) return new(false, null);
        // The receiver briefly returns zero during a link transition before
        // the keyboard's actual percentage arrives. Do not show it as 0%.
        return response[2] is >= 1 and <= 100 ? new(true, response[2]) : new(true, null);
    }

    public static ReceiverStatus? ReadReceiver(AtkMouseHid.Device device)
    {
        if (!IsReceiverStatusInterface(device)) return null;
        try
        {
            using var mutex = new Mutex(false, "Local\\GearPulse-VKMAG75Max-374A-A223");
            bool held;
            try { held = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) return null;
            try { return ParseStatus(ProbeStatus(device)); }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception error)
        {
            AppLog.Write("Valkyrie receiver status failed", error);
            return null;
        }
    }
}
