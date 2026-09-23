using System.Text.Json;
using GearPulse;

if (args.Contains("--startup-integration", StringComparer.OrdinalIgnoreCase))
{
    var task = new WindowsStartupTask();
    var original = task.IsEnabled ?? throw new Exception("GearPulse login task is unavailable.");
    try
    {
        task.SetEnabled(!original);
        if (task.IsEnabled != !original) throw new Exception("Task did not toggle.");
    }
    finally { task.SetEnabled(original); }
    Console.WriteLine("Task Scheduler COM integration passed; original setting restored.");
    return;
}

if (args.Contains("--logitech-integration", StringComparer.OrdinalIgnoreCase))
{
    foreach (var sample in LogitechHid.Sample())
        Console.WriteLine($"Logitech {sample.Name}: status={sample.Status}, battery={sample.Battery?.ToString() ?? "unknown"}, charging={sample.Charging?.ToString() ?? "unknown"}");
    return;
}

if (args.Contains("--logitech-watch", StringComparer.OrdinalIgnoreCase))
{
    for (var attempt = 0; attempt < 15; attempt++)
    {
        foreach (var sample in LogitechHid.Sample())
            Console.WriteLine($"{attempt} {sample.Name}: status={sample.Status}, battery={sample.Battery?.ToString() ?? "unknown"}, charging={sample.Charging?.ToString() ?? "unknown"}");
        await Task.Delay(2000);
    }
    return;
}

if (args.Contains("--devices-integration", StringComparer.OrdinalIgnoreCase))
{
    foreach (var provider in DeviceRoster.Providers)
    {
        foreach (var state in provider.Read())
            Console.WriteLine($"{state.Id}: status={state.Status}, battery={state.Battery?.ToString() ?? "unknown"}, charging={state.Charging?.ToString() ?? "unknown"}");
    }
    return;
}

var checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    checks++;
}

var cases = new (DeviceState state, string text)[]
{
    (new("x", "X", "battery", 85, false, true, "ok"), "85% · 未充电"),
    (new("x", "X", "battery", 85, true, true, "ok"), "85% · 充电中"),
    (new("x", "X", "battery", 0, false, true, "ok"), "0% · 未充电"),
    (new("x", "X", "battery", null, null, true, "error"), "电量暂不可用"),
    (new("x", "X", "battery", null, null, false, "receiver_absent"), "接收器未连接"),
    (new("x", "X", "mouse", null, null, true, "mouse_offline"), "鼠标未连接或休眠"),
    (new("x", "X", "battery", 82, null, true, "ok"), "82% · 充电状态未知")
};
foreach (var item in cases) Check(item.state.StatusText == item.text, item.text);
Check(new DeviceState("x", "X", "mouse", 20, false, true, "ok").IsLow, "20% low threshold");
Check(!new DeviceState("x", "X", "mouse", 21, false, true, "ok").IsLow, "21% normal threshold");
Check(DeviceRoster.Providers.Select(p => p.Id).SequenceEqual(["blackshark-v2-pro", "atk-f1-v3", "atk-a9-plus", "logitech-lightspeed"]), "device order");
Check(DeviceRoster.Visible([
    new("h", "H", "headset", null, null, true, "pending"),
    new("m", "M", "mouse", null, null, false, "hidden")
]).Count == 1, "absent mouse hidden");

var input = new byte[64];
input[0] = 2; input[12] = 0x21; input[13] = 1; input[14] = 1; input[15] = 95;
Check(BlackSharkHid.Parse(input, 0x21) == 95, "battery parser");
input[15] = 101;
Check(BlackSharkHid.Parse(input, 0x21) is null, "battery range");
Check(AtkMouseHid.QueryFrame(3).Length == 16, "ATK query shape");
try { AtkMouseHid.QueryFrame(5); throw new Exception("ATK unsafe command allowed"); }
catch (ArgumentException) { checks++; }

var logitechRoot = LogitechHid.BuildRequest(1, 0, 0, 0x1004);
Check(logitechRoot[0] == 0x11 && logitechRoot[1] == 1 && logitechRoot[2] == 0 && logitechRoot[4] == 0x10 && logitechRoot[5] == 4, "Logitech feature discovery request");
Check(LogitechHid.IsReply([0x11, 1, 0, logitechRoot[3], 6, 0, 0], 1, 0, 0), "Logitech feature reply");
Check(!LogitechHid.IsReply([0x11, 2, 0, logitechRoot[3], 6, 0, 0], 1, 0, 0), "unrelated Logitech slot");
Check(LogitechHid.IsError([0x11, 1, 0xff, 0, logitechRoot[3], 1, 0], 1, 0, 0), "Logitech error reply");
Check(!LogitechHid.IsError([0x11, 2, 0xff, 0, logitechRoot[3], 1, 0], 1, 0, 0), "unrelated Logitech error");
var model = new byte[20];
model[11] = 0x40; model[12] = 0xa9;
var mouseType = new byte[7]; mouseType[4] = 3;
var keyboardType = new byte[7];
Check(LogitechHid.ParseIdentity(model, mouseType, "PRO X 2")?.Icon == "mouse", "Logitech mouse identity");
Check(LogitechHid.ParseIdentity(model, keyboardType, "G915")?.Icon == "keyboard", "Logitech keyboard identity");
Check(LogitechHid.ParseIdentity(new byte[20], mouseType, "No model") is null, "unidentified Logitech hidden");
Check(LogitechHid.ParseIdentity(model, [0, 0, 0, 0, 7], "Receiver") is null, "receiver is not a peripheral");
Check(LogitechHid.MakeId("receiver-a", 1, "40A9") != LogitechHid.MakeId("receiver-b", 1, "40A9"), "same model on two receivers distinct");
Check(LogitechHid.MakeId("receiver-a", 1, "40A9") != LogitechHid.MakeId("receiver-a", 2, "40A9"), "same model on two slots distinct");
Check(LogitechHid.ReportsPercentage(0x1004, [0, 0, 0, 0, 0, 2]), "unified percentage capability");
Check(!LogitechHid.ReportsPercentage(0x1004, [0, 0, 0, 0, 0, 0]), "unified coarse level rejected");
Check(LogitechHid.ReportsPercentage(0x1000, [0, 0, 0, 0, 20, 2]), "legacy percentage capability");
Check(!LogitechHid.ReportsPercentage(0x1000, [0, 0, 0, 0, 4, 2]), "legacy coarse level rejected");
foreach (var (level, status, charging) in new (byte, byte, bool?)[] { (1, 0, false), (100, 1, true), (100, 3, false), (63, 7, null) })
{
    var reply = new byte[20]; reply[4] = level; reply[6] = status;
    var parsed = LogitechHid.ParseBattery(0x1004, reply);
    Check(parsed.Battery == level && parsed.Charging == charging, "Logitech battery state");
}
var badBattery = new byte[20]; badBattery[4] = 101;
Check(LogitechHid.ParseBattery(0x1004, badBattery).Battery is null, "invalid Logitech battery rejected");
Check(LogitechHid.ParseBattery(0x1004, [0x11, 1]).Battery is null, "truncated Logitech battery rejected");
Check(LogitechHid.ParseBattery(0x1004, [0x11, 1, 1, 0, 0, 0, 0]).Battery == 0, "unified zero percent accepted");
Check(LogitechHid.ParseBattery(0x1000, [0x11, 1, 1, 0, 0, 0, 0]).Battery is null, "unknown battery rejected");

foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "blackshark-replies.jsonl")))
{
    using var record = JsonDocument.Parse(line);
    foreach (var report in record.RootElement.GetProperty("reports").EnumerateArray())
    {
        if (report.GetProperty("Direction").GetString() != "in") continue;
        var bytes = report.GetProperty("Hex").GetString()!.Split('-').Select(x => Convert.ToByte(x, 16)).ToArray();
        if (report.GetProperty("Note").GetString() == "accepted")
            Check(BlackSharkHid.Parse(bytes, bytes[12]) is not null, "accepted report rejected");
        else Check(BlackSharkHid.Parse(bytes, 0x21) is null, "unrelated report accepted");
    }
}

var slow = new SlowProvider();
var sampler = new BatterySampler([slow]);
var first = sampler.SampleAsync();
await slow.Started.Task;
Check(sampler.IsSampling, "sampling flag");
Check(await sampler.SampleAsync() is null, "overlap suppressed");
slow.Release.SetResult();
Check((await first)![0].Battery == 65 && !sampler.IsSampling, "sample result");
var multiple = await new BatterySampler([new MultiProvider(), new SlowReadyProvider()]).SampleAsync();
Check(multiple is { Count: 3 } && multiple.Select(x => x.Id).SequenceEqual(["same-1", "same-2", "after"]), "multi-device rows and provider order");
Check(multiple![0].Name == multiple[1].Name, "same-name devices stay separate");

var startup = new FakeStartup();
bool shown = true, exited = false;
var tray = new TrayCommands(startup, value => shown = value, () => exited = true);
tray.SetVisible(false);
Check(!shown && !tray.IsVisible, "tray hide");
tray.SetVisible(true);
Check(shown && tray.IsVisible, "tray show");
tray.SetAutostart(false);
Check(tray.AutostartEnabled == false, "autostart off");
tray.SetAutostart(true);
Check(tray.AutostartEnabled == true, "autostart on");
tray.Exit();
Check(exited, "tray exit");

Console.WriteLine($"Passed {checks} offline checks; no HID commands sent.");

sealed class SlowProvider : IBatteryProvider
{
    public string Id => "slow";
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public IReadOnlyList<DeviceState> Read()
    {
        Started.SetResult();
        Release.Task.GetAwaiter().GetResult();
        return [new(Id, "Slow", "battery", 65, false, true, "ok")];
    }
}

sealed class MultiProvider : IBatteryProvider
{
    public string Id => "multi";
    public IReadOnlyList<DeviceState> Read() =>
    [
        new("same-1", "Same device", "mouse", 70, false, true, "ok"),
        new("same-2", "Same device", "mouse", null, null, true, "device_offline")
    ];
}

sealed class SlowReadyProvider : IBatteryProvider
{
    public string Id => "after";
    public IReadOnlyList<DeviceState> Read() => [new("after", "After", "battery", 80, false, true, "ok")];
}

sealed class FakeStartup : IStartupTask
{
    public bool? IsEnabled { get; private set; } = true;
    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}
