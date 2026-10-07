using System.IO;
using System.Text;
using System.Windows;
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
/// Automated check of the risky parts on the current machine, for every monitor:
/// overlay placement, DPI, whether screenshots include the ink, and click-through.
/// Run with <c>PowerPen.exe --selftest [reportPath]</c>.
/// </summary>
/// <remarks>
/// Screenshots are only inspected in memory and never saved, so nothing on the user's screen is stored.
/// The test briefly shows a magenta line on each monitor (about 2 seconds).
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

        using var overlays = new OverlayManager();
        overlays.Start();

        report.AppendLine(overlays.Overlays.Select(o => o.TargetMonitor.Dpi).Distinct().Count() > 1
            ? "Monitors use different scaling: mixed-DPI placement IS being tested."
            : "All monitors use the same scaling: mixed-DPI placement is NOT being tested.");

        foreach (OverlayWindow overlay in overlays.Overlays)
            overlay.Strokes.Add(CreateMarkerStroke(overlay.TargetMonitor));

        // Pointer mode (the startup state): check placement, DPI, capture, and click-through.
        await WaitForScreenAsync();
        var pointerModeHits = overlays.Overlays.ToDictionary(o => o, o => WindowAtMarker(o) == o.Handle);

        // Draw mode: the same spot must now hit the overlay. SetDrawMode doesn't take focus, so the user's app stays active.
        overlays.SetDrawMode(true);
        await WaitForScreenAsync();
        var drawModeHits = overlays.Overlays.ToDictionary(o => o, o => WindowAtMarker(o) == o.Handle);
        overlays.SetDrawMode(false);

        foreach (OverlayWindow overlay in overlays.Overlays)
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

    /// <summary>Returns the window Windows would send a click to at the center of the marker line.</summary>
    private static IntPtr WindowAtMarker(OverlayWindow overlay)
    {
        PixelRect b = overlay.TargetMonitor.Bounds;
        var point = new NativeMethods.POINT { X = b.X + b.Width / 2, Y = b.Y + b.Height / 2 };
        return NativeMethods.WindowFromPoint(point);
    }

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
