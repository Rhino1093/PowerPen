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

    /// <summary>Extended style: clicking the window does not activate it (focus stays with the current app).</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;

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

    /// <summary>Returns the window the user is currently working in (the one receiving keyboard input).</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    /// <summary>Brings a window to the front and gives it keyboard focus.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Gets the ID of the process that owns a window.</summary>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>True if the handle still refers to an existing window.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

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

    #region Capture exclusion and known folders

    /// <summary>Display affinity: the window shows normally everywhere.</summary>
    public const uint WDA_NONE = 0x0;

    /// <summary>
    /// Display affinity (Windows 10 2004+): the window shows on the monitor but is left out of screenshots,
    /// recordings, and screen sharing.
    /// </summary>
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    /// <summary>Sets whether a window appears in screen captures.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    /// <summary>Known-folder ID for the user's Downloads folder (wherever it's been redirected to).</summary>
    public static readonly Guid FOLDERID_Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>Gets the real path of a known folder. Returns 0 (S_OK) on success; free the path with <see cref="CoTaskMemFree"/>.</summary>
    [DllImport("shell32.dll")]
    public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    /// <summary>Frees memory allocated by the system for us (e.g. a known-folder path).</summary>
    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);

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

    #region Synthetic input (self-test only)

    /// <summary>INPUT type: mouse event.</summary>
    public const uint INPUT_MOUSE = 0;

    /// <summary>INPUT type: keyboard event.</summary>
    public const uint INPUT_KEYBOARD = 1;

    /// <summary>Mouse flag: the event moves the cursor.</summary>
    public const uint MOUSEEVENTF_MOVE = 0x0001;

    /// <summary>Mouse flag: coordinates are absolute (0–65535) rather than relative to the last position.</summary>
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    /// <summary>Mouse flag: absolute coordinates span the whole multi-monitor desktop, not just the main monitor.</summary>
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    /// <summary>Mouse flag: left button pressed.</summary>
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;

    /// <summary>Mouse flag: left button released.</summary>
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;

    /// <summary>Keyboard flag: key released (omit for key pressed).</summary>
    public const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>Virtual-key code for Shift (either side).</summary>
    public const ushort VK_SHIFT = 0x10;

    /// <summary>Virtual-key code for Ctrl (either side).</summary>
    public const ushort VK_CONTROL = 0x11;

    /// <summary>Virtual-key code for Alt (either side).</summary>
    public const ushort VK_MENU = 0x12;

    /// <summary>Virtual-key code for the left Windows key.</summary>
    public const ushort VK_LWIN = 0x5B;

    /// <summary>One synthetic mouse or keyboard event for <see cref="SendInput"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    /// <summary>The mouse or keyboard payload of an <see cref="INPUT"/> (they share memory, like a C union).</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    /// <summary>Synthetic mouse event data.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>Synthetic keyboard event data.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>Injects mouse/keyboard events as if the user performed them. Returns how many were sent.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>Moves the mouse cursor to a position in physical screen pixels.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    /// <summary>GetSystemMetrics index: left edge of the whole multi-monitor desktop.</summary>
    public const int SM_XVIRTUALSCREEN = 76;

    /// <summary>GetSystemMetrics index: top edge of the whole multi-monitor desktop.</summary>
    public const int SM_YVIRTUALSCREEN = 77;

    /// <summary>GetSystemMetrics index: width of the whole multi-monitor desktop.</summary>
    public const int SM_CXVIRTUALSCREEN = 78;

    /// <summary>GetSystemMetrics index: height of the whole multi-monitor desktop.</summary>
    public const int SM_CYVIRTUALSCREEN = 79;

    /// <summary>Reads a system measurement such as the desktop size.</summary>
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

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
