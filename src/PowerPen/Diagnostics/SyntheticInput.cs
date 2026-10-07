using System.Runtime.InteropServices;
using PowerPen.Interop;

namespace PowerPen.Diagnostics;

/// <summary>
/// Sends fake mouse clicks and key presses, exactly as if the user did them. Self-test only:
/// Windows only lets an app take keyboard focus right after the user interacts with it,
/// so focus behavior can only be tested with real-looking input.
/// </summary>
internal static class SyntheticInput
{
    /// <summary>Moves the cursor to (<paramref name="x"/>, <paramref name="y"/>) in physical pixels and left-clicks.</summary>
    public static void ClickAt(int x, int y)
    {
        NativeMethods.SetCursorPos(x, y);
        Send(Mouse(NativeMethods.MOUSEEVENTF_LEFTDOWN), Mouse(NativeMethods.MOUSEEVENTF_LEFTUP));
    }

    /// <summary>Presses and releases Esc.</summary>
    public static void PressEscape() =>
        Send(Key(NativeMethods.VK_ESCAPE, 0), Key(NativeMethods.VK_ESCAPE, NativeMethods.KEYEVENTF_KEYUP));

    /// <summary>Builds a mouse-button event at the current cursor position.</summary>
    private static NativeMethods.INPUT Mouse(uint flags) => new()
    {
        type = NativeMethods.INPUT_MOUSE,
        u = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dwFlags = flags } },
    };

    /// <summary>Builds a key event.</summary>
    private static NativeMethods.INPUT Key(ushort virtualKey, uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = virtualKey, dwFlags = flags } },
    };

    /// <summary>Injects the events in order.</summary>
    private static void Send(params NativeMethods.INPUT[] inputs) =>
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
}
