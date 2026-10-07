using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PowerPen.Diagnostics;
using PowerPen.Input;
using PowerPen.Settings;

namespace PowerPen;

/// <summary>
/// Application entry point: loads settings, starts PowerPen and its global hotkeys, or runs the self-test
/// when started with <c>--selftest</c>.
/// </summary>
public partial class App : Application
{
    private AppHost? _host;
    private HotkeyService? _hotkeys;
    private bool _isSelfTest;

    /// <summary>Starts PowerPen in normal or self-test mode.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        if (e.Args.Length > 0 && e.Args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            string reportPath = e.Args.Length > 1 ? e.Args[1] : Path.Combine(Path.GetTempPath(), "PowerPen-selftest.txt");
            _isSelfTest = true;
            RunSelfTest(reportPath);
            return;
        }

        if (e.Args.Length > 0 && e.Args[0].Equals("--render-toolbar", StringComparison.OrdinalIgnoreCase))
        {
            string imagePath = e.Args.Length > 1 ? e.Args[1] : Path.Combine(Path.GetTempPath(), "PowerPen-toolbar.png");
            RenderToolbar(imagePath);
            Shutdown();
            return;
        }

        // Settings and hotkey problems are collected and shown together, after everything is running.
        var problems = new List<string>();
        AppSettings settings = SettingsService.Load(SettingsService.DefaultPath, problems);

        _host = new AppHost(settings);
        _host.Start();

        _hotkeys = new HotkeyService();
        RegisterGlobalHotkeys(settings.Hotkeys, problems);

        if (problems.Count > 0)
        {
            MessageBox.Show(
                "PowerPen is running, but found some problems:\n\n" +
                string.Join("\n\n", problems.Select(p => "• " + p)) +
                $"\n\nSettings file:\n{SettingsService.DefaultPath}",
                "PowerPen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Releases hotkeys and closes all windows on exit.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _host?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Registers every global hotkey from settings, describing any that fail in <paramref name="problems"/>.</summary>
    private void RegisterGlobalHotkeys(HotkeySettings hotkeys, List<string> problems)
    {
        Core.AppCommands commands = _host!.Commands;

        Register("toggleDraw", "switch between Draw and Pointer mode", hotkeys.ToggleDraw, commands.ToggleDrawMode);
        Register("toggleInkVisible", "hide or show ink", hotkeys.ToggleInkVisible, commands.ToggleInkVisible);
        Register("clearAll", "clear all ink", hotkeys.ClearAll, commands.ClearAll);
        Register("quit", "quit PowerPen", hotkeys.Quit, commands.Quit);

        // Color hotkeys: modifiers + 1..N. Failures are grouped into one message instead of up to eight.
        if (Hotkey.TryParseModifiers(hotkeys.ColorModifiers, out ModifierKeys colorModifiers, out _))
        {
            var failed = new List<string>();
            for (int i = 0; i < _host.State.Palette.Count; i++)
            {
                int colorIndex = i; // Each lambda needs its own copy, not the shared loop variable.
                var hotkey = new Hotkey(colorModifiers, Key.D1 + i);
                if (!_hotkeys!.TryRegister(hotkey, () => commands.SelectColor(colorIndex), out _))
                    failed.Add(hotkey.ToString());
            }

            if (failed.Count > 0)
            {
                problems.Add($"{string.Join(", ", failed)} (pen colors) {(failed.Count == 1 ? "is" : "are")} already used by " +
                    "another app or another copy of PowerPen. To fix: change \"hotkeys.colorModifiers\" in the settings file " +
                    "(for example to Ctrl+Alt+Shift), then restart PowerPen.");
            }
        }

        // Registers one named hotkey. Empty means the user turned it off.
        void Register(string settingName, string purpose, string combo, Action action)
        {
            if (!Hotkey.TryParse(combo, out Hotkey hotkey, out _))
                return; // Empty, or invalid (already reported and replaced during validation).

            if (_hotkeys!.TryRegister(hotkey, action, out HotkeyError error))
                return;

            problems.Add(error == HotkeyError.AlreadyInUse
                ? $"{hotkey} ({purpose}) is already used by another app or another copy of PowerPen. " +
                  $"To fix: close the other app, or change \"hotkeys.{settingName}\" in the settings file, then restart PowerPen."
                : $"Windows wouldn't let PowerPen use {hotkey} ({purpose}). " +
                  $"To fix: restart PowerPen. If it keeps happening, change \"hotkeys.{settingName}\" in the settings file.");
        }
    }

    /// <summary>
    /// Development aid: renders the toolbar to two PNGs (Pointer mode, and drawing with the Arrow) without
    /// showing any windows, to check the layout. Only the toolbar is drawn; nothing from the screen is captured.
    /// </summary>
    private static void RenderToolbar(string imagePath)
    {
        using var host = new AppHost(new AppSettings());
        host.Toolbar.SaveSnapshot(imagePath);

        host.State.IsDrawMode = true;
        host.State.CurrentTool = Core.ToolType.Arrow;
        host.Toolbar.SaveSnapshot(Path.ChangeExtension(imagePath, null) + "-active.png");
    }

    /// <summary>Runs the self-test, then exits with code 0 (all passed) or 1 (failures or crash).</summary>
    private async void RunSelfTest(string reportPath)
    {
        bool passed = false;
        try
        {
            passed = await SelfTest.RunAsync(reportPath);
        }
        catch (Exception ex)
        {
            File.WriteAllText(reportPath, $"Self-test crashed:\n{ex}");
        }
        finally
        {
            Shutdown(passed ? 0 : 1);
        }
    }

    /// <summary>Shows unexpected errors to the user instead of crashing.</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        // During the self-test a popup would steal focus and break the focus checks; put the error in the report instead.
        if (_isSelfTest)
        {
            SelfTest.UnhandledErrors.Add(e.Exception.ToString());
            return;
        }

        // Message only for now. Phase 5 adds a log file under %AppData%\PowerPen\logs.
        MessageBox.Show(
            $"Something went wrong:\n{e.Exception.Message}\n\nPowerPen will keep running. If this keeps happening, restart PowerPen.",
            "PowerPen error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
