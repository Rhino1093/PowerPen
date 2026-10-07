using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PowerPen.Interop;

namespace PowerPen.Overlay;

/// <summary>
/// A transparent, always-on-top window that exactly covers one monitor and holds that monitor's ink.
/// PowerPen creates one per monitor (see <see cref="OverlayManager"/>) so each renders at its own monitor's scaling.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>
    /// Nearly invisible background used in Draw mode. Windows sends clicks straight through pixels that are
    /// 100% transparent, so the surface needs an alpha of 1 (out of 255) to catch the mouse while still looking clear.
    /// </summary>
    private static readonly Brush HitTestableBrush = CreateFrozenBrush(Color.FromArgb(1, 0, 0, 0));

    private IntPtr _hwnd;

    /// <summary>Creates an overlay for <paramref name="monitor"/>. Call <see cref="Window.Show"/> to display it.</summary>
    public OverlayWindow(DisplayMonitor monitor)
    {
        InitializeComponent();
        TargetMonitor = monitor;

        // Phase 0: fixed red pen. The toolbar and AppState take over tool/color selection in Phase 1.
        Ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Colors.Red,
            Width = 4,
            Height = 4,
            FitToCurve = true, // Smooths mouse jitter into curves.
        };
    }

    /// <summary>Raised when the user presses Esc while drawing, asking to go back to Pointer mode.</summary>
    public event EventHandler? ExitDrawModeRequested;

    /// <summary>The monitor this overlay covers.</summary>
    public DisplayMonitor TargetMonitor { get; }

    /// <summary>Native window handle. Valid once the window has been shown.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>The strokes drawn on this monitor.</summary>
    public StrokeCollection Strokes => Ink.Strokes;

    /// <summary>
    /// Switches between Draw mode (overlay catches mouse input) and Pointer mode (ink stays visible,
    /// clicks pass through to the apps underneath).
    /// </summary>
    public void SetDrawMode(bool isDrawing)
    {
        if (isDrawing)
        {
            NativeMethods.RemoveExtendedStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT);
            Ink.Background = HitTestableBrush;
            Ink.EditingMode = InkCanvasEditingMode.Ink;
        }
        else
        {
            Ink.EditingMode = InkCanvasEditingMode.None;
            Ink.Background = Brushes.Transparent;
            // WS_EX_TRANSPARENT makes the whole window click-through, including pixels that have ink on them.
            NativeMethods.AddExtendedStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT);
        }
    }

    /// <summary>Moves and sizes the window to exactly cover its monitor, using physical pixels.</summary>
    public void ApplyMonitorBounds()
    {
        PixelRect b = TargetMonitor.Bounds;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Configures the native window before it first appears on screen.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        NativeMethods.AddExtendedStyle(_hwnd, NativeMethods.WS_EX_TOOLWINDOW);
        SetDrawMode(false);

        // Position in physical pixels instead of WPF's Left/Top: WPF units depend on DPI,
        // which is ambiguous before the window knows which monitor it's on.
        ApplyMonitorBounds();

        // WPF can still adjust the size while it finishes showing the window; snap back once it's done.
        Dispatcher.InvokeAsync(ApplyMonitorBounds, DispatcherPriority.Loaded);
    }

    /// <summary>Re-applies the exact monitor bounds after the window lands on a monitor with different scaling.</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // On a DPI change WPF resizes the window to Windows' "suggested" size, which is not the exact monitor size.
        // Queue the fix at low priority so it runs after WPF has finished its own resize.
        Dispatcher.InvokeAsync(ApplyMonitorBounds, DispatcherPriority.Background);
    }

    /// <summary>Handles Esc to leave Draw mode.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            ExitDrawModeRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    /// <summary>Creates a frozen (read-only, thread-safe, faster to render) solid brush.</summary>
    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
