using System.Windows.Media;

namespace PowerPen.Core;

/// <summary>The drawing tools. Pointer is not a tool; it's a mode (see <see cref="AppState.IsDrawMode"/>).</summary>
public enum ToolType
{
    /// <summary>Freehand pen.</summary>
    Pen,

    /// <summary>Wide, semi-transparent marker.</summary>
    Highlighter,

    /// <summary>Removes whole strokes it touches.</summary>
    Eraser,
}

/// <summary>The three pen-size presets, in order from thinnest to thickest.</summary>
public enum PenSize
{
    /// <summary>Thin line.</summary>
    Small,

    /// <summary>Default line.</summary>
    Medium,

    /// <summary>Thick line.</summary>
    Large,
}

/// <summary>A palette color with a display name for tooltips.</summary>
/// <param name="Name">Name shown to the user, e.g. "Red".</param>
/// <param name="Color">The color itself.</param>
public sealed record NamedColor(string Name, Color Color);
