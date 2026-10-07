using System.Runtime.InteropServices;
using System.Windows.Input;
using PowerPen.Interop;

namespace PowerPen.Diagnostics;

/// <summary>
/// Sends fake mouse clicks and key presses, exactly as if the user did them. Self-test only:
/// Windows only lets an app take keyboard focus right after the user interacts with it,
/// so focus and hotkey behavior can only be tested with real-looking input.
/// </summary>
internal static class SyntheticInput
{
    /// <summary>Moves the cursor to (<paramref name="x"/>, <paramref name="y"/>) in physical pixels and left-clicks.</summary>
    public static void ClickAt(int x, int y)
    {
        // Move and click go in ONE batch: Windows guarantees a batch isn't interleaved with real mouse input,
        // so the click always lands on (x, y) even if the user moves the mouse at that moment.
        // Absolute coordinates are scaled to 0–65535 across the whole multi-monitor desktop.
        int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        int dx = (int)Math.Round((x - left) * 65535.0 / (width - 1));
        int dy = (int)Math.Round((y - top) * 65535.0 / (height - 1));

        const uint moveFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK;
        Send(MouseEvent(moveFlags, dx, dy),
             MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN),
             MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP));
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
