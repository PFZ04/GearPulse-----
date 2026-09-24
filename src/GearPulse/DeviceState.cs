namespace GearPulse;

public sealed record DeviceState(
    string Id,
    string Name,
    string Icon,
    int? Battery,
    bool? Charging,
    bool ReceiverPresent,
    string Status)
{
    public bool IsVisible => Status != "hidden";
    public bool IsLow => Battery is >= 0 and <= 20;
    public string StatusText => UiLanguage.StatusText(this);
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
            if (!held) return [new(Id, name, "headset", null, null, true, "device_busy")];
            try
            {
                var sample = BlackSharkHid.Sample(1000);
                return [new(Id, name, "headset", sample.Battery, sample.Charging,
                    sample.ReceiverPresent, sample.Status)];
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception error)
        {
            AppLog.Write("BlackShark sample failed", error);
            return [new(Id, name, "headset", null, null, true, "error")];
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
                if (hid is null || peripheral.Icon != "mouse")
                    return new DeviceState(peripheral.Id, peripheral.Name, peripheral.Icon,
                        null, null, true, "unavailable");
                var sample = AtkMouseHid.Sample(hid, 1000);
                var name = sample.Name is null or "ATK MOUSE" ? peripheral.Name : sample.Name;
                return new DeviceState(peripheral.Id, name, "mouse", sample.Battery,
                    sample.Charging, true, sample.Status);
            }).ToArray();
        }
        catch (Exception error)
        {
            AppLog.Write("ATK device discovery failed", error);
            return [];
        }
    }
}

public static class DeviceRoster
{
    public static readonly IReadOnlyList<IBatteryProvider> Providers =
    [
        new BlackSharkBatteryProvider(),
        new AtkBatteryProvider(),
        new LogitechBatteryProvider()
    ];

    public static IReadOnlyList<DeviceState> Initial() =>
    [
        new("blackshark-v2-pro", "BLACKSHARK V2 PRO", "headset", null, null, true, "pending")
    ];

    public static IReadOnlyList<DeviceState> Visible(IEnumerable<DeviceState> states) =>
        states.Where(s => s.IsVisible).ToArray();
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
