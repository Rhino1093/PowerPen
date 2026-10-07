using System.Windows.Input;
using PowerPen.Capture;
using PowerPen.Core;
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
    /// <param name="settings">Validated settings.</param>
    public AppHost(AppSettings settings)
    {
        Settings = settings;
        State = new AppState(settings);
        Undo = new UndoManager();
        Overlays = new OverlayManager(State, Undo);
        Screenshots = new ScreenshotService(settings);
        Commands = new AppCommands(State, Overlays, Undo, Screenshots);
        Toolbar = new ToolbarWindow(new ToolbarViewModel(State, Commands, Undo, settings))
        {
            HideFromCapture = settings.HideToolbarFromCapture,
        };

        // Overlays jump above the toolbar whenever they're shown or clicked; put the toolbar back on top.
        Overlays.OverlaysRaised += (_, _) => Toolbar.BringToFront();

        Overlays.DrawModeKeyDown += (_, e) =>
        {
            // While Alt is held, WPF reports the real key in SystemKey.
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (Commands.HandleDrawModeKey(key, Keyboard.Modifiers))
                e.Handled = true;
        };
    }

    /// <summary>The settings PowerPen started with.</summary>
    public AppSettings Settings { get; }

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

    /// <summary>The floating toolbar.</summary>
    public ToolbarWindow Toolbar { get; }

    /// <summary>Shows the overlays and the toolbar.</summary>
    public void Start()
    {
        Overlays.Start();
        Toolbar.Show();
        Toolbar.BringToFront();
    }

    /// <summary>Closes the toolbar and the overlays.</summary>
    public void Dispose()
    {
        Toolbar.Close();
        Overlays.Dispose();
    }
}
