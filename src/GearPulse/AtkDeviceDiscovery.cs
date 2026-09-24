using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GearPulse;

// PnP discovery is read-only. A container represents one physical USB device,
// even when Windows exposes separate mouse, keyboard and vendor HID interfaces.
public static class AtkDeviceDiscovery
{
    public sealed record Node(Guid ContainerId, string InstanceId, int Vendor, int Product,
        string Name, string Manufacturer, string DeviceClass);
    public sealed record Peripheral(string Id, Guid ContainerId, int Vendor, int Product,
        string Name, string Icon);

    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfoData
    {
        public int Size;
        public Guid Class;
        public uint DevInst;
        public IntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfoData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref DeviceInfoData data,
        StringBuilder id, uint length, out uint needed);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref DeviceInfoData data,
        uint property, out uint type, byte[] buffer, uint length, out uint needed);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref DeviceInfoData data,
        ref PropertyKey key, out uint type, byte[] buffer, uint length, out uint needed, uint flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    private static readonly Regex UsbId = new(@"(?:USB|HID)\\VID_([0-9A-F]{4})&PID_([0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Brand = new(@"\b(?:ATK|VXE|VGN)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Guid ContainerFormat = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c");
    // Published receiver and cable IDs for the shared Compx mouse families.
    // The silicon is shared by unrelated brands, so never admit a whole VID.
    private static readonly HashSet<(int Vendor, int Product)> KnownMouseIds =
    [
        (0x373b, 0x1278), (0x373b, 0x10c9),
        (0x373b, 0x1031), (0x373b, 0x102e),
        (0x373b, 0x11d9), (0x373b, 0x11b6),
        (0x373b, 0x104d), (0x373b, 0x103f),
        (0x373b, 0x1040), (0x373b, 0x104c),
        (0x3554, 0xf58a), (0x3554, 0xf58c),
        (0x3554, 0xf58e), (0x3554, 0xf58f),
        (0x3554, 0xf503), (0x3554, 0xf502),
        (0x3554, 0xfb3e), (0x3554, 0xfb3d),
        (0x373b, 0x1155), (0x373b, 0x124f), (0x373b, 0x1154)
    ];

    private static string RegistryString(IntPtr set, ref DeviceInfoData info, uint property)
    {
        var buffer = new byte[1024];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref info, property, out _, buffer,
            (uint)buffer.Length, out var used)) return "";
        return Encoding.Unicode.GetString(buffer, 0, Math.Min((int)used, buffer.Length))
            .Split('\0', 2)[0].Trim();
    }

    public static IReadOnlyList<Node> EnumerateNodes(bool usbOnly = true)
    {
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, 0x06); // PRESENT | ALLCLASSES
        if (set == new IntPtr(-1)) throw new Win32Exception();
        var nodes = new List<Node>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new DeviceInfoData { Size = Marshal.SizeOf<DeviceInfoData>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                var id = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, (uint)id.Capacity, out _)) continue;
                var match = UsbId.Match(id.ToString());
                if (usbOnly && !match.Success) continue;
                var vendor = match.Success ? Convert.ToInt32(match.Groups[1].Value, 16) : 0;
                var product = match.Success ? Convert.ToInt32(match.Groups[2].Value, 16) : 0;
                var key = new PropertyKey { Format = ContainerFormat, Id = 2 };
                var bytes = new byte[16];
                var container = SetupDiGetDevicePropertyW(set, ref info, ref key, out var type,
                    bytes, 16, out _, 0) && type == 0x0d ? new Guid(bytes) : Guid.Empty;
                var name = RegistryString(set, ref info, 12); // SPDRP_FRIENDLYNAME
                if (name.Length == 0) name = RegistryString(set, ref info, 0); // SPDRP_DEVICEDESC
                nodes.Add(new Node(container, id.ToString(), vendor, product, name,
                    RegistryString(set, ref info, 11), RegistryString(set, ref info, 7)));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return nodes;
    }

    public static Guid UniqueContainer(int vendor, int product)
    {
        try
        {
            var containers = EnumerateNodes().Where(x => x.Vendor == vendor && x.Product == product &&
                x.ContainerId != Guid.Empty).Select(x => x.ContainerId).Distinct().Take(2).ToArray();
            return containers.Length == 1 ? containers[0] : Guid.Empty;
        }
        catch { return Guid.Empty; }
    }

    public static IReadOnlyList<Peripheral> Resolve(IEnumerable<Node> nodes)
    {
        return nodes.GroupBy(node => node.ContainerId == Guid.Empty
                ? node.InstanceId.ToUpperInvariant() : node.ContainerId.ToString("N"))
            .Select(group =>
            {
                var items = group.ToArray();
                var branded = items.Any(n => Brand.IsMatch(n.Name) || Brand.IsMatch(n.Manufacturer));
                var known = items.Select(n => (n.Vendor, n.Product))
                    .FirstOrDefault(id => KnownMouseIds.Contains(id));
                var hasKnown = KnownMouseIds.Contains(known);
                if (!branded && !hasKnown) return null;
                // Receiver labels describe the dongle, not the paired mouse.
                var best = items.OrderByDescending(n => NameScore(n.Name)).First();
                var sharedReceiver = known is (0x373b, 0x1278) or (0x373b, 0x10c9);
                var mouseLike = hasKnown || items.Any(n => n.DeviceClass.Equals("Mouse", StringComparison.OrdinalIgnoreCase) ||
                    n.Name.Contains("A9 Mini", StringComparison.OrdinalIgnoreCase) ||
                    n.Name.Contains("mouse", StringComparison.OrdinalIgnoreCase) && Brand.IsMatch(n.Name));
                var receiver = mouseLike && (sharedReceiver || items.Any(n => AtkMouseHid.IsReceiverDescriptor(n.Name)));
                var name = receiver ? AtkMouseHid.ReceiverName(best.Vendor, best.Product, best.Name) :
                    NameScore(best.Name) > 0 ? best.Name :
                    $"ATK/VXE/VGN Mouse {best.Vendor:X4}:{best.Product:X4}";
                var icon = mouseLike ? "mouse" :
                    items.Any(n => n.DeviceClass.Equals("Media", StringComparison.OrdinalIgnoreCase) ||
                    n.Name.Contains("headset", StringComparison.OrdinalIgnoreCase) ||
                    n.Name.Contains("耳机", StringComparison.OrdinalIgnoreCase)) ? "headset" :
                    items.Any(n => n.DeviceClass.Equals("Keyboard", StringComparison.OrdinalIgnoreCase) ||
                    n.Name.Contains("keyboard", StringComparison.OrdinalIgnoreCase)) ? "keyboard" : "mouse";
                var key = best.ContainerId == Guid.Empty ? best.InstanceId : best.ContainerId.ToString("N");
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
                return new Peripheral($"atk-{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}",
                    best.ContainerId, best.Vendor, best.Product, name, icon);
            })
            .Where(item => item is not null).Cast<Peripheral>()
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int NameScore(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("composite", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("HID-compliant", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("USB Input Device", StringComparison.OrdinalIgnoreCase)) return 0;
        return (Brand.IsMatch(name) ? 4 : 1) + (name.Contains("receiver", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("dongle", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("接收器", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
    }
}
