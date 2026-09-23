namespace GearPulse;

public interface IStartupTask
{
    bool? IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public sealed class WindowsStartupTask : IStartupTask
{
    public bool? IsEnabled
    {
        get
        {
            try { return ReadTask().Enabled; }
            catch { return null; }
        }
    }

    public void SetEnabled(bool enabled)
    {
        dynamic task = ReadTask();
        task.Enabled = enabled;
    }

    private static dynamic ReadTask()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        return folder.GetTask("GearPulse");
    }
}

public sealed class TrayCommands(IStartupTask startupTask, Action<bool> showWidget, Action quit)
{
    public bool IsVisible { get; private set; } = true;
    public bool? AutostartEnabled => startupTask.IsEnabled;

    public void SetVisible(bool visible)
    {
        IsVisible = visible;
        showWidget(visible);
    }

    public void SetAutostart(bool enabled) => startupTask.SetEnabled(enabled);
    public void Exit() => quit();
}
