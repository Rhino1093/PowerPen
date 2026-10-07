using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using PowerPen.Interop;

namespace PowerPen.Capture;

/// <summary>Grabs pixels straight from the screen.</summary>
public static class ScreenCapture
{
    /// <summary>
    /// Copies an area of the screen into a new bitmap, including PowerPen's transparent ink overlays.
    /// The caller owns (and must dispose) the returned bitmap.
    /// </summary>
    /// <param name="area">Area to capture, in physical pixels (full native resolution).</param>
    /// <exception cref="Win32Exception">The screen couldn't be read (e.g. the PC is locked).</exception>
    public static Bitmap Capture(PixelRect area)
    {
        var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb);
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            IntPtr bitmapDc = graphics.GetHdc();
            try
            {
                // Calls BitBlt directly because Graphics.CopyFromScreen rejects the CAPTUREBLT flag.
                // CAPTUREBLT is required: without it Windows leaves "layered" (transparent) windows out of the copy,
                // and the ink overlay is exactly that kind of window.
                if (!NativeMethods.BitBlt(bitmapDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y,
                        NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                    throw new Win32Exception();
            }
            finally
            {
                graphics.ReleaseHdc(bitmapDc);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
