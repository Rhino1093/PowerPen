using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PowerPen.Diagnostics;

/// <summary>
/// Development aid: renders a piece of PowerPen's own UI to a PNG without showing it, to check layouts.
/// Only the given element is drawn; nothing from the screen is ever captured.
/// </summary>
internal static class VisualSnapshot
{
    /// <summary>Lays out <paramref name="root"/> and saves it as a PNG at 2× size (so small details are easy to inspect).</summary>
    /// <param name="root">The element to render (its background should be set on it, not on the window).</param>
    /// <param name="path">Where to write the PNG.</param>
    /// <param name="width">Fixed width in WPF units, or null to use the element's natural width.</param>
    public static void Save(FrameworkElement root, string path, double? width = null)
    {
        root.Measure(new Size(width ?? double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(new Size(width ?? root.DesiredSize.Width, root.DesiredSize.Height)));
        root.UpdateLayout();

        const double scale = 2;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * scale), (int)Math.Ceiling(root.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }
}
