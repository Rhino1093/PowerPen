using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PowerPen.Interop;

namespace PowerPen.UI;

/// <summary>
/// The floating toolbar. It never takes focus (so your app, or the overlay while drawing, keeps the keyboard)
/// and is kept above the overlays so it stays clickable.
/// </summary>
public partial class ToolbarWindow : Window
{
    private IntPtr _hwnd;

    /// <summary>Creates the toolbar bound to <paramref name="viewModel"/>.</summary>
    public ToolbarWindow(ToolbarViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Native window handle. Valid once the window has been shown.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>Center of the Pen button in physical screen pixels. Used by the self-test to click it.</summary>
    internal Point PenButtonScreenCenter() =>
        PenButton.PointToScreen(new Point(PenButton.ActualWidth / 2, PenButton.ActualHeight / 2));

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

    /// <summary>Configures the native window before it first appears.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // NOACTIVATE: clicking a button must not steal focus from the app you're presenting,
        // or from the overlay while drawing (which needs it for Esc).
        // TOOLWINDOW: keep the toolbar out of Alt+Tab.
        NativeMethods.AddExtendedStyle(_hwnd, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

        PlaceAtDefaultPosition();
        // WPF may still move the window while it finishes showing it; place it again once that's done.
        Dispatcher.InvokeAsync(PlaceAtDefaultPosition, DispatcherPriority.Loaded);
    }

    /// <summary>Puts the toolbar near the top-left of the main monitor.</summary>
    private void PlaceAtDefaultPosition()
    {
        // Phase 5 remembers the last position. Until then, always start at the same predictable spot.
        IReadOnlyList<DisplayMonitor> monitors = MonitorService.GetMonitors();
        DisplayMonitor? main = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        if (main == null)
            return;

        PixelRect area = main.WorkArea;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            area.X + (int)(16 * main.Scale), area.Y + (int)(120 * main.Scale), 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Lets the user drag the toolbar by its handle.</summary>
    private void OnDragHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
