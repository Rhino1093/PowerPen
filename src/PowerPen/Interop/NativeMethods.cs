using System.Runtime.InteropServices;

namespace PowerPen.Interop;

/// <summary>
/// All Win32 API declarations used by PowerPen. Keeping them in one file means the rest of the code
/// never deals with raw flags or struct layouts directly.
/// </summary>
internal static class NativeMethods
{
    #region Constants

    /// <summary>Index of the extended window style for Get/SetWindowLongPtr.</summary>
    public const int GWL_EXSTYLE = -20;

    /// <summary>Extended style: mouse input passes through the window to whatever is underneath.</summary>
    public const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>Extended style: hides the window from Alt+Tab.</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Special z-order value for SetWindowPos: place the window above all non-topmost windows.</summary>
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    /// <summary>Parent handle that makes a window "message-only": it is never shown and only receives messages.</summary>
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    /// <summary>SetWindowPos flag: don't activate (focus) the window.</summary>
    public const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>SetWindowPos flag: keep the current size.</summary>
    public const uint SWP_NOSIZE = 0x0001;

    /// <summary>SetWindowPos flag: keep the current position.</summary>
    public const uint SWP_NOMOVE = 0x0002;

    /// <summary>SetWindowPos flag: keep the current z-order.</summary>
    public const uint SWP_NOZORDER = 0x0004;

    /// <summary>SetWindowPos flag: tell Windows the window's styles changed so they take effect right away.</summary>
    public const uint SWP_FRAMECHANGED = 0x0020;

    /// <summary>Message sent to the registering window when a global hotkey is pressed.</summary>
    public const int WM_HOTKEY = 0x0312;

    /// <summary>RegisterHotKey flag: holding the keys down fires once instead of auto-repeating.</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    /// <summary>Win32 error code returned when another app already registered the same hotkey.</summary>
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    /// <summary>BitBlt raster operation: plain copy of the source pixels.</summary>
    public const uint SRCCOPY = 0x00CC0020;

    /// <summary>BitBlt flag: include layered (transparent) windows such as the ink overlay in the copy.</summary>
    public const uint CAPTUREBLT = 0x40000000;

    /// <summary>MONITORINFO flag set on the primary (main) monitor.</summary>
    public const uint MONITORINFOF_PRIMARY = 0x1;

    /// <summary>GetDpiForMonitor type: the DPI that reflects the user's scaling setting.</summary>
    public const int MDT_EFFECTIVE_DPI = 0;

    #endregion

    #region Structs and delegates

    /// <summary>Win32 rectangle given as edges (left/top inclusive, right/bottom exclusive).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>Win32 point in physical screen pixels.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    /// <summary>Monitor details returned by <see cref="GetMonitorInfo"/>.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    /// <summary>Callback invoked once per monitor by <see cref="EnumDisplayMonitors"/>.</summary>
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    #endregion

    #region Monitors

    /// <summary>Calls <paramref name="lpfnEnum"/> once for every connected monitor.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    /// <summary>Gets a monitor's bounds, work area, device name, and primary flag.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    /// <summary>Gets a monitor's DPI (96 = 100% scaling). Returns 0 (S_OK) on success.</summary>
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    #endregion

    #region Windows

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>Moves, resizes, and/or changes the z-order of a window. Coordinates are physical pixels.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>Gets a window's on-screen rectangle in physical pixels.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>Returns the window that would receive a mouse click at <paramref name="point"/>.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    /// <summary>Gets the mouse cursor position in physical screen pixels.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>Adds the given extended style flags to a window.</summary>
    public static void AddExtendedStyle(IntPtr hwnd, int flags) =>
        SetExtendedStyle(hwnd, GetExtendedStyle(hwnd) | (uint)flags);

    /// <summary>Removes the given extended style flags from a window.</summary>
    public static void RemoveExtendedStyle(IntPtr hwnd, int flags) =>
        SetExtendedStyle(hwnd, GetExtendedStyle(hwnd) & ~(long)(uint)flags);

    /// <summary>Reads a window's extended style flags.</summary>
    private static long GetExtendedStyle(IntPtr hwnd) =>
        // 64-bit Windows exports GetWindowLongPtrW; 32-bit only has GetWindowLongW.
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64() : GetWindowLong32(hwnd, GWL_EXSTYLE);

    /// <summary>Writes a window's extended style flags and makes Windows apply them immediately.</summary>
    private static void SetExtendedStyle(IntPtr hwnd, long style)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(style));
        else
            SetWindowLong32(hwnd, GWL_EXSTYLE, (int)style);

        // Some style changes are cached by Windows until the frame is refreshed; this forces it without moving anything.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    #endregion

    #region Screen capture

    /// <summary>Gets a device context for a window; <see cref="IntPtr.Zero"/> means the entire screen.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    /// <summary>Releases a device context obtained with <see cref="GetDC"/>.</summary>
    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    /// <summary>Copies a block of pixels from one device context to another.</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rasterOp);

    #endregion

    #region Hotkeys

    /// <summary>Registers a system-wide hotkey that sends WM_HOTKEY to <paramref name="hWnd"/>.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    /// <summary>Releases a hotkey registered with <see cref="RegisterHotKey"/>.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    #endregion
}
