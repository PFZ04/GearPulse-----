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
    public string StatusText => Status switch
    {
        "mouse_offline" => "鼠标未连接或休眠",
        "device_offline" => "设备未连接或休眠",
        _ when !ReceiverPresent => "接收器未连接",
        _ when Battery is null => Status == "device_busy" ? "设备正在被读取" : "电量暂不可用",
        _ => $"{Battery}% · {(Charging is null ? "充电状态未知" : Charging.Value ? "充电中" : "未充电")}"
    };
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

public sealed class AtkMouseBatteryProvider(int product) : IBatteryProvider
{
    public string Id => product == 0x1278 ? "atk-f1-v3" : "atk-a9-plus";
    private string Name => product == 0x1278 ? "ATK F1 V3 ULTIMATE+" : "ATK A9 PLUS NK";

    public IReadOnlyList<DeviceState> Read()
    {
        if (product is not (0x1278 or 0x10c9)) throw new ArgumentOutOfRangeException(nameof(product));
        try
        {
            var sample = AtkMouseHid.SampleByProduct(product, 1000);
            if (sample.Status == "receiver_absent")
                return [new(Id, Name, "mouse", null, null, false, "hidden")];
            return [new(Id, sample.Name ?? Name, "mouse", sample.Battery, sample.Charging,
                sample.ReceiverPresent, sample.Status)];
        }
        catch (Exception error)
        {
            AppLog.Write($"ATK {product:X4} sample failed", error);
            return [new(Id, Name, "mouse", null, null, true, "error")];
        }
    }
}

public static class DeviceRoster
{
    public static readonly IReadOnlyList<IBatteryProvider> Providers =
    [
        new BlackSharkBatteryProvider(),
        new AtkMouseBatteryProvider(0x1278),
        new AtkMouseBatteryProvider(0x10c9),
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
