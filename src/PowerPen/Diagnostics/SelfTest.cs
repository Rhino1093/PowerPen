using System.IO;
using System.Text;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PowerPen.Capture;
using PowerPen.Core;
using PowerPen.Input;
using PowerPen.Interop;
using PowerPen.Overlay;
using PowerPen.Settings;
using Drawing = System.Drawing;

namespace PowerPen.Diagnostics;

/// <summary>
/// Automated check of PowerPen on the current machine. Per monitor: overlay placement, DPI, whether screenshots
/// include the ink, and click-through. Overall: undo, hiding ink, toolbar z-order, focus, global hotkeys,
/// and Draw-mode keys. Run with <c>PowerPen.exe --selftest [reportPath]</c>.
/// </summary>
/// <remarks>
/// Uses default settings and never reads or writes the user's settings file.
/// Screenshots are only inspected in memory and never saved, so nothing on the user's screen is stored.
/// The test briefly shows the toolbar and a magenta line on each monitor. It clicks the toolbar's Pen button and
/// presses keys (moving the mouse for a few seconds), only after confirming those inputs can only reach PowerPen.
/// </remarks>
public static class SelfTest
{
    /// <summary>An unusual color that is unlikely to already be on screen, so finding it in a capture proves the ink was captured.</summary>
    private static readonly Color MarkerColor = Color.FromRgb(255, 0, 254);

    /// <summary>Points along the marker line to sample, as fractions of the monitor width.</summary>
    private static readonly double[] SampleFractions = [0.30, 0.40, 0.50, 0.60, 0.70];

    /// <summary>
    /// A combination nobody uses, registered only for the hotkey test. F24 exists in Windows but not on most
    /// keyboards, so it can't clash with anything. (Not F12: Windows reserves it for debuggers and it may never fire.)
    /// </summary>
    private static readonly Hotkey TestHotkey = new(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.F24);

    /// <summary>
    /// Unexpected errors raised while the self-test runs. The app records them here instead of showing a popup,
    /// so they end up in the report rather than blocking the test.
    /// </summary>
    public static List<string> UnhandledErrors { get; } = [];

    /// <summary>Runs every check, writes a readable report to <paramref name="reportPath"/>, and returns true if all passed.</summary>
    public static async Task<bool> RunAsync(string reportPath)
    {
        var report = new StringBuilder();
        bool allPassed = true;

        // Record one check's result in the report and the overall pass/fail.
        void Check(bool passed, string description)
        {
            allPassed &= passed;
            report.AppendLine($"  [{(passed ? "PASS" : "FAIL")}] {description}");
        }

        void Skip(string description, string reason) => report.AppendLine($"  [SKIP] {description} ({reason})");

        report.AppendLine($"PowerPen self-test  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Windows {Environment.OSVersion.Version}, .NET {Environment.Version}");
        report.AppendLine("Tip: using the mouse or keyboard while the test runs (about 10 seconds) can cause false failures.");

        using var host = new AppHost(new AppSettings());
        host.Start();
        IReadOnlyList<OverlayWindow> overlays = host.Overlays.Overlays;
        OverlayWindow mainOverlay = overlays.FirstOrDefault(o => o.TargetMonitor.IsPrimary) ?? overlays[0];

        report.AppendLine(overlays.Select(o => o.TargetMonitor.Dpi).Distinct().Count() > 1
            ? "Monitors use different scaling: mixed-DPI placement IS being tested."
            : "All monitors use the same scaling: mixed-DPI placement is NOT being tested.");

        foreach (OverlayWindow overlay in overlays)
            overlay.Strokes.Add(CreateMarkerStroke(overlay.TargetMonitor));

        // --- Undo/redo through the real wiring (stroke events → undo history), then put everything back.
        int strokeCount = CountStrokes(overlays);
        host.Commands.Undo();
        bool undoRemoved = CountStrokes(overlays) == strokeCount - 1;
        host.Commands.Redo();
        bool redoRestored = CountStrokes(overlays) == strokeCount;

        host.Commands.ClearAll();
        bool cleared = CountStrokes(overlays) == 0;
        host.Commands.Undo();
        bool clearUndoneInOneStep = CountStrokes(overlays) == strokeCount;

        // --- Hide/show ink: the marker must disappear from the screen and come back.
        host.Commands.ToggleInkVisible();
        await WaitForScreenAsync();
        bool hiddenOffScreen = CountMarkerMatches(mainOverlay.TargetMonitor) == 0;
        host.Commands.ToggleInkVisible();
        await WaitForScreenAsync();
        bool shownAgain = CountMarkerMatches(mainOverlay.TargetMonitor) >= SampleFractions.Length - 1;

        // --- Pointer mode (the startup state).
        var pointerModeHits = overlays.ToDictionary(o => o, o => WindowAtMarker(o) == o.Handle);

        // --- Draw mode the way a user enters it: a real (synthetic) click on the toolbar's Pen button.
        // Windows only lets an app take focus right after the user interacts with it, so calling
        // EnterDrawMode() directly from a background-launched test can't prove focus works.
        IntPtr originalForeground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetCursorPos(out NativeMethods.POINT originalCursor);
        System.Windows.Point pen = host.Toolbar.PenButtonScreenCenter();
        var penPoint = new NativeMethods.POINT { X = (int)pen.X, Y = (int)pen.Y };

        // Record what the toolbar actually receives, so a failed click can be told apart from a lost click.
        var toolbarSaw = new List<string>();
        MouseButtonEventHandler onDown = (_, e) => toolbarSaw.Add($"down on {e.OriginalSource.GetType().Name}");
        MouseButtonEventHandler onUp = (_, e) => toolbarSaw.Add($"up on {e.OriginalSource.GetType().Name}");
        host.Toolbar.PreviewMouseLeftButtonDown += onDown;
        host.Toolbar.PreviewMouseLeftButtonUp += onUp;

        // Safety: only click if that exact spot belongs to the toolbar, so a stray click can never reach the user's apps.
        bool penClickable = NativeMethods.WindowFromPoint(penPoint) == host.Toolbar.Handle;
        if (penClickable)
            SyntheticInput.ClickAt(penPoint.X, penPoint.Y);
        await WaitForScreenAsync();
        host.Toolbar.PreviewMouseLeftButtonDown -= onDown;
        host.Toolbar.PreviewMouseLeftButtonUp -= onUp;
        string toolbarSawText = toolbarSaw.Count == 0 ? "nothing" : string.Join(", ", toolbarSaw);

        bool penStartedDrawing = host.State.IsDrawMode && host.State.CurrentTool == ToolType.Pen;
        var drawModeHits = overlays.ToDictionary(o => o, o => WindowAtMarker(o) == o.Handle);
        bool overlayHasFocus = IsOverlay(overlays, NativeMethods.GetForegroundWindow());

        // Simulate clicking the overlay under the toolbar: activating it pushes it above the toolbar,
        // and the toolbar must bring itself back on top.
        overlays.FirstOrDefault(o => o.TargetMonitor.Bounds.Contains(penPoint.X, penPoint.Y))?.Activate();
        await WaitForScreenAsync();
        bool toolbarOnTop = NativeMethods.WindowFromPoint(penPoint) == host.Toolbar.Handle;

        // Leave Draw mode with Esc. Safety: keys are only sent while a PowerPen overlay has focus,
        // so they can never type into the user's app.
        bool escLeftDrawMode = false;
        if (IsOverlay(overlays, NativeMethods.GetForegroundWindow()))
        {
            SyntheticInput.PressKey(Key.Escape);
            await WaitForScreenAsync();
            escLeftDrawMode = !host.State.IsDrawMode;
        }
        host.Commands.EnterPointerMode(); // In case Esc wasn't sent; does nothing if already in Pointer mode.
        await WaitForScreenAsync();
        bool focusRestored = NativeMethods.GetForegroundWindow() == originalForeground;

        // --- Global hotkey → Draw mode. Proves a hotkey press also lets PowerPen take focus.
        using var hotkeys = new HotkeyService();
        bool hotkeyRegistered = hotkeys.TryRegister(TestHotkey, host.Commands.ToggleDrawMode, out _);
        if (hotkeyRegistered)
        {
            SyntheticInput.PressChord(TestHotkey.Modifiers, TestHotkey.Key);
            await WaitForScreenAsync();
        }
        bool hotkeyStartedDrawing = host.State.IsDrawMode;
        bool hotkeyFocusedOverlay = IsOverlay(overlays, NativeMethods.GetForegroundWindow());

        // --- Draw-mode keys, sent only while an overlay has focus.
        var keyResults = new List<(string Description, bool Passed)>();
        if (hotkeyFocusedOverlay)
        {
            int before = CountStrokes(overlays);
            keyResults.Add(("H selects the highlighter", await PressAndCheck(Key.H, () => host.State.CurrentTool == ToolType.Highlighter)));
            keyResults.Add(("3 selects color 3", await PressAndCheck(Key.D3, () => host.State.ColorIndex == 2)));
            keyResults.Add(("] makes lines thicker", await PressAndCheck(Key.OemCloseBrackets, () => host.State.PenSize == PenSize.Large)));
            keyResults.Add(("Ctrl+Z undoes", await PressAndCheck(Key.Z, () => CountStrokes(overlays) == before - 1, ModifierKeys.Control)));
            keyResults.Add(("Ctrl+Y redoes", await PressAndCheck(Key.Y, () => CountStrokes(overlays) == before, ModifierKeys.Control)));
            keyResults.Add(("P selects the pen", await PressAndCheck(Key.P, () => host.State.CurrentTool == ToolType.Pen)));
            keyResults.Add(("Esc leaves Draw mode", await PressAndCheck(Key.Escape, () => !host.State.IsDrawMode)));
        }
        host.Commands.EnterPointerMode();
        await WaitForScreenAsync();
        bool focusRestoredAfterHotkey = NativeMethods.GetForegroundWindow() == originalForeground;
        NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);

        // --- Per-monitor results.
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

            int matches = CountMarkerMatches(monitor);
            // Allow one miss in case another always-on-top window covers part of the line.
            Check(matches >= SampleFractions.Length - 1,
                $"Screenshot includes the ink ({matches}/{SampleFractions.Length} sample points matched)");

            Check(!pointerModeHits[overlay], "Pointer mode: clicks pass through the ink to the app underneath");
            Check(drawModeHits[overlay], "Draw mode: clicks are caught by PowerPen");
        }

        report.AppendLine();
        report.AppendLine("Undo and ink visibility");
        Check(undoRemoved && redoRestored, "Undo removes the last stroke; Redo brings it back");
        Check(cleared && clearUndoneInOneStep, "Clear all is a single undo step across all monitors");
        Check(hiddenOffScreen && shownAgain, "Hide ink removes it from the screen; Show brings it back");

        report.AppendLine();
        report.AppendLine("Toolbar and focus");
        Check(penClickable, "Toolbar is on top and clickable");
        Check(penStartedDrawing, $"Clicking Pen on the toolbar starts drawing with the pen (toolbar received: {toolbarSawText})");
        Check(overlayHasFocus, "Entering Draw mode focuses an overlay (so keys work)");
        Check(toolbarOnTop, "Toolbar stays clickable after an overlay is clicked");
        Check(escLeftDrawMode, "Esc switches back to Pointer mode");
        Check(focusRestored, "Leaving Draw mode gives focus back to the app you were in");

        report.AppendLine();
        report.AppendLine("Hotkeys and Draw-mode keys");
        if (hotkeyRegistered)
        {
            Check(hotkeyStartedDrawing, $"Global hotkey ({TestHotkey}) starts Draw mode");
            Check(hotkeyFocusedOverlay, "Global hotkey lets PowerPen take focus (so Draw-mode keys work)");
            if (keyResults.Count > 0)
            {
                foreach ((string description, bool passed) in keyResults)
                    Check(passed, description);
            }
            else
            {
                Skip("Draw-mode keys", "no overlay had focus, so keys weren't sent");
            }
            Check(focusRestoredAfterHotkey, "Focus returns to your app afterwards");
        }
        else
        {
            Skip("Global hotkey and Draw-mode keys", $"{TestHotkey} is taken by another app");
        }

        report.AppendLine();
        report.AppendLine("Unexpected errors");
        Check(UnhandledErrors.Count == 0, $"No unexpected errors ({UnhandledErrors.Count} found)");
        foreach (string error in UnhandledErrors)
            report.AppendLine($"    {error.Replace("\n", "\n    ")}");

        report.AppendLine();
        report.AppendLine(allPassed ? "RESULT: ALL PASSED" : "RESULT: FAILURES FOUND");
        File.WriteAllText(reportPath, report.ToString());
        return allPassed;
    }

    /// <summary>Presses a key (with optional modifiers), waits for PowerPen to react, and evaluates <paramref name="check"/>.</summary>
    private static async Task<bool> PressAndCheck(Key key, Func<bool> check, ModifierKeys modifiers = ModifierKeys.None)
    {
        SyntheticInput.PressChord(modifiers, key);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(150);
        return check();
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

    /// <summary>Captures the monitor and counts how many sample points along the marker line show the marker color.</summary>
    private static int CountMarkerMatches(DisplayMonitor monitor)
    {
        using Drawing.Bitmap shot = ScreenCapture.Capture(monitor.Bounds);
        return SampleFractions.Count(f => IsMarkerPixel(shot, f));
    }

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
