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

if (args.Contains("--atk-integration", StringComparer.OrdinalIgnoreCase))
{
    foreach (var state in new AtkBatteryProvider().Read())
        Console.WriteLine($"{state.Name}: {state.Status}, battery={state.Battery?.ToString() ?? "unknown"}, charging={state.Charging?.ToString() ?? "unknown"}");
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
var localized = new DeviceState("x", "X", "mouse", 85, false, true, "ok");
foreach (var (code, normal, charging, unknown, offline, unavailable) in new[]
{
    (UiLanguage.SimplifiedChinese, "85% · 未充电", "85% · 充电中", "85% · 充电状态未知", "鼠标未连接或休眠", "电量暂不可用"),
    (UiLanguage.English, "85% · Not charging", "85% · Charging", "85% · Charging status unknown", "Mouse disconnected or asleep", "Battery unavailable"),
    (UiLanguage.TraditionalChinese, "85% · 未充電", "85% · 充電中", "85% · 充電狀態未知", "滑鼠未連接或休眠", "電量暫不可用")
})
{
    UiLanguage.Select(code);
    Check(localized.StatusText == normal, $"{code} immediate status refresh");
    Check((localized with { Charging = true }).StatusText == charging, $"{code} charging");
    Check((localized with { Charging = null }).StatusText == unknown, $"{code} charging unknown");
    Check((localized with { Battery = null, Status = "mouse_offline" }).StatusText == offline, $"{code} offline");
    Check((localized with { Battery = null, Status = "error" }).StatusText == unavailable, $"{code} unavailable");
    Check(!string.IsNullOrWhiteSpace(UiLanguage.WindowTitle) && !string.IsNullOrWhiteSpace(UiLanguage.ShowWidget)
        && !string.IsNullOrWhiteSpace(UiLanguage.StartWithWindows) && !string.IsNullOrWhiteSpace(UiLanguage.InstallFirst)
        && !string.IsNullOrWhiteSpace(UiLanguage.LanguageMenu) && !string.IsNullOrWhiteSpace(UiLanguage.Exit)
        && !string.IsNullOrWhiteSpace(UiLanguage.AutostartError), $"{code} interface strings");
}
var settingsPath = Path.Combine(Path.GetTempPath(), "GearPulse-language-test-" + Guid.NewGuid().ToString("N"), "settings.json");
try
{
    UiLanguage.Load(settingsPath);
    Check(UiLanguage.Current == UiLanguage.SimplifiedChinese, "missing language defaults to simplified Chinese");
    Check(WidgetSettings.Load(settingsPath) == new WidgetSettings(), "missing appearance defaults");
    UiLanguage.Select(UiLanguage.TraditionalChinese);
    Check(UiLanguage.Save(settingsPath), "language saves");
    var appearance = new WidgetSettings("silhouette", "large", 37, 68, "DISPLAY2", "top-left");
    Check(appearance.Save(settingsPath), "appearance saves");
    UiLanguage.Select(UiLanguage.English);
    UiLanguage.Load(settingsPath);
    Check(UiLanguage.Current == UiLanguage.TraditionalChinese, "language survives restart");
    Check(WidgetSettings.Load(settingsPath) == appearance, "appearance survives restart");
    UiLanguage.Select(UiLanguage.English);
    Check(UiLanguage.Save(settingsPath), "language changes after appearance");
    Check(WidgetSettings.Load(settingsPath) == appearance, "language save preserves appearance");
    File.WriteAllText(settingsPath, "{\"language\":\"en\",\"appearance\":{\"iconStyle\":\"bad\",\"size\":\"huge\",\"backgroundOpacity\":101,\"contentOpacity\":-4,\"corner\":\"bad\"}}");
    Check(WidgetSettings.Load(settingsPath) == new WidgetSettings("line", "medium", 100, 0), "invalid appearance normalized");
    File.WriteAllText(settingsPath, "{\"appearance\":{\"size\":\"small\",\"backgroundOpacity\":\"invalid\"}}");
    Check(WidgetSettings.Load(settingsPath) == new WidgetSettings(Size: "small"), "invalid setting does not discard valid settings");
    File.WriteAllText(settingsPath, "{\"language\":\"invalid\"}");
    UiLanguage.Load(settingsPath);
    Check(UiLanguage.Current == UiLanguage.SimplifiedChinese, "invalid language defaults");
    Check(WidgetSettings.Load(settingsPath) == new WidgetSettings(), "legacy language-only settings load");
}
finally
{
    Directory.Delete(Path.GetDirectoryName(settingsPath)!, true);
    UiLanguage.Select(UiLanguage.SimplifiedChinese);
}
var area = new System.Drawing.Rectangle(100, 200, 800, 600);
Check(WidgetPlacement.Calculate(area, 270, 88, 20, "top-left").Location == new System.Drawing.Point(120, 220), "top-left placement");
Check(WidgetPlacement.Calculate(area, 270, 88, 20, "top-right").Location == new System.Drawing.Point(610, 220), "top-right placement");
Check(WidgetPlacement.Calculate(area, 270, 88, 20, "bottom-left").Location == new System.Drawing.Point(120, 692), "bottom-left placement");
Check(WidgetPlacement.Calculate(area, 270, 88, 20, "bottom-right").Location == new System.Drawing.Point(610, 692), "bottom-right placement");
Check(new WidgetSettings(Size: "small").Scale == .8 && new WidgetSettings(Size: "large").Scale == 1.25, "size presets");
Check(new DeviceState("x", "X", "mouse", 20, false, true, "ok").IsLow, "20% low threshold");
Check(!new DeviceState("x", "X", "mouse", 21, false, true, "ok").IsLow, "21% normal threshold");
Check(DeviceRoster.Providers.Select(p => p.Id).SequenceEqual(["blackshark-v2-pro", "atk-peripherals", "logitech-lightspeed"]), "device order");
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
Check(AtkMouseHid.ValidChecksum(AtkMouseHid.QueryFrame(4)), "ATK report checksum");
var corruptedAtk = AtkMouseHid.QueryFrame(4);
corruptedAtk[5]++;
Check(!AtkMouseHid.ValidChecksum(corruptedAtk), "ATK bad checksum rejected");
try { AtkMouseHid.QueryFrame(5); throw new Exception("ATK unsafe command allowed"); }
catch (ArgumentException) { checks++; }
var receiverA = Guid.NewGuid();
var receiverB = Guid.NewGuid();
var atkNodes = new AtkDeviceDiscovery.Node[]
{
    new(receiverA, "USB\\VID_373B&PID_1278\\A", 0x373b, 0x1278, "USB Composite Device", "Compx", "USB"),
    new(receiverA, "HID\\VID_373B&PID_1278\\A", 0x373b, 0x1278, "HID-compliant mouse", "Compx", "Mouse"),
    new(receiverB, "USB\\VID_373B&PID_1278\\B", 0x373b, 0x1278, "USB Composite Device", "Compx", "USB"),
    new(Guid.NewGuid(), "USB\\VID_3554&PID_2000\\K", 0x3554, 0x2000, "VXE K75 Receiver", "VXE", "Keyboard"),
    new(Guid.NewGuid(), "USB\\VID_3554&PID_2001\\H", 0x3554, 0x2001, "ATK Headset", "ATK", "Media"),
    new(Guid.NewGuid(), "USB\\VID_3554&PID_2002\\K", 0x3554, 0x2002, "ATK68 V3", "Unknown", "Keyboard"),
    new(Guid.NewGuid(), "USB\\VID_3554&PID_F58A\\R", 0x3554, 0xf58a, "Compx Receiver", "Compx", "Mouse"),
    new(Guid.NewGuid(), "USB\\VID_373B&PID_9999\\X", 0x373b, 0x9999, "Compx receiver", "Compx", "Mouse")
};
var atkResolved = AtkDeviceDiscovery.Resolve(atkNodes);
Check(atkResolved.Count == 6, "ATK multi-interface dedup and unrelated Compx exclusion");
Check(atkResolved.Count(p => p.Name == "ATK F1 V3 ULTIMATE+") == 2, "same receiver model remains distinct");
Check(atkResolved.Select(p => p.Id).Distinct().Count() == 6, "ATK ids unique by container");
Check(atkResolved.Any(p => p.Name == "VXE R1 Pro Max" && p.Icon == "mouse"), "published receiver ID admitted");
Check(atkResolved.Any(p => p.Name == "ATK68 V3" && p.Icon == "keyboard"), "compact ATK model name admitted");
Check(atkResolved.Any(p => p.Name == "VXE K75 Receiver" && p.Icon == "keyboard"), "unknown keyboard uses receiver name");
Check(atkResolved.Any(p => p.Name == "ATK Headset" && p.Icon == "headset"), "headset discovery");
Check(new DeviceState("atk", "ATK Headset", "headset", null, null, true, "unavailable").StatusText == "电量暂不可用", "unknown ATK battery visible");

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
