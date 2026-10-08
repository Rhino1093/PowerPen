using System.ComponentModel;
using System.Windows.Input;
using PowerPen.Capture;
using PowerPen.Core;
using PowerPen.Diagnostics;
using PowerPen.Overlay;
using PowerPen.Settings;
using PowerPen.UI;

namespace PowerPen;

/// <summary>
/// Builds PowerPen's main objects and connects them. The app and the self-test both use this,
/// so the self-test always checks exactly the same wiring the user gets.
/// </summary>
public sealed class AppHost : IDisposable
{
    /// <summary>Creates and wires everything. Call <see cref="Start"/> to show it.</summary>
    /// <param name="settings">Validated settings. This one object is shared by the whole app.</param>
    /// <param name="store">Where settings are saved (an in-memory store for the self-test).</param>
    public AppHost(AppSettings settings, SettingsStore store)
    {
        Settings = settings;
        Store = store;
        State = new AppState(settings);
        Undo = new UndoManager();
        Overlays = new OverlayManager(State, Undo);
        Screenshots = new ScreenshotService(settings);
        Commands = new AppCommands(State, Overlays, Undo, Screenshots);
        ToolbarViewModel = new ToolbarViewModel(State, Commands, Undo, settings);
        Toolbar = new ToolbarWindow(ToolbarViewModel)
        {
            HideFromCapture = settings.HideToolbarFromCapture,
            InitialPosition = settings.Toolbar.X is int x && settings.Toolbar.Y is int y ? (x, y) : null,
        };

        // Overlays jump above the toolbar whenever they're shown or clicked; put the toolbar back on top.
        Overlays.OverlaysRaised += (_, _) => Toolbar.BringToFront();

        // A monitor was unplugged or rearranged: make sure the toolbar didn't end up off-screen.
        Overlays.MonitorsChanged += (_, _) => Toolbar.EnsureOnScreen();

        Overlays.DrawModeKeyDown += (_, e) =>
        {
            // While Alt is held, WPF reports the real key in SystemKey.
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (Commands.HandleDrawModeKey(key, Keyboard.Modifiers))
                e.Handled = true;
        };

        // Remember where the toolbar was left, and whether it was collapsed.
        Toolbar.Moved += (_, _) =>
        {
            (Settings.Toolbar.X, Settings.Toolbar.Y) = Toolbar.Position;
            SaveSettings();
        };
        ToolbarViewModel.PropertyChanged += OnToolbarViewModelChanged;
    }

    /// <summary>The settings in use (one shared object; see <see cref="ApplySettings"/>).</summary>
    public AppSettings Settings { get; }

    /// <summary>Where settings are saved.</summary>
    public SettingsStore Store { get; }

    /// <summary>The user's current selections.</summary>
    public AppState State { get; }

    /// <summary>App-wide undo history.</summary>
    public UndoManager Undo { get; }

    /// <summary>The per-monitor ink overlays.</summary>
    public OverlayManager Overlays { get; }

    /// <summary>The 📷 flow.</summary>
    public ScreenshotService Screenshots { get; }

    /// <summary>Every user action.</summary>
    public AppCommands Commands { get; }

    /// <summary>What the toolbar shows.</summary>
    public ToolbarViewModel ToolbarViewModel { get; }

    /// <summary>The floating toolbar.</summary>
    public ToolbarWindow Toolbar { get; }

    /// <summary>Shows the overlays and the toolbar.</summary>
    public void Start()
    {
        Overlays.Start();
        Toolbar.Show();
        Toolbar.BringToFront();
    }

    /// <summary>Brings the full toolbar back into view (tray icon click, or launching PowerPen a second time).</summary>
    public void ShowToolbar()
    {
        ToolbarViewModel.IsCollapsed = false;
        Toolbar.EnsureOnScreen();
        Toolbar.BringToFront();
    }

    /// <summary>
    /// Applies new settings from the Settings window to the running app. The shared settings object is updated in place,
    /// so every part that reads it (screenshots, hotkeys, tooltips) sees the new values.
    /// </summary>
    public void ApplySettings(AppSettings updated)
    {
        // Keep the toolbar's current position: it may have been moved while the Settings window was open.
        ToolbarSettings toolbar = Settings.Toolbar;
        Settings.CopyFrom(updated);
        Settings.Toolbar = toolbar;

        State.ApplySettings(Settings);
        Toolbar.HideFromCapture = Settings.HideToolbarFromCapture;
        ToolbarViewModel.RefreshFromSettings();
    }

    /// <summary>Closes the toolbar and the overlays.</summary>
    public void Dispose()
    {
        Toolbar.Close();
        Overlays.Dispose();
    }

    /// <summary>Saves the collapsed/expanded state when it changes.</summary>
    private void OnToolbarViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ToolbarViewModel.IsCollapsed) || Settings.Toolbar.Collapsed == ToolbarViewModel.IsCollapsed)
            return;

        Settings.Toolbar.Collapsed = ToolbarViewModel.IsCollapsed;
        SaveSettings();
    }

    /// <summary>Saves settings quietly (toolbar position and state); failures are only logged.</summary>
    private void SaveSettings()
    {
        if (Store.CanSave && !Store.TrySave(Settings, out string? error))
            Log.Warn($"Toolbar position not saved: {error}");
    }
}
