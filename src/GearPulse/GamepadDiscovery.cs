using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GearPulse;

public static class GamepadDiscovery
{
    public sealed record Peripheral(string Id, Guid ContainerId, string Name, string Connection,
        int? Battery, bool XInputCapable);

    private static readonly Regex GamepadUsage = new(@"HID_DEVICE_UP:0001_U:000[45]\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ControllerName = new(@"\b(gamepad|game controller|joystick|xbox (?:wireless|360|one|series)? ?controller|wireless controller)\b|手柄|游戏控制器",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool Physical(AtkDeviceDiscovery.Node node) =>
        node.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
        node.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);

    private static bool Gamepad(AtkDeviceDiscovery.Node node) =>
        GamepadUsage.IsMatch(node.HardwareIds) ||
        (ControllerName.IsMatch(node.Name) &&
         !node.Name.Contains("system controller", StringComparison.OrdinalIgnoreCase) &&
         !node.Name.Contains("host controller", StringComparison.OrdinalIgnoreCase) &&
         !node.Name.Contains("audio controller", StringComparison.OrdinalIgnoreCase));

    private static int NameScore(string name)
    {
        if (name.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("USB Input Device", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("Nova", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Apex", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("八爪鱼", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("启明星", StringComparison.OrdinalIgnoreCase)) return 10;
        if (name.Contains("Xbox Wireless Controller", StringComparison.OrdinalIgnoreCase)) return 9;
        if (name.Contains("GameSir", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Flydigi", StringComparison.OrdinalIgnoreCase)) return 8;
        return ControllerName.IsMatch(name) ? 5 : 1;
    }

    public static IReadOnlyList<Peripheral> Resolve(IEnumerable<AtkDeviceDiscovery.Node> nodes)
    {
        return nodes.GroupBy(node => node.ContainerId == Guid.Empty
                ? node.InstanceId.ToUpperInvariant() : node.ContainerId.ToString("N"))
            .Select(group =>
            {
                var items = group.ToArray();
                if (!items.Any(Physical) || !items.Any(Gamepad)) return null;
                if (items.Any(n => n.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)))
                {
                    if (items.Any(n => n.Connected == false) && !items.Any(n => n.Connected == true)) return null;
                    // Windows keeps a paired BTHLE parent and its last battery value after power-off.
                    // An active game-input child, rather than the paired parent, proves availability.
                    if (!items.Any(n => n.InstanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) && Gamepad(n) ||
                                        n.DeviceClass.Equals("XnaComposite", StringComparison.OrdinalIgnoreCase))) return null;
                }
                var best = items.OrderByDescending(n => NameScore(n.Name)).First();
                var name = best.Name;
                if (items.Any(n => n.Vendor == 0x3537 && n.Product is 0x1098 or 0x100f) ||
                    name.Contains("Nova 2 Lite", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Nova Lite 2", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("启明星2", StringComparison.OrdinalIgnoreCase)) name = "GameSir Nova Lite 2";
                else if (name.Contains("APEX4", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("APEX 4", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("八爪鱼4", StringComparison.OrdinalIgnoreCase)) name = "Flydigi Apex 4";
                else if (NameScore(name) == 0) name = "Game Controller";
                var bluetooth = items.Any(n => n.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase));
                var wireless = bluetooth || items.Any(n => n.Vendor == 0x3537 && n.Product == 0x1098 ||
                    n.Name.Contains("receiver", StringComparison.OrdinalIgnoreCase) ||
                    n.Name.Contains("dongle", StringComparison.OrdinalIgnoreCase));
                var key = best.ContainerId == Guid.Empty ? best.InstanceId : best.ContainerId.ToString("N");
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
                var battery = items.Where(n => n.Battery is >= 0 and <= 100)
                    .OrderByDescending(n => n.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase))
                    .Select(n => n.Battery).FirstOrDefault();
                return new Peripheral("gamepad-" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant(),
                    best.ContainerId, name, bluetooth ? "bluetooth" : wireless ? "wireless" : "wired", battery,
                    items.Any(n => n.DeviceClass.Equals("XnaComposite", StringComparison.OrdinalIgnoreCase) ||
                        n.Name.Contains("XINPUT compatible input device", StringComparison.OrdinalIgnoreCase)));
            })
            .Where(item => item is not null).Cast<Peripheral>()
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public sealed class GamepadBatteryProvider : IBatteryProvider
{
    public string Id => "windows-gamepads";

    [StructLayout(LayoutKind.Sequential)] private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftThumbX, LeftThumbY, RightThumbX, RightThumbY;
    }
    [StructLayout(LayoutKind.Sequential)] private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }
    [StructLayout(LayoutKind.Sequential)] private struct XInputBattery
    {
        public byte Type, Level;
    }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState(uint userIndex, out XInputState state);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern uint GetBattery(uint userIndex, byte deviceType, out XInputBattery battery);

    public static BatteryLevel? DecodeXInput(byte type, byte level) => type is 2 or 3 && level <= 3
        ? (BatteryLevel)level : null;

    public static IReadOnlyList<string> DiagnosticSlots()
    {
        var lines = new List<string>();
        for (uint slot = 0; slot < 4; slot++)
        {
            if (GetState(slot, out _) != 0) continue;
            var status = GetBattery(slot, 0, out var battery);
            lines.Add($"slot={slot}, batteryCall={status}, type={battery.Type}, level={battery.Level}");
        }
        return lines;
    }

    private static BatteryLevel? UniqueXInputGrade(int physicalCount)
    {
        if (physicalCount != 1) return null;
        var connected = new List<uint>();
        for (uint slot = 0; slot < 4; slot++)
            if (GetState(slot, out _) == 0) connected.Add(slot);
        if (connected.Count != 1 || GetBattery(connected[0], 0, out var battery) != 0) return null;
        return DecodeXInput(battery.Type, battery.Level);
    }

    public IReadOnlyList<DeviceState> Read()
    {
        try
        {
            var physical = GamepadDiscovery.Resolve(AtkDeviceDiscovery.EnumerateNodes(false));
            var grade = physical.Count == 1 && physical[0].XInputCapable && physical[0].Battery is null
                ? UniqueXInputGrade(physical.Count) : null;
            return physical.Select(p =>
            {
                var modelBattery = p.Name == "GameSir Nova Lite 2" && p.Connection == "wireless"
                    ? GameSirNovaLite2Hid.ReadWireless(p.ContainerId) : null;
                var battery = modelBattery ?? p.Battery;
                return new DeviceState(p.Id, p.Name, "gamepad", battery,
                    null, true, battery.HasValue || grade.HasValue ? "ok" : "unavailable",
                    p.Connection, p.ContainerId) { BatteryGrade = battery.HasValue ? null : grade };
            }).ToArray();
        }
        catch (Exception error)
        {
            AppLog.Write("Gamepad discovery failed", error);
            return [];
        }
    }
}
