using Microsoft.Win32;

namespace PowerPen.Interop;

/// <summary>
/// "Start PowerPen when Windows starts", using the per-user Run registry key (no admin rights needed).
/// </summary>
/// <remarks>
/// The registry is the only record of this choice (it isn't in settings.json), so it can never disagree with what
/// Windows actually does.
/// </remarks>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PowerPen";

    /// <summary>
    /// True if Windows will start THIS copy of PowerPen at sign-in. If the entry points to an old location
    /// (the exe was moved), this is false, and turning it on again fixes the path.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string command
                && string.Equals(command.Trim('"'), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Turns starting with Windows on (pointing at the running exe) or off.</summary>
    /// <exception cref="UnauthorizedAccessException">Company policy blocks changing the Run key.</exception>
    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\""); // Quoted, in case the path has spaces.
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
