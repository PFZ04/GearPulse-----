using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace GearPulse;

public partial class App : Application
{
    private const string InstanceName = @"Local\GearPulseWpf";
    private const string ExitEventName = @"Local\GearPulseWpfExit";
    private const string ShowEventName = @"Local\GearPulseWpfShow";
    private Mutex? instance;
    private EventWaitHandle? exitEvent;
    private EventWaitHandle? showEvent;
    private RegisteredWaitHandle? exitRegistration;
    private RegisteredWaitHandle? showRegistration;
    private NotifyIcon? tray;
    private System.Drawing.Icon? icon;
    private MainWindow? window;
    private static readonly string LanguagePath = Path.Combine(AppLog.DataDirectory, "settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var exitRequest = e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase);
        var showRequest = e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase);
        if (exitRequest || showRequest)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(exitRequest ? ExitEventName : ShowEventName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }

        instance = new Mutex(true, InstanceName, out var firstInstance);
        if (!firstInstance)
        {
            try { using var signal = EventWaitHandle.OpenExisting(ShowEventName); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }

        try
        {
            UiLanguage.Load(LanguagePath);
            exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            exitRegistration = ThreadPool.RegisterWaitForSingleObject(exitEvent,
                (_, _) => Dispatcher.BeginInvoke(QuitApplication), null, Timeout.Infinite, false);
            showRegistration = ThreadPool.RegisterWaitForSingleObject(showEvent,
                (_, _) => Dispatcher.BeginInvoke(() => commands?.SetVisible(true)), null, Timeout.Infinite, false);
            window = new MainWindow();
            commands = new TrayCommands(new WindowsStartupTask(), window.SetUserVisible, QuitApplication);
            BuildTray();
            window.Show();
            AppLog.Write("WPF widget started");
        }
        catch (Exception error)
        {
            AppLog.Write("Startup failed", error);
            Shutdown();
        }
    }

    private TrayCommands? commands;

    private void BuildTray()
    {
        icon = TrayIconFactory.Create();
        var menu = new ContextMenuStrip();
        var visible = new ToolStripMenuItem { Checked = true, CheckOnClick = false };
        visible.Click += (_, _) => { commands!.SetVisible(!commands.IsVisible); visible.Checked = commands.IsVisible; };
        var startup = new ToolStripMenuItem();
        startup.Click += (_, _) =>
        {
            try { commands!.SetAutostart(startup.Checked == false); }
            catch (Exception error)
            {
                AppLog.Write("Could not change autostart", error);
                System.Windows.MessageBox.Show(UiLanguage.AutostartError, "GearPulse");
            }
        };
        var quit = new ToolStripMenuItem();
        quit.Click += (_, _) => commands!.Exit();
        var language = new ToolStripMenuItem();
        foreach (var (code, name) in new[]
        {
            (UiLanguage.SimplifiedChinese, "简体中文"),
            (UiLanguage.English, "English"),
            (UiLanguage.TraditionalChinese, "繁體中文")
        })
        {
            var item = new ToolStripMenuItem(name) { Tag = code };
            item.Click += (_, _) =>
            {
                UiLanguage.Select(code);
                UiLanguage.Save(LanguagePath);
                window!.RefreshLanguage();
                UpdateTexts();
            };
            language.DropDownItems.Add(item);
        }
        void UpdateTexts()
        {
            visible.Text = UiLanguage.ShowWidget;
            var state = commands!.AutostartEnabled;
            startup.Enabled = state.HasValue;
            startup.Checked = state == true;
            startup.Text = state.HasValue ? UiLanguage.StartWithWindows : UiLanguage.InstallFirst;
            language.Text = UiLanguage.LanguageMenu;
            foreach (ToolStripMenuItem item in language.DropDownItems)
                item.Checked = (string)item.Tag! == UiLanguage.Current;
            quit.Text = UiLanguage.Exit;
            tray!.Text = UiLanguage.WindowTitle;
        }
        menu.Opening += (_, _) =>
        {
            visible.Checked = commands!.IsVisible;
            UpdateTexts();
        };
        menu.Items.AddRange([visible, startup, language, new ToolStripSeparator(), quit]);
        tray = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Visible = true };
        UpdateTexts();
        tray.DoubleClick += (_, _) => { commands!.SetVisible(true); };
    }

    private void QuitApplication()
    {
        window?.Exit();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        icon?.Dispose();
        exitRegistration?.Unregister(null);
        showRegistration?.Unregister(null);
        exitEvent?.Dispose();
        showEvent?.Dispose();
        if (instance is not null)
        {
            try { instance.ReleaseMutex(); } catch (ApplicationException) { }
            instance.Dispose();
        }
        AppLog.Write("WPF widget stopped");
        base.OnExit(e);
    }
}
