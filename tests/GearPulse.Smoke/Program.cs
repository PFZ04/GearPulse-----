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

if (args.Contains("--razer-integration", StringComparer.OrdinalIgnoreCase))
{
    var discovered = RazerDeviceDiscovery.Resolve(AtkDeviceDiscovery.EnumerateNodes(false));
    foreach (var device in AtkMouseHid.EnumerateVendor(0x1532))
    {
        var matched = discovered.Any(peripheral => RazerHid.IsBatteryInterface(device, peripheral));
        Console.WriteLine($"Razer HID 1532:{device.Product:X4}: interface={RazerHid.InterfaceLabel(device.Path)}, " +
            $"usage={device.UsagePage:X4}:{device.Usage:X4}, reports=input {device.InputLength}, " +
            $"output {device.OutputLength}, feature {device.FeatureLength}, batteryInterface={matched}");
    }
    foreach (var sample in new RazerBatteryProvider().Read())
        Console.WriteLine($"{sample.Name}: icon={sample.Icon}, connection={sample.Connection}, " +
            $"status={sample.Status}, battery={sample.Battery?.ToString() ?? "unknown"}, " +
            $"charging={sample.Charging?.ToString() ?? "unknown"}");
    return;
}

if (args.Contains("--g522-integration", StringComparer.OrdinalIgnoreCase))
{
    foreach (var state in G522Hid.Sample())
        Console.WriteLine($"{state.Name}: status={state.Status}, battery={state.Battery?.ToString() ?? "unknown"}, charging={state.Charging?.ToString() ?? "unknown"}");
    return;
}

if (args.Contains("--g522-watch", StringComparer.OrdinalIgnoreCase))
{
    for (var attempt = 0; attempt < 15; attempt++)
    {
        var samples = G522Hid.Sample();
        if (samples.Count == 0) Console.WriteLine($"{attempt}: G522 LIGHTSPEED receiver absent");
        foreach (var state in samples)
            Console.WriteLine($"{attempt} {state.Name}: status={state.Status}, battery={state.Battery?.ToString() ?? "unknown"}, charging={state.Charging?.ToString() ?? "unknown"}");
        await Task.Delay(2000);
    }
    return;
}

if (args.Contains("--audio-integration", StringComparer.OrdinalIgnoreCase))
{
    foreach (var endpoint in AudioHeadsetDiscovery.Enumerate())
        Console.WriteLine($"{endpoint.Name}: formFactor={endpoint.FormFactor}, connection={endpoint.Connection}");
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

if (args.Contains("--atk-diagnostics", StringComparer.OrdinalIgnoreCase))
{
    var peripherals = AtkDeviceDiscovery.Resolve(AtkDeviceDiscovery.EnumerateNodes());
    foreach (var device in AtkMouseHid.EnumerateAll())
    {
        if (!peripherals.Any(p => device.ContainerId != Guid.Empty && p.ContainerId == device.ContainerId ||
            device.ContainerId == Guid.Empty && p.Vendor == device.Vendor && p.Product == device.Product)) continue;
        var sample = AtkMouseHid.IsBatteryInterface(device) ? AtkMouseHid.Sample(device, 1000) : null;
        Console.WriteLine($"{device.Vendor:X4}:{device.Product:X4} {device.ProductName} " +
            $"usage={device.UsagePage:X4}:{device.Usage:X4} reports={device.InputLength}/{device.OutputLength}/{device.FeatureLength} " +
            $"status={sample?.Status ?? "not_queried"} cid={sample?.Cid?.ToString() ?? "unknown"} " +
            $"mid={sample?.Mid?.ToString() ?? "unknown"} battery={sample?.Battery?.ToString() ?? "unknown"} " +
            $"charging={sample?.Charging?.ToString() ?? "unknown"}");
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
    var hide = localized with { HideUnreadableInformation = true };
    Check((hide with { Charging = null }).StatusText == "85%", $"{code} hide unknown charging");
    Check((hide with { Battery = null, Status = "error" }).StatusText == "", $"{code} hide unknown battery");
    Check((hide with { Battery = null, Status = "mouse_offline" }).StatusText == "", $"{code} hide offline detail");
    Check(hide.StatusText == normal && (hide with { Charging = true }).StatusText == charging,
        $"{code} preserve known charging state");
    Check((new DeviceState("empty", "GearPulse", "battery", null, null, true, "empty")
        { HideUnreadableInformation = true }).StatusText.Length > 0, $"{code} preserve empty card message");
    Check(!string.IsNullOrWhiteSpace(UiLanguage.WindowTitle) && !string.IsNullOrWhiteSpace(UiLanguage.ShowWidget)
        && !string.IsNullOrWhiteSpace(UiLanguage.StartWithWindows) && !string.IsNullOrWhiteSpace(UiLanguage.InstallFirst)
        && !string.IsNullOrWhiteSpace(UiLanguage.LanguageMenu) && !string.IsNullOrWhiteSpace(UiLanguage.Exit)
        && !string.IsNullOrWhiteSpace(UiLanguage.AutostartError)
        && !string.IsNullOrWhiteSpace(UiLanguage.HideUnreadableInformation), $"{code} interface strings");
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
    var headsetSettings = appearance with { ShowWiredHeadsets = false, ShowBluetoothHeadsets = false };
    Check(headsetSettings.Save(settingsPath), "headset visibility settings save");
    Check(WidgetSettings.Load(settingsPath) == headsetSettings, "headset visibility settings reload");
    var hiddenInformation = appearance with { HideUnreadableInformation = true };
    Check(hiddenInformation.Save(settingsPath), "hidden information setting saves");
    Check(WidgetSettings.Load(settingsPath) == hiddenInformation, "hidden information setting reloads");
    Check(appearance.Save(settingsPath), "restore appearance settings");
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
Check(DeviceRoster.Providers.Select(p => p.Id).SequenceEqual(["blackshark-v2-pro", "razer-peripherals", "atk-peripherals", "logitech-lightspeed", "logitech-g522-lightspeed", "windows-audio-headsets"]), "device order");
Check(DeviceRoster.Initial().Count == 0, "no fixed BlackShark placeholder");
Check(AudioHeadsetDiscovery.IsHeadset(3, "Headphones (Realtek Audio)"), "headphone form factor");
Check(AudioHeadsetDiscovery.IsHeadset(5, "Generic Audio"), "headset form factor");
Check(AudioHeadsetDiscovery.IsHeadset(1, "USB Gaming Headset"), "named headset fallback");
Check(!AudioHeadsetDiscovery.IsHeadset(1, "Desktop Speakers"), "speakers excluded");
var audioContainer = Guid.NewGuid();
var audioNodes = new[]
{
    new AtkDeviceDiscovery.Node(audioContainer, "BTHENUM\\A", 0, 0, "Bluetooth Headset", "", "Media")
};
Check(AudioHeadsetDiscovery.ConnectionFor(audioContainer, "Headphones", audioNodes) == "bluetooth", "Bluetooth transport");
Check(AudioHeadsetDiscovery.ConnectionFor(Guid.Empty, "LIGHTSPEED Headset", []) == "wireless", "receiver transport");
Check(AudioHeadsetDiscovery.ConnectionFor(Guid.Empty, "Headphones", []) == "wired", "wired transport");
var headsets = HeadsetRoster.Merge([
    new("g522", "Logitech G522 LIGHTSPEED", "headset", 72, false, true, "ok", "wireless"),
    new("audio-a", "Headphones (G522 LIGHTSPEED)", "headset", null, null, true, "unavailable", "wired"),
    new("audio-b", "G522 LIGHTSPEED Headset", "headset", null, null, true, "unavailable", "wired"),
    new("other", "Headphones (Realtek)", "headset", null, null, true, "unavailable", "wired")
]);
Check(headsets.Count == 2 && headsets.Any(x => x.Id == "g522" && x.Battery == 72), "G522 audio duplicate merged");
Check(HeadsetRoster.Merge([
    new("g522", "Logitech G522 LIGHTSPEED", "headset", 72, false, true, "ok", "wireless"),
    new("audio-g522", "Headphones (G522 LIGHTSPEED)", "headset", null, null, true, "unavailable", "wireless", Guid.NewGuid())
]).Count == 1, "G522 model fallback joins audio endpoint with missing HID container");
var sharedContainer = Guid.NewGuid();
Check(HeadsetRoster.Merge([
    new("known", "BLACKSHARK V2 PRO", "headset", 61, false, true, "ok", "wireless", sharedContainer),
    new("audio", "Headphones (Razer Audio)", "headset", null, null, true, "unavailable", "wired", sharedContainer)
]).Count == 1, "different audio name merges by physical container");
Check(HeadsetRoster.Merge([
    new("audio-1", "Headphones", "headset", null, null, true, "unavailable"),
    new("audio-2", "Headphones", "headset", null, null, true, "unavailable")
]).Count == 2, "same-name physical headsets stay distinct without shared identity");
var razerContainer = Guid.NewGuid();
var razerBluetoothContainer = Guid.NewGuid();
var razerNodes = new AtkDeviceDiscovery.Node[]
{
    new(razerContainer, "USB\\VID_1532&PID_00C1\\A", 0x1532, 0x00c1,
        "Razer Viper V3 Pro", "Razer", "Mouse"),
    new(razerContainer, "HID\\VID_1532&PID_00C1\\B", 0x1532, 0x00c1,
        "HID-compliant mouse", "Razer", "HIDClass"),
    new(Guid.NewGuid(), "USB\\VID_1532&PID_00B3\\D", 0x1532, 0x00b3,
        "Razer HyperPolling Wireless Dongle", "Razer", "Mouse"),
    new(razerBluetoothContainer, "BTHLEDEVICE\\RAZER-A", 0, 0,
        "Razer Orochi V2", "Razer", "Mouse", null, true),
    new(razerBluetoothContainer, "BTHLE\\BATTERY-A", 0, 0,
        "Bluetooth Battery", "", "Bluetooth", 67, true),
    new(Guid.NewGuid(), "BTHLEDEVICE\\RAZER-B", 0, 0,
        "Razer Viper", "Razer", "Mouse", 91, false),
    new(Guid.NewGuid(), "USB\\VID_1532&PID_0555\\H", 0x1532, 0x0555,
        "Razer BlackShark V2 Pro", "Razer", "Media")
};
var razerResolved = RazerDeviceDiscovery.Resolve(razerNodes);
Check(razerResolved.Count == 4, "Razer present devices and multi-interface grouping");
Check(razerResolved.Single(x => x.Product == 0x00c1).Icon == "mouse", "Razer mouse type");
Check(razerResolved.Single(x => x.Product == 0x00b3).Name.Contains("Dongle"), "unidentified paired mouse keeps receiver name");
Check(razerResolved.Single(x => x.Connection == "bluetooth").Battery == 67, "connected Bluetooth battery");
Check(razerResolved.Single(x => x.Product == 0x0555).Icon == "headset", "Razer headset type");
foreach (var (product, connection) in new[] { (0x00e5, "wired"), (0x00e6, "wireless") })
{
    var container = Guid.NewGuid();
    var v4 = RazerDeviceDiscovery.Resolve([
        new(container, $"USB\\VID_1532&PID_{product:X4}\\A", 0x1532, product,
            "Razer control device", "Razer", "Mouse"),
        new(container, $"HID\\VID_1532&PID_{product:X4}&MI_03\\B", 0x1532, product,
            "HID-compliant mouse", "Razer", "HIDClass")
    ]);
    Check(v4.Count == 1 && v4[0].Name == "Razer Viper V4 Pro" &&
        v4[0].Connection == connection && v4[0].Icon == "mouse",
        $"Viper V4 Pro {connection} name and interface grouping");
    AtkMouseHid.Device Interface(string path, int feature = 91) => new()
    {
        Path = $@"\\?\hid#vid_1532&pid_{product:x4}&{path}#device#{{guid}}",
        Vendor = 0x1532, Product = product, ContainerId = container,
        FeatureLength = feature, UsagePage = 1, Usage = 2
    };
    Check(RazerHid.IsBatteryInterface(Interface("mi_03"), v4[0]) &&
        RazerHid.IsBatteryInterface(Interface("mi_04"), v4[0]) &&
        !RazerHid.IsBatteryInterface(Interface("mi_00"), v4[0]) &&
        !RazerHid.IsBatteryInterface(Interface("mi_03&col02"), v4[0]) &&
        !RazerHid.IsBatteryInterface(Interface("mi_03", 64), v4[0]),
        $"Viper V4 Pro {connection} battery interface allowlist");
}
Check(HeadsetRoster.Merge([
    new("blackshark-v2-pro", "BLACKSHARK V2 PRO", "headset", 79, false, true, "ok", "wireless", razerResolved.Single(x => x.Product == 0x0555).ContainerId),
    new("razer-headset", "Razer BlackShark V2 Pro", "headset", null, null, true, "unavailable", "wireless", razerResolved.Single(x => x.Product == 0x0555).ContainerId)
]).Count == 1, "BlackShark generic discovery merges with verified reader");
Check(HeadsetRoster.Merge([
    new("blackshark-v2-pro", "BLACKSHARK V2 PRO", "headset", null, null, true, "unavailable", "wireless"),
    new("razer-headset", "Razer BlackShark V2 Pro", "headset", null, null, true, "unavailable", "wireless")
]).Count == 1, "BlackShark remains one row without container property");
Check(RazerHid.ForProduct(0x00c1)?.Transaction == 0x1f &&
    RazerHid.ForProduct(0x0271)?.Transaction == 0x9f &&
    RazerHid.ForProduct(0x00e5) is { Transaction: 0x1f, Charging: false } &&
    RazerHid.ForProduct(0x00e6) is { Transaction: 0x1f, Charging: false } &&
    RazerHid.ForProduct(0x0555) is null, "Razer protocol product allowlist");
var razerRequest = RazerHid.Frame(0x1f, 0x80);
Check(razerRequest.Length == 91 && razerRequest[2] == 0x1f &&
    razerRequest[7] == 7 && razerRequest[8] == 0x80, "Razer read-only battery request");
byte[] RazerReply(byte command, byte value)
{
    var reply = RazerHid.Frame(0x1f, command);
    reply[1] = 2;
    reply[10] = value;
    byte crc = 0;
    for (var i = 3; i <= 88; i++) crc ^= reply[i];
    reply[89] = crc;
    return reply;
}
var razerBatteryReply = RazerReply(0x80, 204);
var razerChargingReply = RazerReply(0x84, 1);
Check(RazerHid.FromReplies(razerBatteryReply, razerChargingReply, 0x1f, true) ==
    new RazerHid.Result(80, true), "Razer battery and charging parse");
Check(RazerHid.FromReplies(null, razerChargingReply, 0x1f, true) ==
    new RazerHid.Result(null, null), "Razer timeout clears both readings");
Check(RazerHid.FromReplies(RazerReply(0x80, 204), null, 0x1f, false) ==
    new RazerHid.Result(80, null) &&
    RazerHid.FromReplies(null, null, 0x1f, false) == new RazerHid.Result(null, null),
    "Viper V4 Pro battery reading and failed-poll clearing without inferred charging");
razerBatteryReply[89] ^= 1;
Check(RazerHid.Parse(razerBatteryReply, 0x1f, 0x80) is null, "Razer CRC rejection");
Check(RazerHid.Parse(RazerReply(0x80, 0), 0x1f, 0x80) is null, "sleeping receiver zero is unknown");
Check(DeviceRoster.Visible(headsets, new WidgetSettings(ShowWiredHeadsets: false)).Count == 1, "wired setting filters only wired headset");
var hideUnknownSample = new DeviceState("v4", "Razer Viper V4 Pro", "mouse", 85, null, true, "ok");
var beforeHide = DeviceRoster.Visible([hideUnknownSample], new WidgetSettings());
var afterHide = DeviceRoster.Visible([hideUnknownSample], new WidgetSettings(HideUnreadableInformation: true));
Check(beforeHide.Count == 1 && beforeHide[0].StatusText.Contains("充电状态未知") &&
    afterHide.Count == 1 && afterHide[0].Name == hideUnknownSample.Name &&
    afterHide[0].Icon == hideUnknownSample.Icon && afterHide[0].StatusText == "85%" &&
    DeviceRoster.Visible(afterHide, new WidgetSettings())[0].StatusText.Contains("充电状态未知"),
    "appearance toggle immediately changes detail text without hiding the device");
Check(DeviceRoster.Visible([
    new("bt", "Bluetooth Headphones", "headset", null, null, true, "unavailable", "bluetooth"),
    new("w", "Wireless Headset", "headset", null, null, true, "unavailable", "wireless")
], new WidgetSettings(ShowBluetoothHeadsets: false)).Count == 1, "Bluetooth setting filters only Bluetooth headset");
var g522Request = G522Hid.BuildBatteryRequest();
Check(g522Request.Length == 64 && g522Request[0] == 0x50 && g522Request[1] == 0x23 &&
    g522Request[2] == 0x0b && g522Request[9] == 0x05 && g522Request[10] == 0x0a, "G522 read-only request");
var g522Reply = new byte[64];
g522Reply[0] = 0x50; g522Reply[1] = 0x23; g522Reply[2] = 0x0b;
g522Reply[9] = 0x05; g522Reply[11] = 74; g522Reply[13] = 0x02;
Check(G522Hid.Parse(g522Reply) is { Status: "ok", Battery: 74, Charging: true }, "G522 battery and charging");
g522Reply[11] = 101;
Check(G522Hid.Parse(g522Reply) is { Status: "error", Battery: null }, "G522 invalid battery rejected");
g522Reply[11] = 74; g522Reply[9] = 0;
Check(G522Hid.Parse(g522Reply) is null, "G522 unrelated frame rejected");
g522Reply[2] = 0x05; g522Reply[7] = 0;
Check(G522Hid.Parse(g522Reply) is { Status: "device_offline", Battery: null }, "G522 power-off clears battery");
Check(G522Hid.Parse([]) is null, "G522 missing response rejected");
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
Check(AtkMouseHid.KnownMouseName(1, 62) == "ATK F1 V3 ULTIMATE+", "F1 identity mapping");
Check(AtkMouseHid.KnownMouseName(2, 83) == "ATK A9 PLUS NK", "independent A9 Plus identity mapping");
Check(AtkMouseHid.KnownMouseName(1, 31) is null, "unverified A9 Mini+ identity not guessed");
Check(AtkMouseHid.ReceiverName(0x373b, 0x1278, "Wireless mouse 8k dongle-L") ==
    "ATK 8K Receiver", "shared receiver name stays neutral");
Check(AtkMouseHid.ReceiverName(0x373b, 0x1031, "ATK F1 Ultimate Receiver") ==
    "ATK Receiver", "other ATK receiver product string cannot assert paired model");
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
Check(atkResolved.Count(p => p.Name == "ATK 8K Receiver") == 2, "shared receiver does not claim F1 identity");
Check(atkResolved.Select(p => p.Id).Distinct().Count() == 6, "ATK ids unique by container");
Check(atkResolved.Any(p => p.Name == "ATK/VXE/VGN Receiver" && p.Icon == "mouse"), "published receiver ID admitted without model claim");
Check(atkResolved.Any(p => p.Name == "ATK68 V3" && p.Icon == "keyboard"), "compact ATK model name admitted");
Check(atkResolved.Any(p => p.Name == "VXE K75 Receiver" && p.Icon == "keyboard"), "unknown keyboard uses receiver name");
Check(atkResolved.Any(p => p.Name == "ATK Headset" && p.Icon == "headset"), "headset discovery");
var sharedReceiver = atkResolved.Single(p => p.ContainerId == receiverA);
var asleep = AtkBatteryProvider.ToState(sharedReceiver, new AtkMouseHid.Result
    { Status = "mouse_offline", Name = "ATK F1 V3 ULTIMATE+", Battery = 90, Charging = false });
Check(asleep.Name == "ATK 8K Receiver" && asleep.Battery is null && asleep.Charging is null,
    "receiver swap or sleep clears stale mouse identity and battery");
var awake = AtkBatteryProvider.ToState(sharedReceiver, new AtkMouseHid.Result
    { Status = "ok", Cid = 1, Mid = 62, Name = "ATK F1 V3 ULTIMATE+", Battery = 90, Charging = false });
Check(awake.Name == "ATK F1 V3 ULTIMATE+" && awake.Battery == 90,
    "verified paired mouse identity replaces receiver label");
var unknownMouse = AtkBatteryProvider.ToState(sharedReceiver, new AtkMouseHid.Result
    { Status = "ok", Name = "ATK 8K Receiver", Battery = 55, Charging = null });
Check(unknownMouse.Name == "ATK 8K Receiver" && unknownMouse.Battery == 55,
    "unknown paired mouse retains receiver label and valid battery");
var unknownIdentity = AtkBatteryProvider.ToState(sharedReceiver, new AtkMouseHid.Result
    { Status = "error", Cid = 1, Mid = 31, Name = "ATK F1 V3 ULTIMATE+", Battery = 90 });
Check(unknownIdentity.Name == "ATK 8K Receiver" && unknownIdentity.Battery is null,
    "failed identity or battery query cannot retain a previous model or percentage");
foreach (var product in new[] { 0x1031, 0x11d9, 0x104d })
{
    var otherReceiver = AtkDeviceDiscovery.Resolve([
        new(Guid.NewGuid(), $"USB\\VID_373B&PID_{product:X4}\\R", 0x373b, product,
            "ATK F1 Ultimate Receiver", "ATK", "Mouse")
    ]).Single();
    Check(otherReceiver.Name == "ATK Receiver" && otherReceiver.Icon == "mouse",
        "other ATK receiver ID does not assign a fixed paired model");
}
var wiredKnown = AtkDeviceDiscovery.Resolve([
    new(Guid.NewGuid(), "USB\\VID_373B&PID_1031\\W", 0x373b, 0x1031,
        "ATK F1 Ultimate", "ATK", "Mouse")
]).Single();
Check(wiredKnown.Name == "ATK F1 Ultimate", "descriptive wired product name remains available");
var a9Receiver = new AtkDeviceDiscovery.Peripheral("a9", Guid.NewGuid(), 0x373b, 0x10c9,
    "ATK NANO Receiver", "mouse");
var a9Plus = AtkBatteryProvider.ToState(a9Receiver, new AtkMouseHid.Result
    { Status = "ok", Cid = 2, Mid = 83, Name = "ATK A9 PLUS NK", Battery = 80, Charging = false });
Check(a9Plus.Name == "ATK A9 PLUS NK" && a9Plus.Battery == 80,
    "separate A9 Plus remains identified");
var wiredContainer = Guid.NewGuid();
var wiredMini = AtkDeviceDiscovery.Resolve([
    new(wiredContainer, "USB\\VID_373B&PID_4567\\A", 0x373b, 0x4567, "ATK A9 Mini+", "ATK", "USB"),
    new(wiredContainer, "HID\\VID_373B&PID_4567\\KEY", 0x373b, 0x4567, "HID Keyboard Device", "ATK", "Keyboard")
]);
Check(wiredMini.Count == 1 && wiredMini[0].Name == "ATK A9 Mini+" && wiredMini[0].Icon == "mouse",
    "wired A9 Mini+ product name takes mouse icon");
var composite = Guid.NewGuid();
Check(AtkDeviceDiscovery.Resolve([
    new(composite, "USB\\VID_373B&PID_4568\\A", 0x373b, 0x4568, "ATK Device", "ATK", "USB"),
    new(composite, "HID\\VID_373B&PID_4568\\M", 0x373b, 0x4568, "HID-compliant mouse", "ATK", "Mouse"),
    new(composite, "HID\\VID_373B&PID_4568\\K", 0x373b, 0x4568, "HID Keyboard Device", "ATK", "Keyboard")
]).Single().Icon == "mouse", "mouse HID collection outranks auxiliary keyboard");
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
