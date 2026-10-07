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
/// A transparent, always-on-top window that exactly covers one monitor and holds that monitor's ink and text.
/// PowerPen creates one per monitor (see <see cref="OverlayManager"/>) so each renders at its own monitor's scaling.
/// </summary>
/// <remarks>
/// Pen, highlighter, and stroke erasing are handled by the InkCanvas itself. Shapes (<see cref="ShapeTool"/>),
/// text (<see cref="TextTool"/>), and erasing text are handled here, because the InkCanvas doesn't do them.
/// </remarks>
public partial class OverlayWindow : Window
{
    /// <summary>
    /// Nearly invisible background used in Draw mode. Windows sends clicks straight through pixels that are
    /// 100% transparent, so the surface needs an alpha of 1 (out of 255) to catch the mouse while still looking clear.
    /// </summary>
    private static readonly Brush HitTestableBrush = CreateFrozenBrush(Color.FromArgb(1, 0, 0, 0));

    /// <summary>How close (WPF units) the eraser has to get to text to remove it. Matches the eraser's 16-unit size.</summary>
    private const double EraserRadius = 8;

    private readonly ShapeTool _shapes;
    private readonly TextTool _text;
    private IntPtr _hwnd;
    private bool _isClickThrough;
    private bool _isDrawing;
    private ToolType _tool;

    /// <summary>Creates an overlay for <paramref name="monitor"/>. Call <see cref="Window.Show"/> to display it.</summary>
    public OverlayWindow(DisplayMonitor monitor)
    {
        InitializeComponent();
        TargetMonitor = monitor;

        // Bigger than the default eraser so a quick swipe reliably catches thin strokes.
        Ink.EraserShape = new EllipseStylusShape(EraserRadius * 2, EraserRadius * 2);

        _shapes = new ShapeTool(Preview);
        _text = new TextTool(Ink);
        _text.ItemChanged += (_, e) => TextItemChanged?.Invoke(this, e);

        // The release handler uses handledEventsToo because InkCanvas marks mouse-up as handled.
        AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => GestureEnded?.Invoke(this, EventArgs.Empty)), handledEventsToo: true);
        Ink.LostMouseCapture += OnInkLostMouseCapture;
    }

    /// <summary>Raised when the mouse button goes down on this overlay (start of one undo step).</summary>
    public event EventHandler? GestureStarted;

    /// <summary>Raised when the mouse button is released (or capture is lost) on this overlay (end of one undo step).</summary>
    public event EventHandler? GestureEnded;

    /// <summary>Raised when a text item is added or removed (for undo).</summary>
    public event EventHandler<TextItemChangedEventArgs>? TextItemChanged;

    /// <summary>The monitor this overlay covers.</summary>
    public DisplayMonitor TargetMonitor { get; }

    /// <summary>Native window handle. Valid once the window has been shown.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>The strokes drawn on this monitor (pen, highlighter, and shapes).</summary>
    public StrokeCollection Strokes => Ink.Strokes;

    /// <summary>The overlay's child elements (text items live here). Used by undo to put text back.</summary>
    public UIElementCollection Elements => Ink.Children;

    /// <summary>Finished text items on this monitor.</summary>
    public IEnumerable<TextBox> TextItems => _text.Items;

    /// <summary>True while the user is typing in a text box; keys must go to the box, not to shortcuts.</summary>
    public bool IsEditingText => _text.IsEditing;

    /// <summary>
    /// Applies the current mode, visibility, tool, and style. In Draw mode the overlay catches the mouse;
    /// in Pointer mode the ink stays visible and clicks pass through.
    /// </summary>
    /// <param name="state">The shared app state.</param>
    /// <param name="attributes">Pen settings for new strokes. Must be a copy this overlay can own.</param>
    public void ApplyState(AppState state, DrawingAttributes attributes)
    {
        _isDrawing = state.IsDrawMode;
        _tool = state.CurrentTool;

        // Leaving the text tool (or Draw mode) finishes whatever is being typed; leaving a shape tool drops a half-drawn shape.
        if (!_isDrawing || _tool != ToolType.Text)
            _text.Commit();
        if (!_isDrawing || !_tool.IsShape())
            CancelShape();

        _text.SetStyle(state.CurrentColor, InkStyle.TextFontSize(state.PenSize));

        Ink.DefaultDrawingAttributes = attributes;
        Ink.EditingMode = _isDrawing ? InkStyle.EditingModeFor(_tool) : InkCanvasEditingMode.None;
        Ink.Background = _isDrawing ? HitTestableBrush : Brushes.Transparent;
        Ink.Visibility = state.IsInkVisible ? Visibility.Visible : Visibility.Hidden;

        // InkCanvas picks its own cursor for pen/eraser; shapes and text need one that says what a click will do.
        Ink.UseCustomCursor = _isDrawing && (_tool.IsShape() || _tool == ToolType.Text);
        Ink.Cursor = _tool == ToolType.Text ? Cursors.IBeam : Cursors.Cross;

        SetClickThrough(!_isDrawing);
    }

    /// <summary>Removes all ink and text from this monitor. Each removal is reported for undo.</summary>
    public void ClearAll()
    {
        CancelShape();
        _text.RemoveAll();
        Ink.Strokes.Clear();
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

    /// <summary>Starts an undo step, then starts a shape, places/finishes text, or erases text, depending on the tool.</summary>
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        GestureStarted?.Invoke(this, EventArgs.Empty);
        if (!_isDrawing)
            return;

        Point position = e.GetPosition(Ink);
        if (_tool.IsShape())
        {
            _shapes.Begin(_tool, position, Ink.DefaultDrawingAttributes);
            Ink.CaptureMouse(); // Keep receiving moves even if the drag leaves the window.
            e.Handled = true;
        }
        else if (_tool == ToolType.Text)
        {
            // Clicks inside the open box move the caret as usual.
            if (_text.IsInsideEditor(e.OriginalSource))
                return;

            // A click elsewhere finishes the open box; the next click starts a new one.
            if (_text.IsEditing)
                _text.Commit();
            else
                _text.BeginAt(position);
            e.Handled = true;
        }
        else if (_tool == ToolType.Eraser)
        {
            _text.EraseAt(position, EraserRadius); // Not handled: the InkCanvas erases strokes at the same time.
        }
    }

    /// <summary>Updates the shape preview, or keeps erasing text while the eraser is dragged.</summary>
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_shapes.IsDragging)
            _shapes.Update(e.GetPosition(Ink));
        else if (_isDrawing && _tool == ToolType.Eraser && e.LeftButton == MouseButtonState.Pressed)
            _text.EraseAt(e.GetPosition(Ink), EraserRadius);
    }

    /// <summary>Finishes a shape drag and adds the shape as a normal stroke (so undo and the eraser just work).</summary>
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!_shapes.IsDragging)
            return;

        Stroke? shape = _shapes.Finish(e.GetPosition(Ink));
        Ink.ReleaseMouseCapture();
        if (shape != null)
            Ink.Strokes.Add(shape);
        e.Handled = true;
    }

    /// <summary>During a shape drag: Esc cancels it, and Shift snaps it immediately without moving the mouse.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_shapes.IsDragging)
            return;

        if (e.Key == Key.Escape)
        {
            CancelShape();
            e.Handled = true; // Only cancels the shape; a second Esc leaves Draw mode.
        }
        else if (e.Key is Key.LeftShift or Key.RightShift)
        {
            // Keyboard.Modifiers already includes Shift here.
            _shapes.RefreshPreview();
        }
    }

    /// <summary>Un-snaps the shape preview as soon as Shift is released.</summary>
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (_shapes.IsDragging && e.Key is Key.LeftShift or Key.RightShift)
            _shapes.RefreshPreview();
    }

    /// <summary>Drops a shape drag if the mouse capture is lost unexpectedly (e.g. switching apps mid-drag).</summary>
    private void OnInkLostMouseCapture(object sender, MouseEventArgs e)
    {
        CancelShape();
        GestureEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Abandons any shape being dragged.</summary>
    private void CancelShape()
    {
        if (!_shapes.IsDragging)
            return;

        _shapes.Cancel();
        if (Ink.IsMouseCaptured)
            Ink.ReleaseMouseCapture();
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
