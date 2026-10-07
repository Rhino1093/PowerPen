using System.Runtime.InteropServices;
using System.Windows.Input;
using PowerPen.Interop;

namespace PowerPen.Diagnostics;

/// <summary>
/// Sends fake mouse clicks, drags, and key presses, exactly as if the user did them. Self-test only:
/// Windows only lets an app take keyboard focus right after the user interacts with it,
/// so focus, hotkey, and drawing behavior can only be tested with real-looking input.
/// </summary>
internal static class SyntheticInput
{
    /// <summary>Pause between drag steps, so WPF sees each move separately (Windows merges queued moves).</summary>
    private const int DragStepDelayMs = 15;

    /// <summary>Moves the cursor to <paramref name="point"/> (physical pixels) and left-clicks.</summary>
    public static void ClickAt(NativeMethods.POINT point)
    {
        // Move and click go in ONE batch: Windows guarantees a batch isn't interleaved with real mouse input,
        // so the click always lands on the point even if the user moves the mouse at that moment.
        Send(MoveTo(point), MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN), MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP));
    }

    /// <summary>
    /// Presses the left button at <paramref name="from"/>, moves in steps to <paramref name="to"/>, and releases there.
    /// </summary>
    /// <param name="from">Start point in physical pixels.</param>
    /// <param name="to">End point in physical pixels.</param>
    /// <param name="holdShift">Hold Shift for the whole drag (e.g. to draw a square).</param>
    /// <param name="steps">Number of intermediate moves.</param>
    public static async Task DragAsync(NativeMethods.POINT from, NativeMethods.POINT to, bool holdShift = false, int steps = 10)
    {
        try
        {
            var start = new List<NativeMethods.INPUT>();
            if (holdShift)
                start.Add(KeyEvent(NativeMethods.VK_SHIFT, 0));
            start.Add(MoveTo(from));
            start.Add(MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN));
            Send([.. start]);

            for (int i = 1; i < steps; i++)
            {
                await Task.Delay(DragStepDelayMs);
                Send(MoveTo(new NativeMethods.POINT
                {
                    X = from.X + (to.X - from.X) * i / steps,
                    Y = from.Y + (to.Y - from.Y) * i / steps,
                }));
            }
            await Task.Delay(DragStepDelayMs);
        }
        finally
        {
            // Always release, even if something above failed: a stuck button or Shift would affect
            // the user's real mouse and keyboard. Final move + release in one batch so the end point is exact.
            var end = new List<NativeMethods.INPUT> { MoveTo(to), MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP) };
            if (holdShift)
                end.Add(KeyEvent(NativeMethods.VK_SHIFT, NativeMethods.KEYEVENTF_KEYUP));
            Send([.. end]);
        }
    }

    /// <summary>Presses and releases a single key.</summary>
    public static void PressKey(Key key) => PressChord(ModifierKeys.None, key);

    /// <summary>Holds the modifiers, taps <paramref name="key"/>, then releases the modifiers in reverse order.</summary>
    public static void PressChord(ModifierKeys modifiers, Key key)
    {
        var modifierKeys = new List<ushort>();
        if (modifiers.HasFlag(ModifierKeys.Control)) modifierKeys.Add(NativeMethods.VK_CONTROL);
        if (modifiers.HasFlag(ModifierKeys.Alt)) modifierKeys.Add(NativeMethods.VK_MENU);
        if (modifiers.HasFlag(ModifierKeys.Shift)) modifierKeys.Add(NativeMethods.VK_SHIFT);
        if (modifiers.HasFlag(ModifierKeys.Windows)) modifierKeys.Add(NativeMethods.VK_LWIN);

        ushort virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
        var inputs = new List<NativeMethods.INPUT>();
        inputs.AddRange(modifierKeys.Select(vk => KeyEvent(vk, 0)));
        inputs.Add(KeyEvent(virtualKey, 0));
        inputs.Add(KeyEvent(virtualKey, NativeMethods.KEYEVENTF_KEYUP));
        inputs.AddRange(Enumerable.Reverse(modifierKeys).Select(vk => KeyEvent(vk, NativeMethods.KEYEVENTF_KEYUP)));
        Send([.. inputs]);
    }

    /// <summary>Builds an absolute move to <paramref name="point"/> (physical pixels) anywhere on the multi-monitor desktop.</summary>
    private static NativeMethods.INPUT MoveTo(NativeMethods.POINT point)
    {
        // Absolute coordinates are scaled to 0–65535 across the whole multi-monitor desktop.
        int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        int dx = (int)Math.Round((point.X - left) * 65535.0 / (width - 1));
        int dy = (int)Math.Round((point.Y - top) * 65535.0 / (height - 1));

        const uint flags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK;
        return MouseEvent(flags, dx, dy);
    }

    /// <summary>Builds a mouse event; <paramref name="dx"/>/<paramref name="dy"/> are only used by move events.</summary>
    private static NativeMethods.INPUT MouseEvent(uint flags, int dx = 0, int dy = 0) => new()
    {
        type = NativeMethods.INPUT_MOUSE,
        u = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } },
    };

    /// <summary>Builds a key-down (flags 0) or key-up event.</summary>
    private static NativeMethods.INPUT KeyEvent(ushort virtualKey, uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = virtualKey, dwFlags = flags } },
    };

    /// <summary>Injects the events in order.</summary>
    private static void Send(params NativeMethods.INPUT[] inputs) =>
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
}
