using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
/// Screenshots are only inspected in memory and never saved (the 📷 flow runs in dry-run mode), so nothing on the
/// user's screen is stored and the clipboard is never replaced.
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
        report.AppendLine("Tip: using the mouse or keyboard while the test runs (about 30 seconds) can cause false failures.");

        using var host = new AppHost(new AppSettings());
        host.Screenshots.DryRun = true; // Screenshots stay in memory: nothing saved to disk, clipboard untouched.
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
        NativeMethods.POINT penPoint = ScreenPoint(host.Toolbar.ButtonScreenCenter("PenButton"));

        // Record what the toolbar actually receives, so a failed click can be told apart from a lost click.
        var toolbarSaw = new List<string>();
        MouseButtonEventHandler onDown = (_, e) => toolbarSaw.Add($"down on {e.OriginalSource.GetType().Name}");
        MouseButtonEventHandler onUp = (_, e) => toolbarSaw.Add($"up on {e.OriginalSource.GetType().Name}");
        host.Toolbar.PreviewMouseLeftButtonDown += onDown;
        host.Toolbar.PreviewMouseLeftButtonUp += onUp;

        // Safety: only click if that exact spot belongs to the toolbar, so a stray click can never reach the user's apps.
        bool penClickable = NativeMethods.WindowFromPoint(penPoint) == host.Toolbar.Handle;
        if (penClickable)
            SyntheticInput.ClickAt(penPoint);
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

        // --- Shapes and text, done the way a user does them.
        var shapeResults = new List<(string Description, bool Passed)>();
        string? shapeSkipReason = await RunShapeAndTextChecksAsync(host, overlays, mainOverlay, shapeResults);
        host.Commands.EnterPointerMode();
        await WaitForScreenAsync();
        bool focusRestoredAfterShapes = NativeMethods.GetForegroundWindow() == originalForeground;

        // --- Screenshots, through the 📷 button.
        var screenshotResults = new List<(string Description, bool Passed)>();
        string? screenshotSkipReason = await RunScreenshotChecksAsync(host, overlays, mainOverlay, screenshotResults);
        await WaitForScreenAsync();
        bool focusRestoredAfterScreenshots = NativeMethods.GetForegroundWindow() == originalForeground;
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
        report.AppendLine("Shapes and text");
        if (shapeSkipReason != null)
            Skip("Shapes and text", shapeSkipReason);
        foreach ((string description, bool passed) in shapeResults)
            Check(passed, description);
        Check(focusRestoredAfterShapes, "Focus returns to your app afterwards");

        report.AppendLine();
        report.AppendLine("Screenshots (kept in memory: nothing saved, clipboard untouched)");
        if (screenshotSkipReason != null)
            Skip("Screenshots", screenshotSkipReason);
        foreach ((string description, bool passed) in screenshotResults)
            Check(passed, description);
        Check(focusRestoredAfterScreenshots, "Focus returns to your app afterwards");

        report.AppendLine();
        report.AppendLine("Unexpected errors");
        Check(UnhandledErrors.Count == 0, $"No unexpected errors ({UnhandledErrors.Count} found)");
        foreach (string error in UnhandledErrors)
            report.AppendLine($"    {error.Replace("\n", "\n    ")}");

        report.AppendLine();
        report.AppendLine(allPassed ? "RESULT: ALL PASSED" : "RESULT: FAILURES FOUND");

        // UTF-8 with a byte-order mark, so Notepad and Windows PowerShell show 📷 and × correctly instead of garbage.
        File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return allPassed;
    }

    /// <summary>
    /// Draws an arrow and a Shift-square by dragging, types text, erases it, and checks undo and Clear all.
    /// Everything happens in Draw mode, where the overlays cover every monitor, so synthetic clicks and drags
    /// can only land on PowerPen. Keys are only sent while an overlay has focus.
    /// </summary>
    /// <returns>Null if the checks ran; otherwise why they were skipped.</returns>
    private static async Task<string?> RunShapeAndTextChecksAsync(
        AppHost host, IReadOnlyList<OverlayWindow> overlays, OverlayWindow main, List<(string Description, bool Passed)> results)
    {
        bool OverlayHasFocus() => IsOverlay(overlays, NativeMethods.GetForegroundWindow());
        PixelRect area = main.TargetMonitor.Bounds;

        // A point on the main monitor as fractions of its size. Shapes go in the upper area, away from the
        // marker line (middle) and the toolbar (left edge).
        NativeMethods.POINT At(double fx, double fy) =>
            new() { X = area.X + (int)(area.Width * fx), Y = area.Y + (int)(area.Height * fy) };

        // Start the way a user would: click the Arrow button.
        NativeMethods.POINT arrowButton = ScreenPoint(host.Toolbar.ButtonScreenCenter("ArrowButton"));
        if (NativeMethods.WindowFromPoint(arrowButton) != host.Toolbar.Handle)
            return "the toolbar's Arrow button isn't clickable";
        SyntheticInput.ClickAt(arrowButton);
        await WaitForScreenAsync();
        if (!host.State.IsDrawMode || host.State.CurrentTool != ToolType.Arrow || !OverlayHasFocus())
            return "clicking Arrow didn't start drawing with focus";

        // Arrow: one stroke whose first two points are the tail and the tip.
        int before = main.Strokes.Count;
        NativeMethods.POINT tail = At(0.30, 0.20), tip = At(0.45, 0.30);
        await SyntheticInput.DragAsync(tail, tip);
        await ShortWaitAsync();
        bool oneArrow = main.Strokes.Count == before + 1;
        StylusPointCollection arrow = main.Strokes[^1].StylusPoints;
        results.Add(("Dragging with Arrow draws one arrow from tail to tip",
            oneArrow && Near(arrow[0], ToOverlay(main, tail)) && Near(arrow[1], ToOverlay(main, tip))));

        // Rectangle with Shift held: the drag is taller than wide, but the result must be a square.
        SyntheticInput.PressKey(Key.R);
        await ShortWaitAsync();
        await SyntheticInput.DragAsync(At(0.30, 0.20), At(0.36, 0.32), holdShift: true);
        await ShortWaitAsync();
        bool square = false;
        if (host.State.CurrentTool == ToolType.Rectangle && main.Strokes.Count == before + 2)
        {
            StylusPointCollection rect = main.Strokes[^1].StylusPoints;
            double width = rect.Max(p => p.X) - rect.Min(p => p.X);
            double height = rect.Max(p => p.Y) - rect.Min(p => p.Y);
            square = width > 0 && Math.Abs(width - height) < 1.5;
        }
        results.Add(("R + drag with Shift draws a square", square));

        if (!OverlayHasFocus())
            return "an overlay lost focus after drawing shapes";
        SyntheticInput.PressChord(ModifierKeys.Control, Key.Z);
        await ShortWaitAsync();
        results.Add(("Ctrl+Z removes the last shape", main.Strokes.Count == before + 1));

        // Text: T, click, type "hi", Enter. Typing H and I must go into the text, not switch tools.
        SyntheticInput.PressKey(Key.T);
        await ShortWaitAsync();
        SyntheticInput.ClickAt(At(0.60, 0.20));
        await WaitForScreenAsync();
        results.Add(("Clicking with Text opens a text box", main.IsEditingText));
        if (!OverlayHasFocus())
            return "an overlay lost focus before typing";
        SyntheticInput.PressKey(Key.H);
        SyntheticInput.PressKey(Key.I);
        await ShortWaitAsync();
        results.Add(("Typed letters go into the text, not to shortcuts", host.State.CurrentTool == ToolType.Text));
        SyntheticInput.PressKey(Key.Enter);
        await ShortWaitAsync();
        TextBox? typed = FindText(main, "hi");
        results.Add(("Enter finishes the text (\"hi\")", typed != null && !main.IsEditingText));
        if (typed == null)
            return null;

        // Eraser across the middle of the text removes it; Ctrl+Z brings it back.
        if (!OverlayHasFocus())
            return "an overlay lost focus after typing";
        SyntheticInput.PressKey(Key.E);
        await ShortWaitAsync();
        double textMiddle = InkCanvas.GetTop(typed) + typed.ActualHeight / 2;
        NativeMethods.POINT eraseFrom = ToScreen(main, new Point(InkCanvas.GetLeft(typed) - 10, textMiddle));
        NativeMethods.POINT eraseTo = ToScreen(main, new Point(InkCanvas.GetLeft(typed) + typed.ActualWidth + 10, textMiddle));
        await SyntheticInput.DragAsync(eraseFrom, eraseTo);
        await ShortWaitAsync();
        results.Add(("Eraser removes text", FindText(main, "hi") == null));
        SyntheticInput.PressChord(ModifierKeys.Control, Key.Z);
        await ShortWaitAsync();
        results.Add(("Ctrl+Z brings erased text back", FindText(main, "hi") != null));

        // Clear all removes shapes and text in one step; one Undo restores both.
        int strokesBeforeClear = main.Strokes.Count;
        host.Commands.ClearAll();
        bool allGone = main.Strokes.Count == 0 && !main.TextItems.Any();
        host.Commands.Undo();
        results.Add(("Clear all removes shapes and text; one Undo restores both",
            allGone && main.Strokes.Count == strokesBeforeClear && FindText(main, "hi") != null));

        if (OverlayHasFocus())
        {
            SyntheticInput.PressKey(Key.Escape);
            await ShortWaitAsync();
        }
        results.Add(("Esc leaves Draw mode", !host.State.IsDrawMode));
        return null;
    }

    /// <summary>
    /// Checks that the toolbar is invisible to captures, then takes three screenshots with the 📷 button:
    /// a dragged area, a whole screen (single click), and a cancelled one (Esc). The selection windows cover
    /// every monitor, so synthetic clicks and drags can only land on PowerPen; keys are only sent while a
    /// PowerPen window has focus.
    /// </summary>
    /// <returns>Null if the checks ran; otherwise why they were skipped.</returns>
    private static async Task<string?> RunScreenshotChecksAsync(
        AppHost host, IReadOnlyList<OverlayWindow> overlays, OverlayWindow main, List<(string Description, bool Passed)> results)
    {
        PixelRect area = main.TargetMonitor.Bounds;
        NativeMethods.POINT At(double fx, double fy) =>
            new() { X = area.X + (int)(area.Width * fx), Y = area.Y + (int)(area.Height * fy) };

        // Toolbar hidden from captures: put a magenta line on the overlay right under the toolbar. If the toolbar is
        // excluded, a capture of its area shows the line; if not, it shows the toolbar's dark buttons instead.
        NativeMethods.GetWindowRect(host.Toolbar.Handle, out NativeMethods.RECT toolbar);
        PixelRect toolbarArea = PixelRect.FromRect(toolbar);
        int toolbarMiddle = toolbarArea.Y + toolbarArea.Height / 2;
        Point lineStart = ToOverlay(main, new NativeMethods.POINT { X = toolbarArea.X - 20, Y = toolbarMiddle });
        Point lineEnd = ToOverlay(main, new NativeMethods.POINT { X = toolbarArea.Right + 20, Y = toolbarMiddle });
        Stroke underToolbar = CreateMarkerLine(lineStart, lineEnd);
        main.Strokes.Add(underToolbar);
        await WaitForScreenAsync();
        int visibleThroughToolbar;
        using (Drawing.Bitmap shot = ScreenCapture.Capture(toolbarArea))
        {
            visibleThroughToolbar = SampleFractions.Count(f =>
            {
                Drawing.Color pixel = shot.GetPixel((int)(shot.Width * f), shot.Height / 2);
                return IsMarkerColor(pixel.R, pixel.G, pixel.B);
            });
        }
        main.Strokes.Remove(underToolbar);
        results.Add(($"Toolbar is left out of screenshots and screen shares ({visibleThroughToolbar}/{SampleFractions.Length} points showed what's underneath)",
            visibleThroughToolbar >= SampleFractions.Length - 1));

        NativeMethods.POINT cameraButton = ScreenPoint(host.Toolbar.ButtonScreenCenter("ScreenshotButton"));

        // Clicks 📷 and waits until every monitor is covered by a selection window. Returns false if not.
        async Task<bool> StartScreenshotAsync()
        {
            if (NativeMethods.WindowFromPoint(cameraButton) != host.Toolbar.Handle)
                return false;
            SyntheticInput.ClickAt(cameraButton);
            for (int i = 0; i < 20; i++) // Up to 2 seconds.
            {
                await Task.Delay(100);
                if (overlays.All(o => IsSelectionWindowAt(o.TargetMonitor, overlays, host)))
                    return true;
            }
            return false;
        }

        // 1. Drag an area that crosses the magenta marker line (at mid-height).
        Task<ScreenshotOutcome> next = NextOutcomeAsync(host.Screenshots);
        if (!await StartScreenshotAsync())
            return "clicking 📷 didn't cover every monitor with a selection screen";
        results.Add(("Clicking 📷 freezes every monitor for selecting", true));

        NativeMethods.POINT from = At(0.30, 0.40), to = At(0.50, 0.60);
        await SyntheticInput.DragAsync(from, to);
        ScreenshotOutcome? dragged = await WithTimeoutAsync(next);
        BitmapSource? draggedImage = dragged?.Image;
        bool rightSize = draggedImage != null
            && Math.Abs(draggedImage.PixelWidth - (to.X - from.X)) <= 2
            && Math.Abs(draggedImage.PixelHeight - (to.Y - from.Y)) <= 2;
        results.Add(($"Dragging captures exactly that area ({draggedImage?.PixelWidth ?? 0}×{draggedImage?.PixelHeight ?? 0} px)", rightSize));
        bool hasInk = draggedImage != null && new[] { 0.25, 0.50, 0.75 }
            .Count(f => IsMarkerAt(draggedImage, (int)(draggedImage.PixelWidth * f), draggedImage.PixelHeight / 2)) >= 2;
        results.Add(("The screenshot includes the ink", hasInk));

        // 2. A single click captures that whole monitor.
        next = NextOutcomeAsync(host.Screenshots);
        if (!await StartScreenshotAsync())
            return "the second 📷 click didn't show the selection screens";
        SyntheticInput.ClickAt(At(0.70, 0.30));
        BitmapSource? fullImage = (await WithTimeoutAsync(next))?.Image;
        results.Add(($"Clicking captures the whole screen ({fullImage?.PixelWidth ?? 0}×{fullImage?.PixelHeight ?? 0} px)",
            fullImage != null && fullImage.PixelWidth == area.Width && fullImage.PixelHeight == area.Height));

        // 3. Esc cancels and removes the selection screens.
        next = NextOutcomeAsync(host.Screenshots);
        if (!await StartScreenshotAsync())
            return "the third 📷 click didn't show the selection screens";
        if (FocusRestorer.IsOwnWindow(NativeMethods.GetForegroundWindow()))
            SyntheticInput.PressKey(Key.Escape);
        ScreenshotOutcome? cancelled = await WithTimeoutAsync(next);
        await WaitForScreenAsync();
        bool selectionGone = !overlays.Any(o => IsSelectionWindowAt(o.TargetMonitor, overlays, host));
        results.Add(("Esc cancels and closes the selection screens", cancelled != null && cancelled.Image == null && selectionGone));
        return null;
    }

    /// <summary>
    /// True if the window at the center of <paramref name="monitor"/> is a PowerPen window that isn't an overlay
    /// or the toolbar, i.e. a screenshot selection window.
    /// </summary>
    private static bool IsSelectionWindowAt(DisplayMonitor monitor, IReadOnlyList<OverlayWindow> overlays, AppHost host)
    {
        PixelRect b = monitor.Bounds;
        IntPtr hwnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = b.X + b.Width / 2, Y = b.Y + b.Height / 2 });
        return FocusRestorer.IsOwnWindow(hwnd) && !IsOverlay(overlays, hwnd) && hwnd != host.Toolbar.Handle;
    }

    /// <summary>Returns a task that completes with the next screenshot outcome.</summary>
    private static Task<ScreenshotOutcome> NextOutcomeAsync(ScreenshotService service)
    {
        var result = new TaskCompletionSource<ScreenshotOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? sender, ScreenshotOutcome outcome)
        {
            service.Completed -= OnCompleted;
            result.TrySetResult(outcome);
        }
        service.Completed += OnCompleted;
        return result.Task;
    }

    /// <summary>Waits up to 5 seconds for <paramref name="task"/>; returns null if it doesn't finish in time.</summary>
    private static async Task<T?> WithTimeoutAsync<T>(Task<T> task) where T : class =>
        await Task.WhenAny(task, Task.Delay(5000)) == task ? await task : null;

    /// <summary>True if the pixel at (<paramref name="x"/>, <paramref name="y"/>) of an in-memory image is the marker color.</summary>
    private static bool IsMarkerAt(BitmapSource image, int x, int y)
    {
        // Convert to a known 4-bytes-per-pixel layout (blue, green, red, alpha) so any source format can be read.
        var pixel = new byte[4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return IsMarkerColor(pixel[2], pixel[1], pixel[0]);
    }

    /// <summary>Creates a thick marker-colored line between two overlay points.</summary>
    private static Stroke CreateMarkerLine(Point start, Point end) =>
        new(new StylusPointCollection([new StylusPoint(start.X, start.Y), new StylusPoint(end.X, end.Y)]), new DrawingAttributes
        {
            Color = MarkerColor,
            Width = 24,
            Height = 24,
            FitToCurve = false,
            IgnorePressure = true,
        });

    /// <summary>The finished text item on <paramref name="overlay"/> reading <paramref name="text"/> (any case), if any.</summary>
    private static TextBox? FindText(OverlayWindow overlay, string text) =>
        overlay.TextItems.FirstOrDefault(t => t.Text.Equals(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>Rounds a WPF screen point (physical pixels) to a Win32 point.</summary>
    private static NativeMethods.POINT ScreenPoint(Point point) =>
        new() { X = (int)Math.Round(point.X), Y = (int)Math.Round(point.Y) };

    /// <summary>Converts a screen point (physical pixels) to the overlay's ink coordinates (WPF units).</summary>
    private static Point ToOverlay(OverlayWindow overlay, NativeMethods.POINT point)
    {
        DisplayMonitor m = overlay.TargetMonitor;
        return new Point((point.X - m.Bounds.X) / m.Scale, (point.Y - m.Bounds.Y) / m.Scale);
    }

    /// <summary>Converts an overlay ink point (WPF units) to a screen point (physical pixels).</summary>
    private static NativeMethods.POINT ToScreen(OverlayWindow overlay, Point point)
    {
        DisplayMonitor m = overlay.TargetMonitor;
        return new NativeMethods.POINT { X = m.Bounds.X + (int)(point.X * m.Scale), Y = m.Bounds.Y + (int)(point.Y * m.Scale) };
    }

    /// <summary>True if a stroke point is within 3 WPF units of <paramref name="expected"/> (allows for pixel rounding).</summary>
    private static bool Near(StylusPoint actual, Point expected) => (new Point(actual.X, actual.Y) - expected).Length <= 3;

    /// <summary>Gives WPF a moment to process input.</summary>
    private static async Task ShortWaitAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(150);
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
        return IsMarkerColor(pixel.R, pixel.G, pixel.B);
    }

    /// <summary>True if a red/green/blue value is the marker color, with a small tolerance for rounding.</summary>
    private static bool IsMarkerColor(int red, int green, int blue)
    {
        const int tolerance = 12;
        return Math.Abs(red - MarkerColor.R) <= tolerance
            && Math.Abs(green - MarkerColor.G) <= tolerance
            && Math.Abs(blue - MarkerColor.B) <= tolerance;
    }

    /// <summary>Lets WPF finish rendering and gives Windows' compositor time to put the result on screen.</summary>
    private static async Task WaitForScreenAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(500);
    }
}
