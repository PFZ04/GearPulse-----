using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GearPulse;

namespace GearPulseVisualPreview;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length is < 1 or > 6) throw new ArgumentException("Pass the output PNG path, optional language, icon style, size, background opacity, and content opacity.");
        UiLanguage.Select(args.Length >= 2 ? args[1] : UiLanguage.SimplifiedChinese);
        if (args.Length >= 3 && args[2] == "settings")
        {
            var settings = new AppearanceWindow(new WidgetSettings(), _ => { });
            var panel = (FrameworkElement)settings.Content;
            panel.Measure(new Size(settings.Width, settings.Height));
            panel.Arrange(new Rect(0, 0, settings.Width, settings.Height));
            panel.UpdateLayout();
            Render(panel, settings.Width, settings.Height, args[0]);
            return;
        }
        var window = new MainWindow();
        var style = args.Length >= 3 ? args[2] : "line";
        var size = args.Length >= 4 ? args[3] : "medium";
        var background = args.Length >= 5 ? int.Parse(args[4]) : 92;
        var foreground = args.Length >= 6 ? int.Parse(args[5]) : 100;
        var appearance = new WidgetSettings(IconStyle: style, Size: size,
            BackgroundOpacity: background, ContentOpacity: foreground);
        window.ApplySettings(appearance);
        window.VisibleStates.Clear();
        window.VisibleStates.Add(new("blackshark-v2-pro", "BLACKSHARK V2 PRO", "headset", 79, false, true, "ok"));
        window.VisibleStates.Add(new("g522", "Logitech G522 LIGHTSPEED", "headset", 74, true, true, "ok", "wireless"));
        window.VisibleStates.Add(new("headphones", "Headphones (Realtek Audio)", "headset", null, null, true, "unavailable", "wired"));
        window.VisibleStates.Add(new("atk-f1-v3", "ATK F1 V3 ULTIMATE+", "mouse", null, null, true, "mouse_offline"));
        window.VisibleStates.Add(new("atk-a9-plus", "ATK A9 PLUS NK", "mouse", 80, false, true, "ok"));
        window.VisibleStates.Add(new("logitech-g-pro-x-superlight-2", "G PRO X SUPERLIGHT 2", "mouse", 77, false, true, "ok"));
        window.VisibleStates.Add(new("logitech-keyboard", "G915 LIGHTSPEED", "keyboard", 56, null, true, "ok"));
        window.ApplySettings(appearance);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(window.Width, window.Height));
        content.Arrange(new Rect(0, 0, window.Width, window.Height));
        content.UpdateLayout();
        Render(content, window.Width, window.Height, args[0]);
    }

    private static void Render(FrameworkElement content, double width, double height, string path)
    {
        var render = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        render.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(render));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = File.Create(path);
        encoder.Save(output);
        Console.WriteLine($"Rendered {width}x{height} preview to {path}");
    }
}
