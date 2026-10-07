namespace PowerPen.Interop;

/// <summary>
/// Remembers which app had focus before PowerPen took it (when entering Draw mode), so focus can be handed back
/// afterwards. Without this, a PowerPoint slideshow would stop responding to arrow keys after you draw on it.
/// </summary>
public sealed class FocusRestorer
{
    private IntPtr _previousWindow;

    /// <summary>Records the window that currently has focus, unless it belongs to PowerPen.</summary>
    public void Remember()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();

        // Our own windows (overlays, message boxes) are not worth handing focus back to.
        _previousWindow = foreground != IntPtr.Zero && !IsOwnWindow(foreground) ? foreground : IntPtr.Zero;
    }

    /// <summary>Gives focus back to the remembered window, if PowerPen still has focus.</summary>
    public void Restore()
    {
        IntPtr target = _previousWindow;
        _previousWindow = IntPtr.Zero;

        // If the user already switched to another app (e.g. with Alt+Tab), leave their choice alone.
        if (target != IntPtr.Zero && NativeMethods.IsWindow(target) && IsOwnWindow(NativeMethods.GetForegroundWindow()))
            NativeMethods.SetForegroundWindow(target);
    }

    /// <summary>True if the window belongs to this PowerPen process.</summary>
    public static bool IsOwnWindow(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == Environment.ProcessId;
    }
}
