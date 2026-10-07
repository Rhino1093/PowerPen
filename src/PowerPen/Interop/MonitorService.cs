using System.Runtime.InteropServices;

namespace PowerPen.Interop;

/// <summary>One connected monitor, as Windows reports it.</summary>
/// <param name="DeviceName">Windows device name, e.g. <c>\\.\DISPLAY1</c>.</param>
/// <param name="Bounds">Full monitor area in physical pixels.</param>
/// <param name="WorkArea">Monitor area minus the taskbar, in physical pixels.</param>
/// <param name="IsPrimary">True for the main monitor.</param>
/// <param name="Dpi">Effective DPI; 96 means 100% scaling.</param>
public sealed record DisplayMonitor(string DeviceName, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary, uint Dpi)
{
    /// <summary>Scaling factor: 1.0 = 100%, 1.25 = 125%, 1.5 = 150%.</summary>
    public double Scale => Dpi / 96.0;

    /// <summary>Readable form for logs, e.g. "\\.\DISPLAY1 (main): 2560x1440 at (0,0), 100% scaling".</summary>
    public override string ToString() =>
        $"{DeviceName}{(IsPrimary ? " (main)" : "")}: {Bounds}, {Scale:P0} scaling";
}

/// <summary>Lists the monitors that are currently connected.</summary>
public static class MonitorService
{
    /// <summary>
    /// Returns every connected monitor, ordered left to right, with bounds in physical pixels.
    /// </summary>
    public static IReadOnlyList<DisplayMonitor> GetMonitors()
    {
        var monitors = new List<DisplayMonitor>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
            {
                var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
                if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
                    return true; // Skip a monitor we can't read, but keep enumerating the rest.

                // If the DPI query fails, assume 100% so the overlay still covers the monitor.
                uint dpi = NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
                    ? dpiX
                    : 96;

                monitors.Add(new DisplayMonitor(
                    info.szDevice,
                    PixelRect.FromRect(info.rcMonitor),
                    PixelRect.FromRect(info.rcWork),
                    (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                    dpi));
                return true;
            },
            IntPtr.Zero);

        return monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
    }
}
