namespace GearPulse;

public sealed record DeviceState(
    string Id,
    string Name,
    string Icon,
    int? Battery,
    bool? Charging,
    bool ReceiverPresent,
    string Status,
    string Connection = "",
    Guid ContainerId = default)
{
    public bool HideUnreadableInformation { get; init; }
    public bool IsVisible => Status != "hidden";
    public bool IsLow => Battery is >= 0 and <= 20;
    public string StatusText => UiLanguage.StatusText(this, HideUnreadableInformation);
}

public interface IBatteryProvider
{
    string Id { get; }
    IReadOnlyList<DeviceState> Read();
}

public sealed class BlackSharkBatteryProvider : IBatteryProvider
{
    public string Id => "blackshark-v2-pro";

    public IReadOnlyList<DeviceState> Read()
    {
        const string name = "BLACKSHARK V2 PRO";
        try
        {
            using var mutex = new Mutex(false, @"Local\BlackSharkBatteryDiagnostic-1532-0555");
            bool held;
            try { held = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) return BlackSharkHid.Enumerate().Length == 0 ? [] :
                [new(Id, name, "headset", null, null, true, "device_busy", "wireless")];
            try
            {
                var sample = BlackSharkHid.Sample(1000);
                if (!sample.ReceiverPresent) return [];
                return [new(Id, name, "headset", sample.Battery, sample.Charging,
                    sample.ReceiverPresent, sample.Status, "wireless",
                    AtkDeviceDiscovery.UniqueContainer(0x1532, 0x0555))];
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception error)
        {
            AppLog.Write("BlackShark sample failed", error);
            return [];
        }
    }
}

public sealed class AtkBatteryProvider : IBatteryProvider
{
    public string Id => "atk-peripherals";

    public IReadOnlyList<DeviceState> Read()
    {
        try
        {
            AtkMouseHid.Device[] mouseInterfaces;
            try { mouseInterfaces = AtkMouseHid.Enumerate(); }
            catch (Exception error)
            {
                AppLog.Write("ATK mouse interface discovery failed", error);
                mouseInterfaces = [];
            }
            var hidNodes = mouseInterfaces.Where(device => device.ContainerId != Guid.Empty)
                .Select(device => new AtkDeviceDiscovery.Node(
                device.ContainerId, device.Path, device.Vendor, device.Product,
                device.ProductName ?? "", device.ManufacturerName ?? "", "Mouse"));
            var peripherals = AtkDeviceDiscovery.Resolve(
                AtkDeviceDiscovery.EnumerateNodes().Concat(hidNodes));
            return peripherals.Select(peripheral =>
            {
                var hid = mouseInterfaces.FirstOrDefault(device => device.ContainerId != Guid.Empty &&
                    device.ContainerId == peripheral.ContainerId);
                if (hid is null && peripheral.ContainerId == Guid.Empty)
                    hid = mouseInterfaces.FirstOrDefault(device => device.Vendor == peripheral.Vendor &&
                        device.Product == peripheral.Product);
                return ToState(peripheral, hid is not null && peripheral.Icon == "mouse"
                    ? AtkMouseHid.Sample(hid, 1000) : null);
            }).ToArray();
        }
        catch (Exception error)
        {
            AppLog.Write("ATK device discovery failed", error);
            return [];
        }
    }

    public static DeviceState ToState(AtkDeviceDiscovery.Peripheral peripheral, AtkMouseHid.Result? sample)
    {
        if (sample is null)
            return new(peripheral.Id, peripheral.Name, peripheral.Icon, null, null, true,
                "unavailable", peripheral.Icon == "headset" ? "wireless" : "", peripheral.ContainerId);
        var valid = sample.Status == "ok";
        var confirmedName = sample.Cid is int cid && sample.Mid is int mid
            ? AtkMouseHid.KnownMouseName(cid, mid) : null;
        var name = valid && confirmedName is not null ? confirmedName : peripheral.Name;
        return new(peripheral.Id, name, "mouse", valid ? sample.Battery : null,
            valid ? sample.Charging : null, true, sample.Status, "", peripheral.ContainerId);
    }
}

public static class DeviceRoster
{
    public static readonly IReadOnlyList<IBatteryProvider> Providers =
    [
        new BlackSharkBatteryProvider(),
        new RazerBatteryProvider(),
        new AtkBatteryProvider(),
        new LogitechBatteryProvider(),
        new G522BatteryProvider(),
        new AudioHeadsetProvider()
    ];

    public static IReadOnlyList<DeviceState> Initial() => [];

    public static IReadOnlyList<DeviceState> Visible(IEnumerable<DeviceState> states) =>
        states.Where(s => s.IsVisible).ToArray();

    public static IReadOnlyList<DeviceState> Visible(IEnumerable<DeviceState> states, WidgetSettings settings) =>
        HeadsetRoster.Merge(states).Where(s => s.IsVisible &&
            (s.Icon != "headset" || s.Connection switch
            {
                "wired" => settings.ShowWiredHeadsets,
                "bluetooth" => settings.ShowBluetoothHeadsets,
                _ => true
            })).Select(s => s with { HideUnreadableInformation = settings.HideUnreadableInformation })
            .ToArray();
}

public sealed class G522BatteryProvider : IBatteryProvider
{
    public string Id => "logitech-g522-lightspeed";
    public IReadOnlyList<DeviceState> Read() => G522Hid.Sample();
}

public sealed class RazerBatteryProvider : IBatteryProvider
{
    public string Id => "razer-peripherals";

    public IReadOnlyList<DeviceState> Read()
    {
        try
        {
            var peripherals = RazerDeviceDiscovery.Resolve(AtkDeviceDiscovery.EnumerateNodes(false));
            AtkMouseHid.Device[] interfaces = [];
            if (peripherals.Any(p => p.Connection != "bluetooth" && RazerHid.ForProduct(p.Product) is not null))
            {
                try { interfaces = AtkMouseHid.EnumerateVendor(0x1532); }
                catch (Exception error) { AppLog.Write("Razer HID discovery failed", error); }
            }
            return peripherals.Select(peripheral =>
            {
                var sample = peripheral.Connection == "bluetooth"
                    ? new RazerHid.Result(peripheral.Battery, null)
                    : RazerHid.Sample(peripheral, interfaces);
                return new DeviceState(peripheral.Id, peripheral.Name, peripheral.Icon,
                    sample.Battery, sample.Charging, true,
                    sample.Battery.HasValue ? "ok" : "unavailable", peripheral.Connection,
                    peripheral.ContainerId);
            }).ToArray();
        }
        catch (Exception error)
        {
            AppLog.Write("Razer device discovery failed", error);
            return [];
        }
    }
}

public sealed class AudioHeadsetProvider : IBatteryProvider
{
    public string Id => "windows-audio-headsets";
    public IReadOnlyList<DeviceState> Read() => AudioHeadsetDiscovery.Sample();
}

public sealed class LogitechBatteryProvider : IBatteryProvider
{
    public string Id => "logitech-lightspeed";

    public IReadOnlyList<DeviceState> Read()
    {
        try
        {
            return LogitechHid.Sample();
        }
        catch (Exception error)
        {
            AppLog.Write("Logitech sample failed", error);
            return [];
        }
    }
}
