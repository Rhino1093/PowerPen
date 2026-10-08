using System.Windows.Input;
using PowerPen.Core;
using PowerPen.Diagnostics;
using PowerPen.Settings;

namespace PowerPen.Input;

/// <summary>
/// Registers PowerPen's system-wide hotkeys from settings. They can be paused (while the Settings window records new
/// ones) and re-registered after settings change, without restarting.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private readonly HotkeyService _service = new();
    private readonly AppCommands _commands;
    private readonly AppState _state;
    private readonly AppSettings _settings;

    /// <summary>Creates the hotkey set. Nothing is registered until <see cref="RegisterAll"/>.</summary>
    /// <param name="commands">What the hotkeys do.</param>
    /// <param name="state">Current palette (decides how many color hotkeys exist).</param>
    /// <param name="settings">The shared settings; read again on every <see cref="RegisterAll"/>.</param>
    public GlobalHotkeys(AppCommands commands, AppState state, AppSettings settings)
    {
        _commands = commands;
        _state = state;
        _settings = settings;
    }

    /// <summary>
    /// (Re-)registers every hotkey from the current settings, describing any that fail in <paramref name="problems"/>.
    /// </summary>
    public void RegisterAll(List<string> problems)
    {
        _service.UnregisterAll();
        HotkeySettings hotkeys = _settings.Hotkeys;

        Register("Draw / Pointer mode", hotkeys.ToggleDraw, _commands.ToggleDrawMode, problems);
        Register("Hide / show ink", hotkeys.ToggleInkVisible, _commands.ToggleInkVisible, problems);
        Register("Clear all ink", hotkeys.ClearAll, _commands.ClearAll, problems);
        Register("Quit PowerPen", hotkeys.Quit, _commands.Quit, problems);

        // Color hotkeys: modifiers + 1..N. Failures are grouped into one message instead of up to eight.
        if (Hotkey.TryParseModifiers(hotkeys.ColorModifiers, out ModifierKeys colorModifiers, out _))
        {
            var failed = new List<string>();
            for (int i = 0; i < _state.Palette.Count; i++)
            {
                int colorIndex = i; // Each lambda needs its own copy, not the shared loop variable.
                var hotkey = new Hotkey(colorModifiers, Key.D1 + i);
                if (!_service.TryRegister(hotkey, () => _commands.SelectColor(colorIndex), out _))
                    failed.Add(hotkey.ToString());
            }

            if (failed.Count > 0)
            {
                string message = $"{string.Join(", ", failed)} (pen colors) {(failed.Count == 1 ? "is" : "are")} already used by " +
                    "another app. To fix: open Settings (⚙ on the toolbar) and change the color hotkeys, " +
                    "for example to Ctrl+Alt+Shift.";
                problems.Add(message);
                Log.Warn(message);
            }
        }

        Log.Info($"Hotkeys registered ({problems.Count} problem(s)).");
    }

    /// <summary>Releases every hotkey until the next <see cref="RegisterAll"/>.</summary>
    public void Suspend() => _service.UnregisterAll();

    /// <summary>Releases every hotkey for good.</summary>
    public void Dispose() => _service.Dispose();

    /// <summary>Registers one named hotkey. Empty means the user turned it off.</summary>
    private void Register(string name, string combo, Action action, List<string> problems)
    {
        if (!Hotkey.TryParse(combo, out Hotkey hotkey, out _))
            return; // Empty, or invalid (already reported and replaced during validation).

        if (_service.TryRegister(hotkey, action, out HotkeyError error))
            return;

        string message = error == HotkeyError.AlreadyInUse
            ? $"{hotkey} ({name}) is already used by another app. " +
              $"To fix: open Settings (⚙ on the toolbar) and pick a different combination for \"{name}\"."
            : $"Windows wouldn't let PowerPen use {hotkey} ({name}). " +
              $"To fix: restart PowerPen. If it keeps happening, pick a different combination in Settings.";
        problems.Add(message);
        Log.Warn(message);
    }
}
