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
        if (args.Length != 1) throw new ArgumentException("Pass the output PNG path.");
        var window = new MainWindow();
        window.VisibleStates.Clear();
        window.VisibleStates.Add(new("blackshark-v2-pro", "BLACKSHARK V2 PRO", "headset", 79, false, true, "ok"));
        window.VisibleStates.Add(new("atk-f1-v3", "ATK F1 V3 ULTIMATE+", "mouse", null, null, true, "mouse_offline"));
        window.VisibleStates.Add(new("atk-a9-plus", "ATK A9 PLUS NK", "mouse", 80, false, true, "ok"));
        window.VisibleStates.Add(new("logitech-g-pro-x-superlight-2", "G PRO X SUPERLIGHT 2", "mouse", 77, false, true, "ok"));
        window.VisibleStates.Add(new("logitech-keyboard", "G915 LIGHTSPEED", "keyboard", 56, null, true, "ok"));
        window.Height = 24 + 64 * window.VisibleStates.Count;
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(window.Width, window.Height));
        content.Arrange(new Rect(0, 0, window.Width, window.Height));
        content.UpdateLayout();
        var render = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
        render.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(render));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
        using var output = File.Create(args[0]);
        encoder.Save(output);
        Console.WriteLine($"Rendered {window.Width}x{window.Height} preview to {args[0]}");
    }
}
