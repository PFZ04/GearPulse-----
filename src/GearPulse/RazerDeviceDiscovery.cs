using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GearPulse;

// Discovery is based on present Windows devices. A dongle name never proves which
// peripheral is paired with it, and a paired but disconnected Bluetooth device is hidden.
public static class RazerDeviceDiscovery
{
    public sealed record Peripheral(string Id, Guid ContainerId, int Product, string Name,
        string Icon, string Connection, int? Battery);

    private static readonly Regex UsbId = new(@"(?:USB|HID)\\VID_1532&PID_([0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Brand = new(@"\bRazer\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly string[] Generic = ["HID-compliant", "USB Input Device", "USB Composite Device",
        "Bluetooth Device", "Bluetooth LE Device", "USB Device"];

    private static bool Bluetooth(AtkDeviceDiscovery.Node node) =>
        node.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);

    private static bool Receiver(string name) =>
        name.Contains("receiver", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("dongle", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("接收器", StringComparison.OrdinalIgnoreCase);

    private static int NameScore(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Generic.Any(x => name.Equals(x, StringComparison.OrdinalIgnoreCase)) ||
            name.Contains("composite device", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("input device", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("microphone", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("麦克风", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("麥克風", StringComparison.OrdinalIgnoreCase))
            return 0;
        return (Brand.IsMatch(name) ? 10 : 1) + (name.Length > 16 ? 2 : 0);
    }

    private static string IconFor(IReadOnlyList<AtkDeviceDiscovery.Node> nodes, string name, int product)
    {
        if (name.Contains("Razer Blade", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Seiren", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Microphone", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Kiyo", StringComparison.OrdinalIgnoreCase)) return "";
        if (product == 0x0555 || nodes.Any(x => x.DeviceClass.Equals("Media", StringComparison.OrdinalIgnoreCase)) ||
            name.Contains("headset", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("headphone", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("blackshark", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("barracuda", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("kraken", StringComparison.OrdinalIgnoreCase)) return "headset";
        if (name.Contains("blackwidow", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("deathstalker", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("huntsman", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ornata", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("cynosa", StringComparison.OrdinalIgnoreCase)) return "keyboard";
        if (nodes.Any(x => x.DeviceClass.Equals("Mouse", StringComparison.OrdinalIgnoreCase)) ||
            name.Contains("mouse", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("viper", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("deathadder", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("basilisk", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("naga", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("cobra", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("orochi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("atheris", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("mamba", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("pro click", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("hyperpolling", StringComparison.OrdinalIgnoreCase)) return "mouse";
        if (nodes.Any(x => x.DeviceClass.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)) ||
            name.Contains("keyboard", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("blackwidow", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("deathstalker", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("huntsman", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ornata", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("cynosa", StringComparison.OrdinalIgnoreCase)) return "keyboard";
        return "";
    }

    public static IReadOnlyList<Peripheral> Resolve(IEnumerable<AtkDeviceDiscovery.Node> nodes)
    {
        return nodes.GroupBy(n => n.ContainerId == Guid.Empty ? n.InstanceId.ToUpperInvariant() :
                n.ContainerId.ToString("N"))
            .Select(group =>
            {
                var items = group.ToArray();
                if (!items.Any(n => n.Vendor == 0x1532 || Bluetooth(n) &&
                    (Brand.IsMatch(n.Name) || Brand.IsMatch(n.Manufacturer)))) return null;
                var bt = items.Any(Bluetooth) && !items.Any(n =>
                    n.InstanceId.StartsWith("USB\\VID_1532", StringComparison.OrdinalIgnoreCase));
                if (bt && !items.Any(n => n.Connected == true)) return null;
                var best = items.OrderByDescending(n => NameScore(n.Name)).First();
                var products = items.Where(n => n.Vendor == 0x1532).Select(n => n.Product).ToArray();
                var product = bt ? 0 : products.Contains(0x00e6) ? 0x00e6 :
                    products.Contains(0x00e5) ? 0x00e5 : products.FirstOrDefault(p => p != 0);
                var name = product is 0x00e5 or 0x00e6 ? "Razer Viper V4 Pro" :
                    product == 0x0555 ? "Razer BlackShark V2 Pro" :
                    NameScore(best.Name) > 0 ? best.Name :
                    bt ? "Razer Bluetooth Device" : $"Razer Device 1532:{product:X4}";
                var icon = IconFor(items, name, product);
                if (icon.Length == 0) return null;
                var key = best.ContainerId == Guid.Empty ? best.InstanceId : best.ContainerId.ToString("N");
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
                var battery = bt ? items.Select(n => n.Battery).FirstOrDefault(value => value is >= 0 and <= 100) : null;
                return new Peripheral($"razer-{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}",
                    best.ContainerId, product, name, icon, bt ? "bluetooth" :
                    (product is 0x0555 or 0x00e6) || Receiver(name) ||
                    name.Contains("wireless", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("HyperSpeed", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("2.4", StringComparison.OrdinalIgnoreCase) ? "wireless" : "wired",
                    battery);
            })
            .Where(p => p is not null).Cast<Peripheral>()
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
