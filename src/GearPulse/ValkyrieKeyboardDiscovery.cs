// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text.RegularExpressions;

namespace GearPulse;

public static class ValkyrieKeyboardDiscovery
{
    private static readonly Regex BluetoothName = new(@"\b(?:VK|VALKYRIE(?:\s+VK)?)\s*MAG\s*75\s*MAX\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsWiredInterface(AtkMouseHid.Device device) =>
        device.Vendor == ValkyrieHid.ReceiverVendor && device.Product == ValkyrieHid.WiredProduct &&
        !string.IsNullOrWhiteSpace(device.ProductName) && BluetoothName.IsMatch(device.ProductName) &&
        device.UsagePage == 0xffff &&
        device.Usage == 2 && device.FeatureLength == 65;

    public static IReadOnlyList<AtkDeviceDiscovery.Node> ConnectedBluetoothNodes(
        IEnumerable<AtkDeviceDiscovery.Node> nodes)
    {
        return nodes.GroupBy(node => node.ContainerId == Guid.Empty ?
                node.InstanceId.ToUpperInvariant() : node.ContainerId.ToString("N"))
            .Select(group => group.ToArray())
            .Where(group => group.Any(node => node.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) &&
                BluetoothName.IsMatch(node.Name)) &&
                !group.Any(node => node.Connected == false) && group.Any(node =>
                    node.InstanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) &&
                    node.DeviceClass.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(group => group).ToArray();
    }
}

public sealed class ValkyrieBatteryProvider : IBatteryProvider
{
    public string Id => "valkyrie-mag75-max";

    public IReadOnlyList<DeviceState> Read()
    {
        try
        {
            var interfaces = AtkMouseHid.EnumerateVendor(ValkyrieHid.ReceiverVendor);
            var wired = interfaces.FirstOrDefault(ValkyrieKeyboardDiscovery.IsWiredInterface);
            var receiver = interfaces.FirstOrDefault(ValkyrieHid.IsReceiverStatusInterface);
            IReadOnlyList<AtkDeviceDiscovery.Node> bluetooth =
                ValkyrieKeyboardDiscovery.ConnectedBluetoothNodes(AtkDeviceDiscovery.EnumerateNodes(false));
            const string name = "VK MAG 75 MAX";
            const string id = "valkyrie-mag75-max";
            if (wired is not null)
                return [new(id, name, "keyboard", null, null, true, "unavailable", "wired", wired.ContainerId)];
            if (bluetooth.Count > 0)
            {
                var battery = bluetooth.Select(node => node.Battery)
                    .FirstOrDefault(value => value is >= 0 and <= 100);
                return [new(id, name, "keyboard", battery, null, true,
                    battery.HasValue ? "ok" : "unavailable", "bluetooth", bluetooth[0].ContainerId)];
            }
            if (receiver is null) return [];
            var status = ValkyrieHid.ReadReceiver(receiver);
            return [new(id, name, "keyboard", status?.Battery, null, true,
                status?.Online == false ? "device_offline" : status?.Battery is not null ? "ok" : "unavailable",
                "wireless", receiver.ContainerId)];
        }
        catch (Exception error)
        {
            AppLog.Write("Valkyrie keyboard discovery failed", error);
            return [];
        }
    }
}
