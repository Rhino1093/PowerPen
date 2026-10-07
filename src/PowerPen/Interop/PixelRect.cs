namespace PowerPen.Interop;

/// <summary>
/// A rectangle in physical screen pixels. Kept separate from WPF's <see cref="System.Windows.Rect"/>
/// (which uses scaled, device-independent units) so it's always obvious which unit a value is in.
/// </summary>
/// <param name="X">Left edge in pixels.</param>
/// <param name="Y">Top edge in pixels.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>Right edge (exclusive).</summary>
    public int Right => X + Width;

    /// <summary>Bottom edge (exclusive).</summary>
    public int Bottom => Y + Height;

    /// <summary>True if the pixel at (<paramref name="x"/>, <paramref name="y"/>) is inside this rectangle.</summary>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Converts a Win32 RECT (given as edges) into a PixelRect.</summary>
    internal static PixelRect FromRect(NativeMethods.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    /// <summary>Readable form for logs, e.g. "1920x1080 at (2560,0)".</summary>
    public override string ToString() => $"{Width}x{Height} at ({X},{Y})";
}
