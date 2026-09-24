using System.IO;
using System.Text.Json;

namespace GearPulse;

public static class UiLanguage
{
    public const string SimplifiedChinese = "zh-CN";
    public const string English = "en";
    public const string TraditionalChinese = "zh-TW";
    public static readonly IReadOnlyList<string> Available = [SimplifiedChinese, English, TraditionalChinese];

    public static string Current { get; private set; } = SimplifiedChinese;

    public static void Select(string language)
    {
        if (!Available.Contains(language, StringComparer.Ordinal))
            throw new ArgumentException("Unsupported language.", nameof(language));
        Current = language;
    }

    public static void Load(string path)
    {
        Current = SimplifiedChinese;
        try
        {
            if (!File.Exists(path)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var language = document.RootElement.GetProperty("language").GetString();
            if (language is not null && Available.Contains(language, StringComparer.Ordinal))
                Current = language;
        }
        catch (Exception error)
        {
            AppLog.Write("Could not read language setting", error);
        }
    }

    public static bool Save(string path) => SettingsJson.Update(path, root => root["language"] = Current);

    public static string AppearanceMenu => Current switch { English => "Appearance settings", TraditionalChinese => "外觀設定", _ => "外观设置" };
    public static string IconStyleLabel => Current switch { English => "Icon style", TraditionalChinese => "圖示樣式", _ => "图标样式" };
    public static string SizeLabel => Current switch { English => "Widget size", TraditionalChinese => "小工具大小", _ => "小组件大小" };
    public static string BackgroundOpacityLabel => Current switch { English => "Background opacity", TraditionalChinese => "背景不透明度", _ => "背景不透明度" };
    public static string ContentOpacityLabel => Current switch { English => "Text and icon opacity", TraditionalChinese => "文字與圖示不透明度", _ => "文字与图标不透明度" };
    public static string MonitorLabel => Current switch { English => "Display", TraditionalChinese => "顯示器", _ => "显示器" };
    public static string CornerLabel => Current switch { English => "Corner", TraditionalChinese => "角落", _ => "角落" };
    public static string ShowWiredHeadsets => Current switch { English => "Show wired headsets", TraditionalChinese => "顯示有線耳機", _ => "显示有线耳机" };
    public static string ShowBluetoothHeadsets => Current switch { English => "Show Bluetooth headsets", TraditionalChinese => "顯示藍牙耳機", _ => "显示蓝牙耳机" };
    public static string HideUnreadableInformation => Current switch { English => "Hide unavailable information", TraditionalChinese => "隱藏無法讀取的資訊", _ => "隐藏无法读取的信息" };
    public static string LineIcons => Current switch { English => "Line", TraditionalChinese => "線條", _ => "线条" };
    public static string SilhouetteIcons => Current switch { English => "Silhouette", TraditionalChinese => "剪影", _ => "剪影" };
    public static string SmallSize => Current switch { English => "Small", TraditionalChinese => "小", _ => "小" };
    public static string MediumSize => Current switch { English => "Medium", TraditionalChinese => "中", _ => "中" };
    public static string LargeSize => Current switch { English => "Large", TraditionalChinese => "大", _ => "大" };
    public static string PrimaryDisplay => Current switch { English => "Primary display", TraditionalChinese => "主顯示器", _ => "主显示器" };
    public static string DisconnectedDisplay => Current switch { English => "disconnected", TraditionalChinese => "已斷開", _ => "已断开" };
    public static string TopLeft => Current switch { English => "Top left", TraditionalChinese => "左上", _ => "左上" };
    public static string TopRight => Current switch { English => "Top right", TraditionalChinese => "右上", _ => "右上" };
    public static string BottomLeft => Current switch { English => "Bottom left", TraditionalChinese => "左下", _ => "左下" };
    public static string BottomRight => Current switch { English => "Bottom right", TraditionalChinese => "右下", _ => "右下" };

    public static string WindowTitle => Current switch
    {
        English => "GearPulse | Peripheral Pulse",
        TraditionalChinese => "GearPulse | 外設脈動",
        _ => "GearPulse | 外设脉动"
    };

    public static string ShowWidget => Current switch
    {
        English => "Show widget",
        TraditionalChinese => "顯示小工具",
        _ => "显示小组件"
    };

    public static string StartWithWindows => Current switch
    {
        English => "Start with Windows",
        TraditionalChinese => "開機啟動",
        _ => "开机启动"
    };

    public static string InstallFirst => Current switch
    {
        English => "Start with Windows (install first)",
        TraditionalChinese => "開機啟動（需先安裝）",
        _ => "开机启动（需先安装）"
    };

    public static string LanguageMenu => Current switch
    {
        English => "Language",
        TraditionalChinese => "語言",
        _ => "语言"
    };

    public static string Exit => Current switch
    {
        English => "Exit",
        TraditionalChinese => "退出",
        _ => "退出"
    };

    public static string AutostartError => Current switch
    {
        English => "Could not change the startup task. Please run the installation script again.",
        TraditionalChinese => "無法修改開機啟動工作。請重新執行安裝腳本。",
        _ => "无法修改开机启动任务。请重新运行安装脚本。"
    };

    public static string StatusText(DeviceState state, bool hideUnreadableInformation = false)
    {
        if (state.Status == "empty") return Current switch
        {
            English => "No devices found",
            TraditionalChinese => "未發現裝置",
            _ => "未发现设备"
        };
        if (hideUnreadableInformation && state.Battery is null) return "";
        if (state.Status == "mouse_offline") return Current switch
        {
            English => "Mouse disconnected or asleep",
            TraditionalChinese => "滑鼠未連接或休眠",
            _ => "鼠标未连接或休眠"
        };
        if (state.Status == "device_offline") return Current switch
        {
            English => "Device disconnected or asleep",
            TraditionalChinese => "裝置未連接或休眠",
            _ => "设备未连接或休眠"
        };
        if (!state.ReceiverPresent) return Current switch
        {
            English => "Receiver disconnected",
            TraditionalChinese => "接收器未連接",
            _ => "接收器未连接"
        };
        if (state.Battery is null) return Current switch
        {
            English => state.Status == "device_busy" ? "Device is being read" : "Battery unavailable",
            TraditionalChinese => state.Status == "device_busy" ? "正在讀取裝置" : "電量暫不可用",
            _ => state.Status == "device_busy" ? "设备正在被读取" : "电量暂不可用"
        };
        if (hideUnreadableInformation && state.Charging is null) return $"{state.Battery}%";
        var charging = state.Charging switch
        {
            true => Current switch { English => "Charging", TraditionalChinese => "充電中", _ => "充电中" },
            false => Current switch { English => "Not charging", TraditionalChinese => "未充電", _ => "未充电" },
            null => Current switch { English => "Charging status unknown", TraditionalChinese => "充電狀態未知", _ => "充电状态未知" }
        };
        return $"{state.Battery}% · {charging}";
    }
}
