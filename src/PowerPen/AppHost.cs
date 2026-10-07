using PowerPen.Core;
using PowerPen.Overlay;
using PowerPen.UI;

namespace PowerPen;

/// <summary>
/// Builds PowerPen's main objects and connects them. The app and the self-test both use this,
/// so the self-test always checks exactly the same wiring the user gets.
/// </summary>
public sealed class AppHost : IDisposable
{
    /// <summary>Creates and wires everything. Call <see cref="Start"/> to show it.</summary>
    public AppHost()
    {
        State = new AppState();
        Undo = new UndoManager();
        Overlays = new OverlayManager(State, Undo);
        Commands = new AppCommands(State, Overlays, Undo);
        Toolbar = new ToolbarWindow(new ToolbarViewModel(State, Commands, Undo));

        // Overlays jump above the toolbar whenever they're shown or clicked; put the toolbar back on top.
        Overlays.OverlaysRaised += (_, _) => Toolbar.BringToFront();
        Overlays.ExitDrawModeRequested += (_, _) => Commands.EnterPointerMode();
    }

    /// <summary>The user's current selections.</summary>
    public AppState State { get; }

    /// <summary>App-wide undo history.</summary>
    public UndoManager Undo { get; }

    /// <summary>The per-monitor ink overlays.</summary>
    public OverlayManager Overlays { get; }

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
