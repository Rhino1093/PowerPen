using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PowerPen.Interop;

namespace PowerPen.UI;

/// <summary>
/// A small notice in the bottom-right corner of a monitor (e.g. "Screenshot saved") that disappears by itself.
/// Clicking it runs an optional action, such as opening the folder.
/// </summary>
/// <remarks>Never takes focus and never appears in screenshots or screen shares.</remarks>
public partial class ToastWindow : Window
{
    /// <summary>How long the toast stays up.</summary>
    private static readonly TimeSpan DisplayTime = TimeSpan.FromSeconds(5);

    /// <summary>Gap between the toast and the screen edges, in WPF units.</summary>
    private const double EdgeMargin = 16;

    private readonly DisplayMonitor _monitor;
    private readonly Action? _onClick;
    private readonly DispatcherTimer _closeTimer;
    private IntPtr _hwnd;

    /// <summary>Creates a toast. Call <see cref="Window.Show"/> to display it.</summary>
    /// <param name="title">Main line, e.g. "Screenshot saved".</param>
    /// <param name="detail">Second line, e.g. the file name.</param>
    /// <param name="monitor">Monitor whose bottom-right corner it appears in.</param>
    /// <param name="onClick">Optional action when clicked (the toast closes either way).</param>
    public ToastWindow(string title, string detail, DisplayMonitor monitor, Action? onClick)
    {
        InitializeComponent();
        TitleText.Text = title;
        DetailText.Text = detail;
        _monitor = monitor;
        _onClick = onClick;

        _closeTimer = new DispatcherTimer { Interval = DisplayTime };
        _closeTimer.Tick += (_, _) => Close();

        // Keep it up while the mouse is over it, so there's time to read and click it.
        MouseEnter += (_, _) => _closeTimer.Stop();
        MouseLeave += (_, _) => _closeTimer.Start();
        Closed += (_, _) => _closeTimer.Stop();
    }

    /// <summary>Configures the native window before it first appears.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Never steal focus, stay out of Alt+Tab, and never show up in the next screenshot or a screen share.
        NativeMethods.AddExtendedStyle(_hwnd, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

        // Place it before it first appears, using the content's measured size, so it never flashes in the wrong spot.
        // Then place it again once rendered, using its exact final size (it can change if the monitor's scaling differs).
        var content = (UIElement)Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        PlaceInCorner((int)Math.Ceiling(content.DesiredSize.Width * _monitor.Scale), (int)Math.Ceiling(content.DesiredSize.Height * _monitor.Scale));
        ContentRendered += (_, _) => PlaceInCorner();
        _closeTimer.Start();
    }

    /// <summary>Runs the click action and closes.</summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Close();
        _onClick?.Invoke();
    }

    /// <summary>Moves the toast to the bottom-right of the monitor's work area using its current on-screen size.</summary>
    private void PlaceInCorner()
    {
        NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT rect);
        PlaceInCorner(rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>Moves the toast to the bottom-right of the monitor's work area (above the taskbar), in physical pixels.</summary>
    private void PlaceInCorner(int width, int height)
    {
        int margin = (int)(EdgeMargin * _monitor.Scale);

        PixelRect work = _monitor.WorkArea;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            work.Right - width - margin, work.Bottom - height - margin, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
