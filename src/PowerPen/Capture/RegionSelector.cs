using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PowerPen.Interop;

namespace PowerPen.Capture;

/// <summary>One monitor's frozen screenshot.</summary>
/// <param name="Monitor">The monitor it came from.</param>
/// <param name="Image">The full-resolution capture of that monitor.</param>
public sealed record CapturedScreen(DisplayMonitor Monitor, BitmapSource Image);

/// <summary>The area the user picked.</summary>
/// <param name="Screen">The monitor capture it was picked on.</param>
/// <param name="Area">The area in image pixels (the whole image if the user just clicked).</param>
public readonly record struct ScreenRegion(CapturedScreen Screen, Int32Rect Area);

/// <summary>Shows a <see cref="RegionSelectWindow"/> on every monitor and waits for the user to pick an area or cancel.</summary>
internal static class RegionSelector
{
    /// <summary>
    /// Covers every monitor with its frozen image and returns the picked area, or null if the user cancelled.
    /// </summary>
    public static Task<ScreenRegion?> SelectAsync(IReadOnlyList<CapturedScreen> screens)
    {
        // RunContinuationsAsynchronously: the caller's code after "await" must run only after all the
        // selection windows are closed, not in the middle of closing them.
        var result = new TaskCompletionSource<ScreenRegion?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows = new List<RegionSelectWindow>();

        void Finish(ScreenRegion? region)
        {
            if (!result.TrySetResult(region))
                return; // Already finished (closing the windows below triggers more events).
            foreach (RegionSelectWindow window in windows)
                window.Close();
        }

        foreach (CapturedScreen screen in screens)
        {
            var window = new RegionSelectWindow(screen);
            window.RegionSelected += (_, region) => Finish(region);
            window.Cancelled += (_, _) => Finish(null);

            // If the user switches to another app (Alt+Tab, Win key, a notification), cancel. Otherwise frozen
            // full-screen images would be left covering every monitor. Checked a moment later, because clicking
            // from one monitor's window to another's briefly leaves neither active.
            window.Deactivated += (_, _) => window.Dispatcher.InvokeAsync(() =>
            {
                if (!windows.Any(w => w.IsActive))
                    Finish(null);
            }, DispatcherPriority.Background);

            windows.Add(window);
            window.Show();
        }

        // Focus the window under the mouse so Esc works immediately.
        NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
        RegionSelectWindow? underCursor = windows.FirstOrDefault(w => w.TargetMonitor.Bounds.Contains(cursor.X, cursor.Y));
        (underCursor ?? windows.FirstOrDefault())?.Activate();

        return result.Task;
    }
}
