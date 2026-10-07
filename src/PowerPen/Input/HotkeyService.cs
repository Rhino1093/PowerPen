using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using PowerPen.Interop;

namespace PowerPen.Input;

/// <summary>Why a hotkey couldn't be registered.</summary>
public enum HotkeyError
{
    /// <summary>Registered successfully.</summary>
    None,

    /// <summary>Another app (or another copy of PowerPen) already uses this combination.</summary>
    AlreadyInUse,

    /// <summary>Windows refused for another reason.</summary>
    Other,
}

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

    /// <summary>Registers <paramref name="hotkey"/> to run <paramref name="action"/>.</summary>
    /// <param name="hotkey">The key combination.</param>
    /// <param name="action">What to run when it's pressed.</param>
    /// <param name="error">Why registration failed, or <see cref="HotkeyError.None"/>.</param>
    /// <returns><c>true</c> if the hotkey is now active.</returns>
    public bool TryRegister(Hotkey hotkey, Action action, out HotkeyError error)
    {
        int id = _nextId++;

        // WPF's ModifierKeys values (Alt=1, Control=2, Shift=4, Windows=8) are identical to Win32's MOD_* flags.
        uint win32Modifiers = (uint)hotkey.Modifiers | NativeMethods.MOD_NOREPEAT;
        uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);

        if (!NativeMethods.RegisterHotKey(_messageWindow.Handle, id, win32Modifiers, virtualKey))
        {
            error = Marshal.GetLastPInvokeError() == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                ? HotkeyError.AlreadyInUse
                : HotkeyError.Other;
            return false;
        }

        _actions[id] = action;
        error = HotkeyError.None;
        return true;
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
