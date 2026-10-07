using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PowerPen.Diagnostics;
using PowerPen.Input;

namespace PowerPen;

/// <summary>
/// Application entry point: starts PowerPen and its global hotkeys, or runs the self-test when started with <c>--selftest</c>.
/// </summary>
public partial class App : Application
{
    private AppHost? _host;
    private HotkeyService? _hotkeys;

    /// <summary>Starts PowerPen in normal or self-test mode.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        if (e.Args.Length > 0 && e.Args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            string reportPath = e.Args.Length > 1 ? e.Args[1] : Path.Combine(Path.GetTempPath(), "PowerPen-selftest.txt");
            RunSelfTest(reportPath);
            return;
        }

        _host = new AppHost();
        _host.Start();

        _hotkeys = new HotkeyService();
        var problems = new List<string>();
        RegisterHotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.D, _host.Commands.ToggleDrawMode, problems);
        RegisterHotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.C, _host.Commands.ClearAll, problems);
        // Temporary: the only way to quit until the tray icon's "Exit" arrives in Phase 5.
        RegisterHotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.Q, _host.Commands.Quit, problems);

        // Show all hotkey problems in one message instead of one popup per hotkey.
        if (problems.Count > 0)
            MessageBox.Show(string.Join("\n\n", problems), "PowerPen: hotkey problem", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Releases hotkeys and closes all windows on exit.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _host?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Registers one global hotkey, collecting a user-friendly message if it fails.</summary>
    private void RegisterHotkey(ModifierKeys modifiers, Key key, Action action, List<string> problems)
    {
        if (!_hotkeys!.TryRegister(modifiers, key, action, out string? error))
            problems.Add(error!);
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
        // Message only for now. Phase 5 adds a log file under %AppData%\PowerPen\logs.
        MessageBox.Show(
            $"Something went wrong:\n{e.Exception.Message}\n\nPowerPen will keep running. If this keeps happening, restart PowerPen.",
            "PowerPen error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
