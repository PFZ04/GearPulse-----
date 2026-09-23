using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;

namespace GearPulse;

public partial class MainWindow : Window
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint GwHwndPrev = 3;
    private const int WmDpiChanged = 0x02E0;
    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private readonly BatterySampler sampler = new(DeviceRoster.Providers);
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer desktopTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ObservableCollection<DeviceState> visibleStates = [];
    private IntPtr desktopHost;
    private System.Drawing.Rectangle lastPlacement;
    private bool userHidden;
    private bool exiting;
    private bool hostMissingLogged;

    public ObservableCollection<DeviceState> VisibleStates => visibleStates;
    public bool IsUserVisible => !userHidden;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ApplyStates(DeviceRoster.Initial());
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            source.AddHook(WindowMessage);
            var hwnd = source.Handle;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | WsExToolWindow | WsExNoActivate);
        };
        Loaded += (_, _) =>
        {
            AppLog.Write("WPF window loaded");
            UpdateDesktopPosition();
            _ = RefreshAsync();
            refreshTimer.Start();
            desktopTimer.Start();
        };
        refreshTimer.Tick += (_, _) => _ = RefreshAsync();
        desktopTimer.Tick += (_, _) => UpdateDesktopPosition();
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is WmDpiChanged or WmDisplayChange or WmSettingChange)
            Dispatcher.BeginInvoke(UpdateDesktopPosition);
        return IntPtr.Zero;
    }

    public void SetUserVisible(bool visible)
    {
        userHidden = !visible;
        if (!visible) Hide();
        else UpdateDesktopPosition();
    }

    private async Task RefreshAsync()
    {
        var states = await sampler.SampleAsync();
        if (states is not null && !exiting) ApplyStates(states);
    }

    private void ApplyStates(IEnumerable<DeviceState> states)
    {
        visibleStates.Clear();
        foreach (var state in DeviceRoster.Visible(states)) visibleStates.Add(state);
        var source = (HwndSource?)PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget.TransformToDevice.M22 ?? 1;
        var maxHeight = (Screen.PrimaryScreen?.WorkingArea.Height ?? 900) / scale - 40;
        Height = Math.Min(24 + 64 * Math.Max(1, visibleStates.Count), Math.Max(88, maxHeight));
        if (IsLoaded) UpdateDesktopPosition();
    }

    private static IntPtr FindDesktopHost()
    {
        var host = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            host = window;
            return false;
        }, IntPtr.Zero);
        return host;
    }

    private void UpdateDesktopPosition()
    {
        if (exiting || userHidden) return;
        try
        {
            var host = FindDesktopHost();
            if (host == IntPtr.Zero)
            {
                if (!hostMissingLogged) AppLog.Write("Desktop host unavailable; waiting for Explorer");
                hostMissingLogged = true;
                Hide(); desktopHost = IntPtr.Zero; return;
            }
            hostMissingLogged = false;
            var screen = Screen.PrimaryScreen;
            if (screen is null) return;
            var source = (HwndSource?)PresentationSource.FromVisual(this);
            if (source is null) return;
            var scale = source.CompositionTarget.TransformToDevice;
            var width = (int)Math.Round(Width * scale.M11);
            var height = (int)Math.Round(Height * scale.M22);
            var margin = (int)Math.Round(20 * scale.M11);
            var area = screen.WorkingArea;
            var placement = new System.Drawing.Rectangle(area.Right - width - margin,
                area.Bottom - height - margin, width, height);
            var hwnd = source.Handle;
            var predecessor = GetWindow(host, GwHwndPrev);
            var needZOrder = predecessor != hwnd;
            if (host != desktopHost || !IsVisible || placement != lastPlacement || needZOrder)
            {
                var moved = host != desktopHost || placement != lastPlacement;
                if (!IsVisible) Show();
                var flags = SwpNoActivate | SwpShowWindow | (needZOrder ? 0u : SwpNoZOrder);
                if (!SetWindowPos(hwnd, predecessor, placement.X, placement.Y, width, height, flags))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                desktopHost = host;
                lastPlacement = placement;
                if (moved) AppLog.Write($"Desktop card positioned at {placement.X},{placement.Y} {width}x{height}");
            }
        }
        catch (Exception error)
        {
            AppLog.Write("Desktop positioning failed", error);
            Hide();
            desktopHost = IntPtr.Zero;
        }
    }

    public void Exit()
    {
        exiting = true;
        refreshTimer.Stop();
        desktopTimer.Stop();
        Close();
    }
}
