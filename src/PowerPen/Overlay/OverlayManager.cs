using System.ComponentModel;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PowerPen.Core;
using PowerPen.Interop;

namespace PowerPen.Overlay;

/// <summary>
/// Creates one <see cref="OverlayWindow"/> per monitor, keeps them in sync with <see cref="AppState"/>,
/// records their changes in the undo history, and rebuilds them when monitors are added, removed, or change resolution.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly AppState _state;
    private readonly UndoManager _undo;
    private readonly List<OverlayWindow> _overlays = [];
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _rebuildTimer;

    /// <summary>Creates the manager. Call <see cref="Start"/> to show the overlays.</summary>
    public OverlayManager(AppState state, UndoManager undo)
    {
        _state = state;
        _undo = undo;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // Windows often fires several display-change events for one physical change (e.g. docking a laptop).
        // Waiting until they stop avoids rebuilding the overlays several times in a row.
        _rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _rebuildTimer.Tick += (_, _) =>
        {
            _rebuildTimer.Stop();
            Rebuild();
        };

        _state.PropertyChanged += OnStateChanged;
    }

    /// <summary>
    /// Raised when overlays move to the top of the z-order (after being created or clicked).
    /// Windows that must stay above the overlays, like the toolbar, should bring themselves back to the front.
    /// </summary>
    public event EventHandler? OverlaysRaised;

    /// <summary>
    /// Raised for every key pressed on an overlay. Overlays only have keyboard focus in Draw mode, so this is where
    /// Draw-mode shortcuts come from. Handlers set <see cref="RoutedEventArgs.Handled"/> for keys they use.
    /// </summary>
    public event KeyEventHandler? DrawModeKeyDown;

    /// <summary>The current overlays, one per monitor, ordered left to right.</summary>
    public IReadOnlyList<OverlayWindow> Overlays => _overlays;

    /// <summary>Shows an overlay on every monitor and starts watching for monitor changes.</summary>
    public void Start()
    {
        Rebuild();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>
    /// Closes all overlays and creates fresh ones for the current monitor layout.
    /// Ink and undo history are cleared (v1 limitation; see PLAN.md §5.2).
    /// </summary>
    public void Rebuild()
    {
        CloseAll();
        _undo.Clear(); // The old strokes are gone, so history that points at them is meaningless.

        foreach (DisplayMonitor monitor in MonitorService.GetMonitors())
        {
            var overlay = new OverlayWindow(monitor);
            StrokeCollection strokes = overlay.Strokes;

            // Every add/remove of strokes (drawing, erasing, clearing) becomes an undo step.
            strokes.StrokesChanged += (_, e) =>
            {
                if (e.Added.Count > 0 || e.Removed.Count > 0)
                    _undo.Record(new StrokesChangedAction(strokes, e.Added, e.Removed));
            };
            overlay.GestureStarted += (_, _) => _undo.BeginGroup();
            overlay.GestureEnded += (_, _) => _undo.EndGroup();
            // Preview (tunneling) so shortcuts are seen before the InkCanvas, which has its own Delete/Ctrl+C handling.
            overlay.PreviewKeyDown += (sender, e) => DrawModeKeyDown?.Invoke(sender, e);
            overlay.Activated += (_, _) => OverlaysRaised?.Invoke(this, EventArgs.Empty);

            overlay.Show();
            _overlays.Add(overlay);
        }

        ApplyStateToAll();
        OverlaysRaised?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Focuses the overlay on the monitor under the mouse, so it receives keys like Esc.</summary>
    public void ActivateOverlayUnderCursor() => OverlayUnderCursor()?.Activate();

    /// <summary>Removes all ink from every monitor as a single undo step.</summary>
    public void ClearAll()
    {
        _undo.BeginGroup();
        foreach (OverlayWindow overlay in _overlays)
            overlay.Strokes.Clear();
        _undo.EndGroup();
    }

    /// <summary>Stops watching for changes and closes all overlays.</summary>
    public void Dispose()
    {
        // SystemEvents is static: without unsubscribing, it would keep this object alive and keep calling it.
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _state.PropertyChanged -= OnStateChanged;
        _rebuildTimer.Stop();
        CloseAll();
    }

    /// <summary>Pushes the current mode, visibility, tool, color, and size to every overlay.</summary>
    private void ApplyStateToAll()
    {
        DrawingAttributes attributes = InkStyle.Create(_state);
        foreach (OverlayWindow overlay in _overlays)
        {
            // Each overlay gets its own copy so a change on one monitor can never leak into another.
            overlay.ApplyState(_state.IsDrawMode, _state.IsInkVisible, _state.CurrentTool, attributes.Clone());
        }
    }

    /// <summary>Re-applies state whenever the user changes mode, visibility, tool, color, or size.</summary>
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => ApplyStateToAll();

    /// <summary>Returns the overlay on the monitor that currently contains the mouse cursor, if any.</summary>
    private OverlayWindow? OverlayUnderCursor()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
            return null;

        return _overlays.FirstOrDefault(o => o.TargetMonitor.Bounds.Contains(cursor.X, cursor.Y));
    }

    /// <summary>Restarts the rebuild timer whenever Windows reports a monitor change.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // SystemEvents can raise this on a background thread; WPF objects must only be touched on the UI thread.
        _dispatcher.InvokeAsync(() =>
        {
            _rebuildTimer.Stop();
            _rebuildTimer.Start();
        });
    }

    /// <summary>Closes and forgets every overlay.</summary>
    private void CloseAll()
    {
        foreach (OverlayWindow overlay in _overlays)
            overlay.Close();
        _overlays.Clear();
    }
}
