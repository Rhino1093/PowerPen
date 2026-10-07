using System.Windows.Threading;
using Microsoft.Win32;
using PowerPen.Interop;

namespace PowerPen.Overlay;

/// <summary>
/// Creates one <see cref="OverlayWindow"/> per monitor, keeps them all in the same mode,
/// and rebuilds them when monitors are added, removed, or change resolution.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly List<OverlayWindow> _overlays = [];
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _rebuildTimer;

    /// <summary>Creates the manager. Call <see cref="Start"/> to show the overlays.</summary>
    public OverlayManager()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;

        // Windows often fires several display-change events for one physical change (e.g. docking a laptop).
        // Waiting until they stop avoids rebuilding the overlays several times in a row.
        _rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _rebuildTimer.Tick += (_, _) =>
        {
            _rebuildTimer.Stop();
            Rebuild();
        };
    }

    /// <summary>True while the overlays capture mouse input for drawing.</summary>
    public bool IsDrawMode { get; private set; }

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
    /// Ink is cleared (v1 limitation; see PLAN.md §5.2).
    /// </summary>
    public void Rebuild()
    {
        CloseAll();

        foreach (DisplayMonitor monitor in MonitorService.GetMonitors())
        {
            var overlay = new OverlayWindow(monitor);
            overlay.ExitDrawModeRequested += (_, _) => SetDrawMode(false);
            overlay.Show();
            overlay.SetDrawMode(IsDrawMode);
            _overlays.Add(overlay);
        }
    }

    /// <summary>
    /// Flips between Draw and Pointer mode. When entering Draw mode, focuses the overlay under the
    /// mouse so keys like Esc work immediately.
    /// </summary>
    public void ToggleDrawMode()
    {
        SetDrawMode(!IsDrawMode);
        if (IsDrawMode)
            OverlayUnderCursor()?.Activate();
    }

    /// <summary>Puts every overlay into Draw mode (<c>true</c>) or Pointer mode (<c>false</c>) without changing focus.</summary>
    public void SetDrawMode(bool isDrawing)
    {
        IsDrawMode = isDrawing;
        foreach (OverlayWindow overlay in _overlays)
            overlay.SetDrawMode(isDrawing);
    }

    /// <summary>Removes all ink from every monitor.</summary>
    public void ClearAll()
    {
        foreach (OverlayWindow overlay in _overlays)
            overlay.Strokes.Clear();
    }

    /// <summary>Stops watching for monitor changes and closes all overlays.</summary>
    public void Dispose()
    {
        // SystemEvents is static: without unsubscribing, it would keep this object alive and keep calling it.
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _rebuildTimer.Stop();
        CloseAll();
    }

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
