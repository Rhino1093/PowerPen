using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PowerPen.Core;
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
    private bool _isClickThrough;

    /// <summary>Creates an overlay for <paramref name="monitor"/>. Call <see cref="Window.Show"/> to display it.</summary>
    public OverlayWindow(DisplayMonitor monitor)
    {
        InitializeComponent();
        TargetMonitor = monitor;

        // Bigger than the default eraser so a quick swipe reliably catches thin strokes.
        Ink.EraserShape = new EllipseStylusShape(16, 16);

        // One mouse press-to-release is one "gesture"; the undo history groups everything in it into one step.
        // The release handler uses handledEventsToo because InkCanvas marks mouse-up as handled.
        PreviewMouseLeftButtonDown += (_, _) => GestureStarted?.Invoke(this, EventArgs.Empty);
        AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => GestureEnded?.Invoke(this, EventArgs.Empty)), handledEventsToo: true);
        Ink.LostMouseCapture += (_, _) => GestureEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the user presses Esc while drawing, asking to go back to Pointer mode.</summary>
    public event EventHandler? ExitDrawModeRequested;

    /// <summary>Raised when the mouse button goes down on this overlay.</summary>
    public event EventHandler? GestureStarted;

    /// <summary>Raised when the mouse button is released (or capture is lost) on this overlay.</summary>
    public event EventHandler? GestureEnded;

    /// <summary>The monitor this overlay covers.</summary>
    public DisplayMonitor TargetMonitor { get; }

    /// <summary>Native window handle. Valid once the window has been shown.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>The strokes drawn on this monitor.</summary>
    public StrokeCollection Strokes => Ink.Strokes;

    /// <summary>
    /// Applies the current mode and tool. In Draw mode the overlay catches the mouse and uses
    /// <paramref name="tool"/>; in Pointer mode the ink stays visible and clicks pass through.
    /// </summary>
    /// <param name="isDrawing">True for Draw mode, false for Pointer mode.</param>
    /// <param name="tool">The tool to use in Draw mode.</param>
    /// <param name="attributes">Color/width settings for new strokes. Must be a copy this overlay can own.</param>
    public void ApplyState(bool isDrawing, ToolType tool, DrawingAttributes attributes)
    {
        Ink.DefaultDrawingAttributes = attributes;
        Ink.EditingMode = isDrawing ? InkStyle.EditingModeFor(tool) : InkCanvasEditingMode.None;
        Ink.Background = isDrawing ? HitTestableBrush : Brushes.Transparent;
        SetClickThrough(!isDrawing);
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
        SetClickThrough(true); // Start in Pointer mode so a new overlay never blocks clicks by accident.

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

    /// <summary>Turns mouse pass-through on or off. Skips the Win32 call when nothing changes.</summary>
    private void SetClickThrough(bool enabled)
    {
        if (_hwnd == IntPtr.Zero || _isClickThrough == enabled)
            return;

        // WS_EX_TRANSPARENT makes the whole window click-through, including pixels that have ink on them.
        if (enabled)
            NativeMethods.AddExtendedStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT);
        else
            NativeMethods.RemoveExtendedStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT);
        _isClickThrough = enabled;
    }

    /// <summary>Creates a frozen (read-only, thread-safe, faster to render) solid brush.</summary>
    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
