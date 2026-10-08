using System.IO;

namespace PowerPen.Diagnostics;

/// <summary>
/// Simple daily log files in %AppData%\PowerPen\logs, for figuring out problems on a teammate's PC.
/// Logging never throws: failing to write a log line must never break the app.
/// </summary>
public static class Log
{
    /// <summary>Log files older than this are deleted at startup.</summary>
    private const int DaysToKeep = 14;

    private static readonly object WriteLock = new();

    /// <summary>Folder holding the log files.</summary>
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerPen", "logs");

    /// <summary>Off by default; the app turns it on (the self-test and dev tools leave it off).</summary>
    public static bool IsEnabled { get; set; }

    /// <summary>Today's log file.</summary>
    public static string CurrentFile => Path.Combine(Folder, $"PowerPen-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>Records normal events (startup, settings saved, screenshot saved).</summary>
    public static void Info(string message) => Write("INFO", message);

    /// <summary>Records problems PowerPen worked around (hotkey taken, setting replaced with default).</summary>
    public static void Warn(string message) => Write("WARN", message);

    /// <summary>Records failures, with the full error details when there is one.</summary>
    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    /// <summary>Deletes log files older than two weeks so the folder never grows without limit.</summary>
    public static void DeleteOldFiles()
    {
        try
        {
            if (!Directory.Exists(Folder))
                return;

            DateTime cutoff = DateTime.Now.AddDays(-DaysToKeep);
            foreach (string file in Directory.EnumerateFiles(Folder, "PowerPen-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best-effort.
        }
    }

    /// <summary>Appends one timestamped line to today's file.</summary>
    private static void Write(string level, string message)
    {
        if (!IsEnabled)
            return;

        try
        {
            // Lock: hotkeys, timers, and background errors can all log at the same time.
            lock (WriteLock)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(CurrentFile, $"{DateTime.Now:HH:mm:ss.fff} {level,-5} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Never let logging break the app.
        }
    }
}
