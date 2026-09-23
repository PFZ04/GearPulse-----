// SPDX-License-Identifier: GPL-2.0-or-later
// Desktop-only battery card. The data provider contract is intentionally device-agnostic.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public sealed class DeviceBatteryState
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int? Battery { get; set; }
    public bool? Charging { get; set; }
    public bool ReceiverPresent { get; set; }
    public string Status { get; set; }
    public string Icon { get; set; }
}

public interface IBatteryProvider
{
    string Id { get; }
    DeviceBatteryState Read();
}

public sealed class FixedBatteryProvider : IBatteryProvider
{
    readonly DeviceBatteryState value;
    public string Id { get { return value.Id; } }
    public FixedBatteryProvider(DeviceBatteryState value) { this.value = value; }
    public DeviceBatteryState Read() { return value; }
}

public sealed class BlackSharkBatteryProvider : IBatteryProvider
{
    public string Id { get { return "blackshark-v2-pro"; } }

    public DeviceBatteryState Read()
    {
        try
        {
            using (var mutex = new Mutex(false, @"Local\BlackSharkBatteryDiagnostic-1532-0555"))
            {
                bool held = false;
                try
                {
                    try { held = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { held = true; }
                    if (!held)
                        return new DeviceBatteryState { Id = Id, Name = "BLACKSHARK V2 PRO", Icon = "headset", ReceiverPresent = true, Status = "device_busy" };

                    // BlackSharkHid is loaded by PowerShell Add-Type first. Resolve its
                    // in-memory assembly here so the UI can compile independently.
                    Type hid = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("BlackSharkHid", false)).FirstOrDefault(t => t != null);
                    if (hid == null) throw new InvalidOperationException("BlackSharkHid is not loaded.");
                    object sample = hid.GetMethod("Sample", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { 1000 });
                    Type resultType = sample.GetType();
                    return new DeviceBatteryState {
                        Id = Id, Name = "BLACKSHARK V2 PRO", Icon = "headset",
                        Battery = (int?)resultType.GetProperty("Battery").GetValue(sample),
                        Charging = (bool?)resultType.GetProperty("Charging").GetValue(sample),
                        ReceiverPresent = (bool)resultType.GetProperty("ReceiverPresent").GetValue(sample),
                        Status = (string)resultType.GetProperty("Status").GetValue(sample)
                    };
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }
        catch { return new DeviceBatteryState { Id = Id, Name = "BLACKSHARK V2 PRO", Icon = "headset", ReceiverPresent = true, Status = "error" }; }
    }
}

public sealed class AtkMouseBatteryProvider : IBatteryProvider
{
    readonly int product;
    readonly string name;
    public string Id { get { return product == 0x1278 ? "atk-f1-v3" : "atk-a9-plus"; } }

    public AtkMouseBatteryProvider(int product)
    {
        if (product != 0x1278 && product != 0x10c9) throw new ArgumentException("Unsupported ATK receiver.");
        this.product = product;
        name = product == 0x1278 ? "ATK F1 V3 ULTIMATE+" : "ATK A9 PLUS NK";
    }

    public DeviceBatteryState Read()
    {
        try
        {
            Type hid = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("AtkMouseHid", false)).FirstOrDefault(t => t != null);
            if (hid == null) throw new InvalidOperationException("AtkMouseHid is not loaded.");
            object sample = hid.GetMethod("SampleByProduct", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { product, 1000 });
            Type type = sample.GetType();
            string status = (string)type.GetProperty("Status").GetValue(sample);
            if (status == "receiver_absent")
                return new DeviceBatteryState { Id=Id, Name=name, Icon="mouse", Status="hidden" };
            return new DeviceBatteryState {
                Id=Id, Name=(string)type.GetProperty("Name").GetValue(sample) ?? name, Icon="mouse",
                Battery=(int?)type.GetProperty("Battery").GetValue(sample),
                Charging=(bool?)type.GetProperty("Charging").GetValue(sample),
                ReceiverPresent=true, Status=status
            };
        }
        catch { return new DeviceBatteryState { Id=Id, Name=name, Icon="mouse", ReceiverPresent=true, Status="error" }; }
    }
}

public static class BatteryWidgetLayout
{
    public const int IconSize = 40;
    public const int RowHeight = 64;
    public const int Padding = 12;
    public const int Width = 270;
    public static int Height(int count) { return Padding * 2 + RowHeight * Math.Max(1, count); }

    public static string StatusText(DeviceBatteryState item)
    {
        if (item.Status == "mouse_offline") return "鼠标未连接或休眠";
        if (!item.ReceiverPresent) return "接收器未连接";
        if (!item.Battery.HasValue) return item.Status == "device_busy" ? "设备正在被读取" : "电量暂不可用";
        var state = !item.Charging.HasValue ? "充电状态未知" : item.Charging.Value ? "充电中" : "未充电";
        return item.Battery.Value + "% · " + state;
    }
}

public sealed class BatteryWidgetWindow : Form
{
    readonly IReadOnlyList<IBatteryProvider> providers;
    readonly System.Windows.Forms.Timer refreshTimer;
    readonly System.Windows.Forms.Timer desktopTimer;
    readonly string stopFile;
    List<DeviceBatteryState> readings;
    IntPtr desktopHost = IntPtr.Zero;
    Rectangle lastPlacement = Rectangle.Empty;
    int sampling;
    bool closing;

    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_SHOWWINDOW = 0x0040;
    const uint GW_HWNDPREV = 3;
    static readonly IntPtr HWND_TOP = IntPtr.Zero;

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    public BatteryWidgetWindow(IReadOnlyList<IBatteryProvider> providers, string stopFile)
    {
        if (providers == null || providers.Count == 0) throw new ArgumentException("At least one provider is required.");
        this.providers = providers;
        this.stopFile = stopFile;
        readings = providers.Select(p => p is FixedBatteryProvider ? p.Read() : p is AtkMouseBatteryProvider ?
            new DeviceBatteryState { Id=p.Id, Status="hidden" } :
            new DeviceBatteryState { Id = p.Id, Name = p.Id, Icon = "headset", ReceiverPresent = true, Status = "pending" }).ToList();

        Text = "GearPulse | 外设脉动";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(28, 32, 40);
        Opacity = 0.92;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Width = BatteryWidgetLayout.Width;
        Height = BatteryWidgetLayout.Height(VisibleReadings.Count);

        refreshTimer = new System.Windows.Forms.Timer { Interval = 10000 };
        refreshTimer.Tick += (s, e) => StartSample();
        desktopTimer = new System.Windows.Forms.Timer { Interval = 250 };
        desktopTimer.Tick += (s, e) => {
            if (System.IO.File.Exists(this.stopFile)) { closing = true; Close(); return; }
            try { UpdateDesktopVisibility(); }
            catch (Exception error) {
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(this.stopFile), "widget.log"),
                    DateTimeOffset.Now.ToString("o") + " Desktop error: " + error + Environment.NewLine);
                closing = true;
                Close(); // The launcher will create a fresh window after a short delay.
            }
        };
        Shown += (s, e) => { UpdateDesktopVisibility(); StartSample(); refreshTimer.Start(); desktopTimer.Start(); };
        FormClosed += (s, e) => { closing = true; refreshTimer.Stop(); desktopTimer.Stop(); refreshTimer.Dispose(); desktopTimer.Dispose(); };
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    List<DeviceBatteryState> VisibleReadings { get { return readings.Where(r => r.Status != "hidden").ToList(); } }
    protected override CreateParams CreateParams {
        get {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return parameters;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (ClientSize.Width < 24 || ClientSize.Height < 24) return;
        float radius = 12f * (DeviceDpi > 0 ? DeviceDpi / 96f : 1f);
        using (var path = new GraphicsPath()) {
            float width = ClientSize.Width - 1;
            float height = ClientSize.Height - 1;
            path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
            path.AddArc(width - radius * 2, 0, radius * 2, radius * 2, 270, 90);
            path.AddArc(width - radius * 2, height - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(0, height - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            var old = Region;
            Region = new Region(path);
            if (old != null) old.Dispose();
        }
    }

    static IntPtr FindDesktopHost()
    {
        IntPtr host = IntPtr.Zero;
        EnumWindows((window, ignored) => {
            if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) {
                host = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return host;
    }

    void UpdateDesktopVisibility()
    {
        // A top-level tool window placed immediately above Progman is visible
        // on the desktop while ordinary windows naturally cover it. Embedding
        // WinForms into Explorer's WorkerW is not composed over the live DX wall.
        IntPtr host = FindDesktopHost();
        if (host == IntPtr.Zero) { Visible = false; desktopHost = IntPtr.Zero; return; }
        var screen = Screen.PrimaryScreen;
        int dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        int margin = (int)Math.Round(20.0 * dpi / 96.0);
        int width = (int)Math.Round(BatteryWidgetLayout.Width * dpi / 96.0);
        int height = (int)Math.Round(BatteryWidgetLayout.Height(VisibleReadings.Count) * dpi / 96.0);
        var area = screen.WorkingArea;
        var placement = new Rectangle(area.Right - width - margin, area.Bottom - height - margin, width, height);
        IntPtr predecessor = GetWindow(host, GW_HWNDPREV);
        bool needZOrder = predecessor != Handle;
        if (host != desktopHost || !Visible || placement != lastPlacement || needZOrder) {
            uint flags = SWP_NOACTIVATE | SWP_SHOWWINDOW | (needZOrder ? 0u : SWP_NOZORDER);
            IntPtr after = predecessor == IntPtr.Zero ? HWND_TOP : predecessor;
            if (!SetWindowPos(Handle, after, placement.X, placement.Y, width, height, flags))
                throw new InvalidOperationException("Could not position desktop widget (Win32 " + Marshal.GetLastWin32Error() + ").");
            desktopHost = host;
            lastPlacement = placement;
            if (!Visible) Visible = true;
        }
    }

    void StartSample()
    {
        if (Interlocked.Exchange(ref sampling, 1) != 0) return;
        Task.Run(() => {
            var next = new List<DeviceBatteryState>();
            foreach (var provider in providers)
            {
                try {
                    var value = provider.Read();
                    next.Add(value ?? new DeviceBatteryState { Id = provider.Id, Name = provider.Id, ReceiverPresent = true, Status = "error" });
                }
                catch { next.Add(new DeviceBatteryState { Id = provider.Id, Name = provider.Id, ReceiverPresent = true, Status = "error" }); }
            }
            try {
                if (!closing && IsHandleCreated) BeginInvoke((Action)(() => { readings = next; Invalidate(); }));
            } catch (InvalidOperationException) { }
            finally { Interlocked.Exchange(ref sampling, 0); }
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(28, 32, 40));
        float scale = DeviceDpi > 0 ? DeviceDpi / 96f : 1f;
        using (var nameFont = new Font("Segoe UI", 9f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var valueFont = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var nameBrush = new SolidBrush(Color.FromArgb(218, 225, 236)))
        using (var normalBrush = new SolidBrush(Color.White))
        using (var lowBrush = new SolidBrush(Color.FromArgb(255, 151, 64)))
        using (var iconPen = new Pen(Color.FromArgb(220, 232, 245), 2f * scale))
        {
            int pad = (int)(BatteryWidgetLayout.Padding * scale);
            int row = (int)(BatteryWidgetLayout.RowHeight * scale);
            int icon = (int)(BatteryWidgetLayout.IconSize * scale);
            var visible = VisibleReadings;
            for (int i = 0; i < visible.Count; i++)
            {
                var item = visible[i];
                int y = pad + i * row;
                if (item.Icon == "headset") DrawHeadset(g, iconPen, pad, y + (row - icon) / 2, icon);
                else if (item.Icon == "mouse") DrawMouse(g, iconPen, pad, y + (row - icon) / 2, icon);
                else DrawGenericBattery(g, iconPen, pad, y + (row - icon) / 2, icon);
                int textX = pad + icon + (int)(12 * scale);
                g.DrawString(item.Name ?? item.Id ?? "DEVICE", nameFont, nameBrush, textX, y + 10 * scale);
                string status = BatteryWidgetLayout.StatusText(item);
                var valueBrush = item.Battery.HasValue && item.Battery.Value <= 20 ? lowBrush : normalBrush;
                g.DrawString(status, valueFont, valueBrush, textX, y + 30 * scale);
            }
        }
    }

    static void DrawHeadset(Graphics g, Pen pen, int x, int y, int size)
    {
        float s = size / 40f;
        g.DrawArc(pen, x + 7 * s, y + 6 * s, 26 * s, 29 * s, 183, 174);
        g.DrawRoundedRectangleCompat(pen, x + 5 * s, y + 21 * s, 7 * s, 13 * s, 2 * s);
        g.DrawRoundedRectangleCompat(pen, x + 28 * s, y + 21 * s, 7 * s, 13 * s, 2 * s);
        g.DrawArc(pen, x + 17 * s, y + 27 * s, 17 * s, 10 * s, 0, 130);
    }

    static void DrawGenericBattery(Graphics g, Pen pen, int x, int y, int size)
    {
        float s = size / 40f;
        g.DrawRoundedRectangleCompat(pen, x + 6 * s, y + 12 * s, 26 * s, 17 * s, 3 * s);
        g.DrawLine(pen, x + 35 * s, y + 17 * s, x + 35 * s, y + 24 * s);
        g.DrawLine(pen, x + 13 * s, y + 17 * s, x + 13 * s, y + 24 * s);
        g.DrawLine(pen, x + 18 * s, y + 17 * s, x + 18 * s, y + 24 * s);
    }

    static void DrawMouse(Graphics g, Pen pen, int x, int y, int size)
    {
        float s=size/40f;
        g.DrawRoundedRectangleCompat(pen,x+11*s,y+3*s,18*s,34*s,9*s);
        g.DrawLine(pen,x+20*s,y+4*s,x+20*s,y+17*s);
        g.DrawLine(pen,x+15*s,y+19*s,x+25*s,y+19*s);
    }
}

static class GraphicsExtensions
{
    public static void DrawRoundedRectangleCompat(this Graphics g, Pen pen, float x, float y, float width, float height, float radius)
    {
        using (var path = new GraphicsPath())
        {
            path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
            path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
            path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            g.DrawPath(pen, path);
        }
    }
}
