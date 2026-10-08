using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PowerPen.Diagnostics;
using PowerPen.Interop;

namespace PowerPen.UI;

/// <summary>
/// The floating toolbar. It never takes focus (so your app, or the overlay while drawing, keeps the keyboard)
/// and is kept above the overlays so it stays clickable.
/// </summary>
public partial class ToolbarWindow : Window
{
    private IntPtr _hwnd;
    private bool _hideFromCapture = true;

    /// <summary>Creates the toolbar bound to <paramref name="viewModel"/>.</summary>
    public ToolbarWindow(ToolbarViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Raised after the user finishes dragging the toolbar (so its position can be remembered).</summary>
    public event EventHandler? Moved;

    /// <summary>Native window handle. Valid once the window has been shown.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>Where to open the toolbar (top-left, physical pixels), or null for the default spot.</summary>
    public (int X, int Y)? InitialPosition { get; init; }

    /// <summary>The toolbar's current top-left corner in physical pixels.</summary>
    public (int X, int Y) Position
    {
        get
        {
            NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT rect);
            return (rect.Left, rect.Top);
        }
    }

    /// <summary>
    /// Keep the toolbar out of screenshots and screen shares (Teams, Zoom). Can be changed at any time.
    /// </summary>
    public bool HideFromCapture
    {
        get => _hideFromCapture;
        set
        {
            _hideFromCapture = value;
            ApplyCaptureExclusion();
        }
    }

    /// <summary>
    /// Moves the toolbar back to the top of the always-on-top windows. Needed because an overlay jumps above
    /// it whenever the overlay is shown or clicked, which would leave the toolbar visible but unclickable.
    /// </summary>
    public void BringToFront()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// If the toolbar is no longer on any connected monitor (e.g. its monitor was unplugged), moves it back to the
    /// default spot so it can't get lost.
    /// </summary>
    public void EnsureOnScreen()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        (int x, int y) = Position;
        if (!IsOnAnyMonitor(x, y))
            PlaceAtDefaultPosition();
    }

    /// <summary>Center of a named button in physical screen pixels. Used by the self-test to click it.</summary>
    /// <param name="buttonName">The button's x:Name, e.g. "PenButton".</param>
    internal Point ButtonScreenCenter(string buttonName)
    {
        var button = (FrameworkElement)FindName(buttonName);
        return button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
    }

    /// <summary>Renders the toolbar to a PNG without showing it (development aid; see <see cref="VisualSnapshot"/>).</summary>
    internal void SaveSnapshot(string path)
    {
        Root.DataContext ??= DataContext;
        VisualSnapshot.Save(Root, path);
    }

    /// <summary>Configures the native window before it first appears.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // NOACTIVATE: clicking a button must not steal focus from the app you're presenting,
        // or from the overlay while drawing (which needs it for keys and typing).
        // TOOLWINDOW: keep the toolbar out of Alt+Tab.
        NativeMethods.AddExtendedStyle(_hwnd, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        ApplyCaptureExclusion();

        PlaceInitially();
        // WPF may still move the window while it finishes showing it; place it again once that's done.
        Dispatcher.InvokeAsync(PlaceInitially, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Asks Windows to leave the toolbar out of every capture (our screenshots, Snipping Tool, Teams/Zoom sharing)
    /// while still showing it on the monitor, or to include it again. No hide-and-flash needed for screenshots.
    /// </summary>
    private void ApplyCaptureExclusion()
    {
        if (_hwnd != IntPtr.Zero)
            NativeMethods.SetWindowDisplayAffinity(_hwnd, _hideFromCapture ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);
    }

    /// <summary>Opens where the user last left the toolbar if that spot is still on a monitor; otherwise at the default spot.</summary>
    private void PlaceInitially()
    {
        if (InitialPosition is (int x, int y) && IsOnAnyMonitor(x, y))
        {
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
        else
        {
            PlaceAtDefaultPosition();
        }
    }

    /// <summary>Puts the toolbar near the top-left of the main monitor.</summary>
    private void PlaceAtDefaultPosition()
    {
        IReadOnlyList<DisplayMonitor> monitors = MonitorService.GetMonitors();
        DisplayMonitor? main = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        if (main == null)
            return;

        PixelRect area = main.WorkArea;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            area.X + (int)(16 * main.Scale), area.Y + (int)(120 * main.Scale), 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// True if the toolbar's grip (just inside its top-left corner) would be on a connected monitor,
    /// so the user can always grab it.
    /// </summary>
    private static bool IsOnAnyMonitor(int x, int y) =>
        MonitorService.GetMonitors().Any(m => m.WorkArea.Contains(x + 20, y + 10));

    /// <summary>Lets the user drag the toolbar by its handle, then reports the new position.</summary>
    private void OnDragHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove(); // Returns when the mouse button is released.
        Moved?.Invoke(this, EventArgs.Empty);
    }
}
