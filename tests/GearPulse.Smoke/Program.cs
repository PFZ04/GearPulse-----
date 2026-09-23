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
Check(DeviceRoster.Providers.Select(p => p.Id).SequenceEqual(["blackshark-v2-pro", "atk-f1-v3", "atk-a9-plus"]), "device order");
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
    public DeviceState Read()
    {
        Started.SetResult();
        Release.Task.GetAwaiter().GetResult();
        return new(Id, "Slow", "battery", 65, false, true, "ok");
    }
}

sealed class FakeStartup : IStartupTask
{
    public bool? IsEnabled { get; private set; } = true;
    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}
