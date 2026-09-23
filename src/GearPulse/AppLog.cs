using System.IO;

namespace GearPulse;

public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetEnvironmentVariable("GEARPULSE_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GearPulse"),
        "gear-pulse.log");

    public static void Write(string message, Exception? error = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:o} {message}{(error is null ? "" : ": " + error)}{Environment.NewLine}");
            }
        }
        catch (Exception loggingError)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "gear-pulse-fallback.log"),
                    $"{DateTimeOffset.Now:o} {message}: {error}; primary log failed: {loggingError}{Environment.NewLine}");
            }
            catch { /* Logging must never stop the widget. */ }
        }
    }
}
