using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using PowerPen.Interop;

namespace PowerPen.Input;

/// <summary>
/// Registers system-wide hotkeys (they work even while another app has focus) and runs the matching action when pressed.
/// </summary>
/// <remarks>
/// Hotkey messages go to a hidden message-only window owned by this service rather than an overlay,
/// so hotkeys keep working while overlays are rebuilt after a monitor change.
/// </remarks>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _messageWindow;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 1;

    /// <summary>Creates the hidden window that receives hotkey messages.</summary>
    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("PowerPenHotkeys")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0, // Not visible.
        };
        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);
    }

    /// <summary>
    /// Registers <paramref name="modifiers"/> + <paramref name="key"/> to run <paramref name="action"/>.
    /// </summary>
    /// <param name="errorMessage">When registration fails: a message for the user explaining how to fix it.</param>
    /// <returns><c>true</c> if the hotkey is now active.</returns>
    public bool TryRegister(ModifierKeys modifiers, Key key, Action action, out string? errorMessage)
    {
        int id = _nextId++;

        // WPF's ModifierKeys values (Alt=1, Control=2, Shift=4, Windows=8) are identical to Win32's MOD_* flags.
        uint win32Modifiers = (uint)modifiers | NativeMethods.MOD_NOREPEAT;
        uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

        if (!NativeMethods.RegisterHotKey(_messageWindow.Handle, id, win32Modifiers, virtualKey))
        {
            // Read the error code first, before any other call can overwrite it.
            int error = Marshal.GetLastPInvokeError();
            string name = Describe(modifiers, key);
            errorMessage = error == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"{name} is already used by another app, so PowerPen can't use it.\n\n" +
                  "To fix: close the app that uses this shortcut, then restart PowerPen."
                : $"Windows wouldn't let PowerPen use {name}.\n\nTo fix: restart PowerPen. If it keeps happening, restart your PC.";
            return false;
        }

        _actions[id] = action;
        errorMessage = null;
        return true;
    }

    /// <summary>Formats a hotkey for display, e.g. "Ctrl+Alt+D".</summary>
    public static string Describe(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        // WPF names the number-row keys D0–D9; show them as plain digits.
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Unregisters all hotkeys and destroys the hidden window.</summary>
    public void Dispose()
    {
        foreach (int id in _actions.Keys)
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, id);
        _actions.Clear();

        _messageWindow.RemoveHook(WndProc);
        _messageWindow.Dispose();
    }

    /// <summary>Runs the registered action when Windows reports one of our hotkeys was pressed.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out Action? action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }
}
