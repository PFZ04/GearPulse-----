using System.Drawing;
using System.Windows.Forms;

namespace GearPulse;

public static class WidgetPlacement
{
    public static Screen? SelectScreen(IEnumerable<Screen> screens, string? deviceName)
    {
        var available = screens.ToArray();
        return available.FirstOrDefault(screen => screen.DeviceName == deviceName)
            ?? available.FirstOrDefault(screen => screen.Primary)
            ?? available.FirstOrDefault();
    }

    public static Rectangle Calculate(Rectangle area, int width, int height, int margin, string corner)
    {
        var x = corner is "top-left" or "bottom-left" ? area.Left + margin : area.Right - width - margin;
        var y = corner is "top-left" or "top-right" ? area.Top + margin : area.Bottom - height - margin;
        return new Rectangle(
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height)), width, height);
    }
}
