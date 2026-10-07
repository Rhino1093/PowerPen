using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PowerPen.Interop;
using PowerPen.Overlay;

namespace PowerPen.Capture;

/// <summary>
/// Full-screen window on one monitor showing the frozen screenshot, dimmed, where the user drags out an area,
/// clicks to take the whole screen, or presses Esc (or right-clicks) to cancel.
/// </summary>
/// <remarks>
/// Showing the frozen image (instead of the live screen) guarantees the saved picture is exactly what the user saw
/// when they clicked, and that this window itself can never end up in the picture.
/// </remarks>
public partial class RegionSelectWindow : Window
{
    private readonly CapturedScreen _screen;
    private IntPtr _hwnd;
    private Point? _dragStart;

    /// <summary>Creates the selection window for one captured monitor.</summary>
    public RegionSelectWindow(CapturedScreen screen)
    {
        InitializeComponent();
        _screen = screen;
        Frozen.Source = screen.Image;
        Root.SizeChanged += (_, _) => ShowSelection(null);
    }

    /// <summary>Raised when the user picks an area (or clicks for the whole screen).</summary>
    public event EventHandler<ScreenRegion>? RegionSelected;

    /// <summary>Raised when the user presses Esc or right-clicks.</summary>
    public event EventHandler? Cancelled;

    /// <summary>The monitor this window covers.</summary>
    public DisplayMonitor TargetMonitor => _screen.Monitor;

    /// <summary>Native window handle. Valid once the window has been shown.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>Places the window over its monitor (in physical pixels) before it first appears.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExtendedStyle(_hwnd, NativeMethods.WS_EX_TOOLWINDOW);

        ApplyMonitorBounds();
        // WPF can still adjust the size while it finishes showing the window; snap back once it's done.
        Dispatcher.InvokeAsync(ApplyMonitorBounds, DispatcherPriority.Loaded);
    }

    /// <summary>Re-applies the exact monitor bounds after a scaling change (same reason as the overlays).</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.InvokeAsync(ApplyMonitorBounds, DispatcherPriority.Background);
    }

    /// <summary>Starts a selection.</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragStart = e.GetPosition(Root);
        CaptureMouse();
        Hint.Visibility = Visibility.Collapsed; // Out of the way of the area being picked.
        e.Handled = true;
    }

    /// <summary>Grows the selection with the mouse.</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is Point start)
            ShowSelection(new Rect(start, e.GetPosition(Root)));
    }

    /// <summary>Finishes the selection: a drag picks that area, a click picks the whole screen.</summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragStart is not Point start)
            return;

        _dragStart = null;
        ReleaseMouseCapture();
        RegionSelected?.Invoke(this, new ScreenRegion(_screen, ToPixels(start, e.GetPosition(Root))));
        e.Handled = true;
    }

    /// <summary>Right-click cancels, a common convention in screenshot tools.</summary>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        Cancelled?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <summary>Esc cancels.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Converts a drag (WPF units) to a pixel area of the captured image. A click or tiny drag means "this whole screen".
    /// </summary>
    private Int32Rect ToPixels(Point start, Point end)
    {
        int width = _screen.Image.PixelWidth;
        int height = _screen.Image.PixelHeight;
        if ((end - start).Length < ShapeBuilder.MinimumDragLength)
            return new Int32Rect(0, 0, width, height);

        // The image is shown at exactly 1:1, so WPF units × this window's scaling = image pixels.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int left = Math.Clamp((int)Math.Floor(Math.Min(start.X, end.X) * scale), 0, width - 1);
        int top = Math.Clamp((int)Math.Floor(Math.Min(start.Y, end.Y) * scale), 0, height - 1);
        int right = Math.Clamp((int)Math.Ceiling(Math.Max(start.X, end.X) * scale), left + 1, width);
        int bottom = Math.Clamp((int)Math.Ceiling(Math.Max(start.Y, end.Y) * scale), top + 1, height);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Dims everything except <paramref name="selection"/> and labels its size; null dims the whole screen.</summary>
    private void ShowSelection(Rect? selection)
    {
        var everything = new RectangleGeometry(new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
        bool isDrag = selection is Rect r && (r.BottomRight - r.TopLeft).Length >= ShapeBuilder.MinimumDragLength;

        if (!isDrag)
        {
            Dim.Data = everything;
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            return;
        }

        Rect area = selection!.Value;
        Dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, everything, new RectangleGeometry(area));

        SelectionBorder.Margin = new Thickness(area.X, area.Y, 0, 0);
        SelectionBorder.Width = area.Width;
        SelectionBorder.Height = area.Height;
        SelectionBorder.Visibility = Visibility.Visible;

        // Show the real pixel size (what the saved image will be), just above the selection.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        SizeText.Text = $"{Math.Round(area.Width * scale)} × {Math.Round(area.Height * scale)}";
        SizeLabel.Margin = new Thickness(area.X, Math.Max(0, area.Y - 26), 0, 0);
        SizeLabel.Visibility = Visibility.Visible;
    }

    /// <summary>Moves and sizes the window to exactly cover its monitor, using physical pixels.</summary>
    private void ApplyMonitorBounds()
    {
        PixelRect b = _screen.Monitor.Bounds;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, NativeMethods.SWP_NOACTIVATE);
    }
}
