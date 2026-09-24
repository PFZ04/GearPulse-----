// SPDX-License-Identifier: GPL-2.0-or-later
// Read-only Razer Chroma battery queries. PID/TID values follow OpenRazer's
// razermouse_driver.c and razerkbd_driver.c charge_level/charge_status handlers.
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GearPulse;

public static class RazerHid
{
    public sealed record Protocol(byte Transaction, bool Charging, string Icon);
    public sealed record Result(int? Battery, bool? Charging);

    private static readonly Dictionary<int, Protocol> Protocols = new();

    static RazerHid()
    {
        Add("mouse", 0x3f, true, [0x0059, 0x005a, 0x0072, 0x0073, 0x007c, 0x007d]);
        Add("mouse", 0x1f, true, [0x006f, 0x0070, 0x0077, 0x0080, 0x0086, 0x0088,
            0x008f, 0x0090, 0x009e, 0x009f, 0x00a5, 0x00a6, 0x00a7, 0x00a8,
            0x00aa, 0x00ab, 0x00af, 0x00b0, 0x00b6, 0x00b7, 0x00be, 0x00bf,
            0x00c0, 0x00c1, 0x00c2, 0x00c3, 0x00c4, 0x00c5, 0x00c7, 0x00c8,
            0x00cc, 0x00cd, 0x00d0, 0x00d1, 0x00d6, 0x00d7]);
        // Viper V4 Pro uses its non-pointer MI_03/MI_04 feature interface.
        // The published hardware reader confirms battery, but not charging status.
        Add("mouse", 0x1f, false, [0x00e5, 0x00e6]);
        Add("mouse", 0x1f, false, [0x0062, 0x0094, 0x009a, 0x009c, 0x00b4,
            0x00b8, 0x00b9, 0x00d3, 0x00d4]);
        Add("mouse", 0xff, true, [0x001f, 0x0024, 0x0025, 0x0032, 0x003e,
            0x003f, 0x0044, 0x0045, 0x007a, 0x007b]);
        Add("mouse", 0xff, false, [0x0083]);
        Add("keyboard", 0x1f, true, [0x0258, 0x0292, 0x0298, 0x02b9, 0x02d7]);
        Add("keyboard", 0x9f, true, [0x025c, 0x0271, 0x0290, 0x0296, 0x02ba, 0x02d5]);
        Add("keyboard", 0x3f, true, [0x025a]);
    }

    private static void Add(string icon, byte transaction, bool charging, int[] products)
    {
        foreach (var product in products) Protocols.Add(product, new(transaction, charging, icon));
    }

    public static Protocol? ForProduct(int product) => Protocols.GetValueOrDefault(product);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] data, int length);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] data, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    public static byte[] Frame(byte transaction, byte command)
    {
        if (command is not (0x80 or 0x84)) throw new ArgumentException("Battery queries only.", nameof(command));
        var report = new byte[91];
        report[2] = transaction;
        report[6] = 2;
        report[7] = 7;
        report[8] = command;
        report[89] = Checksum(report);
        return report;
    }

    private static byte Checksum(ReadOnlySpan<byte> report)
    {
        byte value = 0;
        for (var i = 3; i <= 88; i++) value ^= report[i];
        return value;
    }

    public static int? Parse(byte[] response, byte transaction, byte command)
    {
        if (response.Length < 91 || response[0] != 0 || response[1] != 2 ||
            response[2] != transaction || response[5] != 0 || response[6] != 2 ||
            response[7] != 7 || response[8] != command || response[89] != Checksum(response))
            return null;
        var raw = response[10];
        // A sleeping dongle can return zero with an otherwise successful frame.
        if (command == 0x80) return raw == 0 ? null : (int)Math.Round(raw * 100.0 / 255);
        return raw is 0 or 1 ? raw : null;
    }

    public static Result FromReplies(byte[]? batteryReply, byte[]? chargingReply,
        byte transaction, bool supportsCharging)
    {
        var battery = batteryReply is null ? null : Parse(batteryReply, transaction, 0x80);
        var charging = battery.HasValue && supportsCharging && chargingReply is not null
            ? Parse(chargingReply, transaction, 0x84) is int value ? value == 1 : (bool?)null
            : null;
        return new(battery, charging);
    }

    private static bool PathPart(string path, string part) => path.Split('#', '&')
        .Any(value => value.Equals(part, StringComparison.OrdinalIgnoreCase));

    public static string InterfaceLabel(string path) =>
        new[] { "mi_00", "mi_01", "mi_02", "mi_03", "mi_04" }
            .FirstOrDefault(part => PathPart(path, part))?.ToUpperInvariant() ?? "unknown";

    public static bool IsBatteryInterface(AtkMouseHid.Device device,
        RazerDeviceDiscovery.Peripheral peripheral)
    {
        var protocol = ForProduct(peripheral.Product);
        if (protocol is null || protocol.Icon != peripheral.Icon || device.Vendor != 0x1532 ||
            device.Product != peripheral.Product || device.FeatureLength < 91 ||
            (peripheral.ContainerId != Guid.Empty && device.ContainerId != peripheral.ContainerId))
            return false;
        if (peripheral.Product is 0x00e5 or 0x00e6)
            return (PathPart(device.Path, "mi_03") || PathPart(device.Path, "mi_04")) &&
                !device.Path.Split('#', '&').Any(part => part.StartsWith("col", StringComparison.OrdinalIgnoreCase));
        return protocol.Icon == "mouse" ? device.UsagePage == 1 && device.Usage == 2 :
            device.UsagePage is 1 or 0xff00;
    }

    private static byte[]? Query(SafeFileHandle handle, byte transaction, byte command)
    {
        var request = Frame(transaction, command);
        foreach (var delay in new[] { 80, 150, 250 })
        {
            if (!HidD_SetFeature(handle, request, request.Length)) return null;
            Thread.Sleep(delay);
            var response = new byte[91];
            if (!HidD_GetFeature(handle, response, response.Length)) return null;
            if (response[1] == 1) continue;
            return response;
        }
        return null;
    }

    public static Result Sample(RazerDeviceDiscovery.Peripheral peripheral,
        IReadOnlyList<AtkMouseHid.Device> interfaces)
    {
        var protocol = ForProduct(peripheral.Product);
        if (protocol is null || protocol.Icon != peripheral.Icon) return new(null, null);
        var candidates = interfaces.Where(device => IsBatteryInterface(device, peripheral))
            .OrderBy(device => InterfaceLabel(device.Path) == "MI_03" ? 0 : 1).ToArray();
        if (peripheral.ContainerId == Guid.Empty && candidates.Select(x => x.Path).Distinct().Count() > 1)
            return new(null, null);
        foreach (var device in candidates)
        {
            try
            {
                // V4's vendor feature interface is opened read/write by the
                // hardware-verified reader; the only commands sent remain 0x80.
                var access = peripheral.Product is 0x00e5 or 0x00e6 ? 0xc0000000u : 0u;
                using var handle = CreateFileW(device.Path, access, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (handle.IsInvalid) continue;
                var battery = Query(handle, protocol.Transaction, 0x80);
                if (battery is null || Parse(battery, protocol.Transaction, 0x80) is null) continue;
                var charging = protocol.Charging ? Query(handle, protocol.Transaction, 0x84) : null;
                return FromReplies(battery, charging, protocol.Transaction, protocol.Charging);
            }
            catch (Exception error) { AppLog.Write("Razer HID battery query failed", error); }
        }
        return new(null, null);
    }
}
