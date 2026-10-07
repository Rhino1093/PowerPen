using System.IO;
using System.Text;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PowerPen.Capture;
using PowerPen.Interop;
using PowerPen.Overlay;
using Drawing = System.Drawing;

namespace PowerPen.Diagnostics;

/// <summary>
/// Automated check of PowerPen on the current machine. Per monitor: overlay placement, DPI, whether screenshots
/// include the ink, and click-through. Overall: undo, toolbar z-order, and focus hand-back.
/// Run with <c>PowerPen.exe --selftest [reportPath]</c>.
/// </summary>
/// <remarks>
/// Screenshots are only inspected in memory and never saved, so nothing on the user's screen is stored.
/// The test briefly shows the toolbar and a magenta line on each monitor. It also clicks the toolbar's Pen button
/// and presses Esc (moving the mouse for about a second), only after confirming those inputs can only reach PowerPen.
/// </remarks>
public static class SelfTest
{
    /// <summary>An unusual color that is unlikely to already be on screen, so finding it in a capture proves the ink was captured.</summary>
    private static readonly Color MarkerColor = Color.FromRgb(255, 0, 254);

    /// <summary>Points along the marker line to sample, as fractions of the monitor width.</summary>
    private static readonly double[] SampleFractions = [0.30, 0.40, 0.50, 0.60, 0.70];

    /// <summary>Runs every check, writes a readable report to <paramref name="reportPath"/>, and returns true if all passed.</summary>
    public static async Task<bool> RunAsync(string reportPath)
    {
        var report = new StringBuilder();
        bool allPassed = true;

        // Records one check's result in the report and the overall pass/fail.
        void Check(bool passed, string description)
        {
            allPassed &= passed;
            report.AppendLine($"  [{(passed ? "PASS" : "FAIL")}] {description}");
        }

        report.AppendLine($"PowerPen self-test  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Windows {Environment.OSVersion.Version}, .NET {Environment.Version}");

        using var host = new AppHost();
        host.Start();
        IReadOnlyList<OverlayWindow> overlays = host.Overlays.Overlays;

        report.AppendLine(overlays.Select(o => o.TargetMonitor.Dpi).Distinct().Count() > 1
            ? "Monitors use different scaling: mixed-DPI placement IS being tested."
            : "All monitors use the same scaling: mixed-DPI placement is NOT being tested.");

        foreach (OverlayWindow overlay in overlays)
            overlay.Strokes.Add(CreateMarkerStroke(overlay.TargetMonitor));

        // Undo/redo through the real wiring (stroke events → undo history), then put everything back.
        int strokeCount = CountStrokes(overlays);
        host.Commands.Undo();
        bool undoRemoved = CountStrokes(overlays) == strokeCount - 1;
        host.Commands.Redo();
        bool redoRestored = CountStrokes(overlays) == strokeCount;

        host.Commands.ClearAll();
        bool cleared = CountStrokes(overlays) == 0;
        host.Commands.Undo();
        bool clearUndoneInOneStep = CountStrokes(overlays) == strokeCount;

        // Pointer mode (the startup state).
        await WaitForScreenAsync();
        var pointerModeHits = overlays.ToDictionary(o => o, o => WindowAtMarker(o) == o.Handle);

        // Draw mode the way a user enters it: a real (synthetic) click on the toolbar's Pen button.
        // Windows only lets an app take focus right after the user interacts with it, so calling
        // EnterDrawMode() directly from a background-launched test can't prove focus works.
        IntPtr originalForeground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetCursorPos(out NativeMethods.POINT originalCursor);
        System.Windows.Point pen = host.Toolbar.PenButtonScreenCenter();
        var penPoint = new NativeMethods.POINT { X = (int)pen.X, Y = (int)pen.Y };

        // Safety: only click if that exact spot belongs to the toolbar, so a stray click can never reach the user's apps.
        bool penClickable = NativeMethods.WindowFromPoint(penPoint) == host.Toolbar.Handle;
        if (penClickable)
            SyntheticInput.ClickAt(penPoint.X, penPoint.Y);
        await WaitForScreenAsync();

        bool penStartedDrawing = host.State.IsDrawMode && host.State.CurrentTool == Core.ToolType.Pen;
        var drawModeHits = overlays.ToDictionary(o => o, o => WindowAtMarker(o) == o.Handle);
        bool overlayHasFocus = IsOverlay(overlays, NativeMethods.GetForegroundWindow());

        // Simulate clicking the overlay under the toolbar: activating it pushes it above the toolbar,
        // and the toolbar must bring itself back on top.
        overlays.FirstOrDefault(o => o.TargetMonitor.Bounds.Contains(penPoint.X, penPoint.Y))?.Activate();
        await WaitForScreenAsync();
        bool toolbarOnTop = NativeMethods.WindowFromPoint(penPoint) == host.Toolbar.Handle;

        // Leave Draw mode with Esc. Safety: only press it if a PowerPen overlay has focus, so it can't reach the user's app.
        bool escLeftDrawMode = false;
        if (IsOverlay(overlays, NativeMethods.GetForegroundWindow()))
        {
            SyntheticInput.PressEscape();
            await WaitForScreenAsync();
            escLeftDrawMode = !host.State.IsDrawMode;
        }
        host.Commands.EnterPointerMode(); // In case Esc wasn't sent; does nothing if already in Pointer mode.
        await WaitForScreenAsync();
        bool focusRestored = NativeMethods.GetForegroundWindow() == originalForeground;
        NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);

        foreach (OverlayWindow overlay in overlays)
        {
            DisplayMonitor monitor = overlay.TargetMonitor;
            report.AppendLine();
            report.AppendLine(monitor.ToString());

            NativeMethods.GetWindowRect(overlay.Handle, out NativeMethods.RECT rect);
            PixelRect actual = PixelRect.FromRect(rect);
            Check(actual == monitor.Bounds, $"Overlay exactly covers the monitor (overlay is {actual})");

            double wpfScale = VisualTreeHelper.GetDpi(overlay).DpiScaleX;
            Check(Math.Abs(wpfScale - monitor.Scale) < 0.01, $"WPF renders at this monitor's scaling ({wpfScale:P0})");

            using (Drawing.Bitmap shot = ScreenCapture.Capture(monitor.Bounds))
            {
                int matches = SampleFractions.Count(f => IsMarkerPixel(shot, f));
                // Allow one miss in case another always-on-top window covers part of the line.
                Check(matches >= SampleFractions.Length - 1,
                    $"Screenshot includes the ink ({matches}/{SampleFractions.Length} sample points matched)");
            }

            Check(!pointerModeHits[overlay], "Pointer mode: clicks pass through the ink to the app underneath");
            Check(drawModeHits[overlay], "Draw mode: clicks are caught by PowerPen");
        }

        report.AppendLine();
        report.AppendLine("Undo, toolbar, and focus");
        Check(undoRemoved && redoRestored, "Undo removes the last stroke; Redo brings it back");
        Check(cleared && clearUndoneInOneStep, "Clear all is a single undo step across all monitors");
        Check(penClickable, "Toolbar is on top and clickable");
        Check(penStartedDrawing, "Clicking Pen on the toolbar starts drawing with the pen");
        Check(overlayHasFocus, "Entering Draw mode focuses an overlay (so Esc works)");
        Check(toolbarOnTop, "Toolbar stays clickable after an overlay is clicked");
        Check(escLeftDrawMode, "Esc switches back to Pointer mode");
        Check(focusRestored, "Leaving Draw mode gives focus back to the app you were in");

        report.AppendLine();
        report.AppendLine(allPassed ? "RESULT: ALL PASSED" : "RESULT: FAILURES FOUND");
        File.WriteAllText(reportPath, report.ToString());
        return allPassed;
    }

    /// <summary>Creates a thick horizontal line across the middle half of the monitor in <see cref="MarkerColor"/>.</summary>
    private static Stroke CreateMarkerStroke(DisplayMonitor monitor)
    {
        // Ink coordinates are WPF units (physical pixels ÷ scaling), relative to the overlay's top-left corner.
        double width = monitor.Bounds.Width / monitor.Scale;
        double y = monitor.Bounds.Height / monitor.Scale / 2;

        var points = new StylusPointCollection();
        for (int i = 0; i <= 20; i++)
            points.Add(new StylusPoint(width * (0.25 + 0.5 * i / 20), y));

        return new Stroke(points, new DrawingAttributes
        {
            Color = MarkerColor,
            Width = 24,
            Height = 24,
            FitToCurve = false,
            IgnorePressure = true, // Synthetic points have default pressure; draw at full width.
        });
    }

    /// <summary>Total number of strokes across all monitors.</summary>
    private static int CountStrokes(IEnumerable<OverlayWindow> overlays) => overlays.Sum(o => o.Strokes.Count);

    /// <summary>Returns the window Windows would send a click to at the center of the marker line.</summary>
    private static IntPtr WindowAtMarker(OverlayWindow overlay)
    {
        PixelRect b = overlay.TargetMonitor.Bounds;
        return NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = b.X + b.Width / 2, Y = b.Y + b.Height / 2 });
    }

    /// <summary>True if <paramref name="hwnd"/> is one of the overlays.</summary>
    private static bool IsOverlay(IEnumerable<OverlayWindow> overlays, IntPtr hwnd) => overlays.Any(o => o.Handle == hwnd);

    /// <summary>True if the captured pixel on the marker line at <paramref name="xFraction"/> of the width is the marker color.</summary>
    private static bool IsMarkerPixel(Drawing.Bitmap shot, double xFraction)
    {
        Drawing.Color pixel = shot.GetPixel((int)(shot.Width * xFraction), shot.Height / 2);

        // Small tolerance for color-management or rounding differences.
        const int tolerance = 12;
        return Math.Abs(pixel.R - MarkerColor.R) <= tolerance
            && Math.Abs(pixel.G - MarkerColor.G) <= tolerance
            && Math.Abs(pixel.B - MarkerColor.B) <= tolerance;
    }

    /// <summary>Lets WPF finish rendering and gives Windows' compositor time to put the result on screen.</summary>
    private static async Task WaitForScreenAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(500);
    }
}
