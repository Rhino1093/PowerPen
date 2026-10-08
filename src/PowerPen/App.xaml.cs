using System.IO;
using System.Windows;
using System.Windows.Threading;
using PowerPen.Diagnostics;
using PowerPen.Input;
using PowerPen.Interop;
using PowerPen.Settings;
using PowerPen.UI;

namespace PowerPen;

/// <summary>
/// Application entry point. Normally starts PowerPen (one copy at a time). Also runs diagnostics:
/// <c>--selftest [report.txt]</c> and <c>--render-ui [folder]</c>.
/// </summary>
public partial class App : Application
{
    /// <summary>Names shared by every PowerPen process in this Windows session ("Local\" = this signed-in user's session).</summary>
    private const string SingleInstanceName = @"Local\PowerPen.SingleInstance";
    private const string ShowToolbarSignalName = @"Local\PowerPen.ShowToolbar";

    private AppHost? _host;
    private GlobalHotkeys? _hotkeys;
    private TrayIcon? _tray;
    private Mutex? _singleInstance;
    private EventWaitHandle? _showToolbarSignal;
    private RegisteredWaitHandle? _showToolbarWait;
    private bool _isSelfTest;

    /// <summary>PowerPen's version, e.g. "1.0.0".</summary>
    public static string Version { get; } = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>Starts PowerPen, or one of the diagnostic modes.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled error on a background thread", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved background task error", args.Exception);
            args.SetObserved();
        };

        string mode = e.Args.Length > 0 ? e.Args[0].ToLowerInvariant() : "";
        string? argument = e.Args.Length > 1 ? e.Args[1] : null;
        switch (mode)
        {
            case "--selftest":
                _isSelfTest = true;
                RunSelfTest(argument ?? Path.Combine(Path.GetTempPath(), "PowerPen-selftest.txt"));
                return;
            case "--render-ui":
                RenderUi(argument ?? Path.GetTempPath());
                Shutdown();
                return;
        }

        if (!ClaimSingleInstance())
        {
            Shutdown(); // The copy already running was asked to show its toolbar.
            return;
        }

        StartPowerPen();
    }

    /// <summary>Releases everything on exit.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _host?.Dispose();

        _showToolbarWait?.Unregister(null);
        _showToolbarSignal?.Dispose();
        if (_host != null)
            _singleInstance?.ReleaseMutex(); // Only the copy that owns the mutex may release it.
        _singleInstance?.Dispose();

        Log.Info("PowerPen closed.");
        base.OnExit(e);
    }

    /// <summary>Loads settings, shows the overlays, toolbar, and tray icon, and registers hotkeys.</summary>
    private void StartPowerPen()
    {
        Log.IsEnabled = true;
        Log.DeleteOldFiles();
        Log.Info($"PowerPen {Version} starting. Windows {Environment.OSVersion.Version}, .NET {Environment.Version}.");
        foreach (DisplayMonitor monitor in MonitorService.GetMonitors())
            Log.Info($"Monitor: {monitor}");

        // Settings and hotkey problems are collected and shown together, after everything is running.
        var problems = new List<string>();
        AppSettings settings = SettingsService.Load(SettingsService.DefaultPath, problems, out bool canSave);
        foreach (string problem in problems)
            Log.Warn($"Settings: {problem}");

        _host = new AppHost(settings, new SettingsStore(SettingsService.DefaultPath, canSave));
        _host.Start();

        _hotkeys = new GlobalHotkeys(_host.Commands, _host.State, settings);
        _hotkeys.RegisterAll(problems);

        var settingsController = new SettingsController(_host, _hotkeys);
        _host.Commands.SettingsRequested += (_, _) => settingsController.Open();

        _tray = new TrayIcon(_host, Version);

        if (problems.Count > 0)
        {
            MessageBox.Show(
                "PowerPen is running, but found some problems:\n\n" +
                string.Join("\n\n", problems.Select(p => "• " + p)) +
                $"\n\nSettings file:\n{SettingsService.DefaultPath}",
                "PowerPen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Makes sure only one copy of PowerPen runs. Two copies would fight over the hotkeys and stack two sets of
    /// overlays. Launching it again just brings up the toolbar of the copy that's already running.
    /// </summary>
    /// <returns>True if this is the only copy and should start.</returns>
    private bool ClaimSingleInstance()
    {
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out bool isFirstCopy);
        if (!isFirstCopy)
        {
            try
            {
                using EventWaitHandle signal = EventWaitHandle.OpenExisting(ShowToolbarSignalName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The other copy is still starting up or shutting down; nothing to signal.
            }
            return false;
        }

        // Wait (on a thread-pool thread) for later launches to ask us to show the toolbar.
        _showToolbarSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowToolbarSignalName);
        _showToolbarWait = ThreadPool.RegisterWaitForSingleObject(_showToolbarSignal,
            (_, _) => Dispatcher.InvokeAsync(() => _host?.ShowToolbar()), null, Timeout.Infinite, executeOnlyOnce: false);
        return true;
    }

    /// <summary>
    /// Development aid: renders the toolbar (normal, drawing, collapsed) and the Settings window to PNGs in
    /// <paramref name="folder"/> without showing any windows. Only PowerPen's own UI is drawn; nothing is captured.
    /// </summary>
    private static void RenderUi(string folder)
    {
        using var host = new AppHost(new AppSettings(), SettingsStore.InMemory());
        host.Toolbar.SaveSnapshot(Path.Combine(folder, "PowerPen-toolbar.png"));

        host.State.IsDrawMode = true;
        host.State.CurrentTool = Core.ToolType.Arrow;
        host.Toolbar.SaveSnapshot(Path.Combine(folder, "PowerPen-toolbar-active.png"));

        host.ToolbarViewModel.IsCollapsed = true;
        host.Toolbar.SaveSnapshot(Path.Combine(folder, "PowerPen-toolbar-collapsed.png"));

        var settingsWindow = new SettingsWindow(new SettingsViewModel(host.Settings, startWithWindows: false));
        settingsWindow.SaveSnapshot(Path.Combine(folder, "PowerPen-settings.png"));
        settingsWindow.Close();
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

    /// <summary>Logs unexpected errors and tells the user, instead of crashing.</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        // During the self-test a popup would steal focus and break the focus checks; put the error in the report instead.
        if (_isSelfTest)
        {
            SelfTest.UnhandledErrors.Add(e.Exception.ToString());
            return;
        }

        Log.Error("Unexpected error", e.Exception);
        MessageBox.Show(
            $"Something went wrong:\n{e.Exception.Message}\n\nPowerPen will keep running. If this keeps happening, restart PowerPen.\n\n" +
            $"Details were saved to:\n{Log.CurrentFile}",
            "PowerPen error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
