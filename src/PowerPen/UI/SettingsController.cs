using System.IO;
using System.Security;
using System.Windows;
using PowerPen.Diagnostics;
using PowerPen.Input;
using PowerPen.Interop;
using PowerPen.Settings;

namespace PowerPen.UI;

/// <summary>
/// Opens the Settings window (one at a time), pauses the global hotkeys while it's open so they can be re-recorded,
/// and applies saved changes everywhere without a restart.
/// </summary>
public sealed class SettingsController
{
    private readonly AppHost _host;
    private readonly GlobalHotkeys _hotkeys;
    private SettingsWindow? _window;

    /// <summary>Creates the controller.</summary>
    public SettingsController(AppHost host, GlobalHotkeys hotkeys)
    {
        _host = host;
        _hotkeys = hotkeys;
    }

    /// <summary>Shows the Settings window, or brings it to the front if it's already open.</summary>
    public void Open()
    {
        if (_window != null)
        {
            _window.Activate();
            return;
        }

        // Registered hotkeys are swallowed by Windows before they reach any window, so the hotkey boxes
        // couldn't record them. Released here, registered again (with any new values) when the window closes.
        _hotkeys.Suspend();

        var viewModel = new SettingsViewModel(_host.Settings, StartupRegistration.IsEnabled);
        viewModel.Saved += (_, e) => Apply(e);

        _window = new SettingsWindow(viewModel);
        _window.Closed += (_, _) =>
        {
            _window = null;
            ResumeHotkeys();
        };
        _window.Show();
        _window.Activate();
        Log.Info("Settings window opened.");
    }

    /// <summary>Applies saved settings to the running app, writes them to disk, and updates starting with Windows.</summary>
    private void Apply(SettingsSavedEventArgs e)
    {
        _host.ApplySettings(e.Settings);
        var problems = new List<string>();

        if (!_host.Store.TrySave(_host.Settings, out string? error))
            problems.Add($"Your changes are active now, but couldn't be saved for next time ({error}).");

        try
        {
            if (e.StartWithWindows != StartupRegistration.IsEnabled)
                StartupRegistration.SetEnabled(e.StartWithWindows);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            problems.Add($"\"Start PowerPen when Windows starts\" couldn't be changed ({ex.Message}). " +
                         "Your company may manage startup apps; ask IT, or start PowerPen yourself.");
            Log.Error("Couldn't change start-with-Windows", ex);
        }

        Log.Info("Settings saved.");
        if (problems.Count > 0)
            MessageBox.Show(string.Join("\n\n", problems), "PowerPen settings", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Registers the hotkeys again (with any new values) and reports any that are taken.</summary>
    private void ResumeHotkeys()
    {
        var problems = new List<string>();
        _hotkeys.RegisterAll(problems);
        if (problems.Count > 0)
            MessageBox.Show(string.Join("\n\n", problems), "PowerPen: hotkey problem", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
