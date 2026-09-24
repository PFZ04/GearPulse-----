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

    public static bool Save(string path)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { language = Current }));
            File.Move(temporary, path, true);
            return true;
        }
        catch (Exception error)
        {
            AppLog.Write("Could not save language setting", error);
            return false;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); } catch { /* The current session still uses the selected language. */ }
            }
        }
    }

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

    public static string StatusText(DeviceState state)
    {
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
        var charging = state.Charging switch
        {
            true => Current switch { English => "Charging", TraditionalChinese => "充電中", _ => "充电中" },
            false => Current switch { English => "Not charging", TraditionalChinese => "未充電", _ => "未充电" },
            null => Current switch { English => "Charging status unknown", TraditionalChinese => "充電狀態未知", _ => "充电状态未知" }
        };
        return $"{state.Battery}% · {charging}";
    }
}
