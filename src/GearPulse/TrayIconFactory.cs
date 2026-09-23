using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GearPulse;

public static class TrayIconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Color.White, 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var background = new SolidBrush(Color.FromArgb(28, 32, 40)))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillEllipse(background, 0, 0, 31, 31);
            g.DrawLines(pen,
            [
                new PointF(4, 17), new PointF(10, 17), new PointF(13, 11),
                new PointF(17, 23), new PointF(20, 14), new PointF(23, 17),
                new PointF(28, 17)
            ]);
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally { DestroyIcon(handle); }
    }
}
