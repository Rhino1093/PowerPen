using System.Windows.Media;

namespace PowerPen.Core;

/// <summary>The drawing tools. Pointer is not a tool; it's a mode (see <see cref="AppState.IsDrawMode"/>).</summary>
public enum ToolType
{
    /// <summary>Freehand pen.</summary>
    Pen,

    /// <summary>Wide, semi-transparent marker.</summary>
    Highlighter,

    /// <summary>Removes whole strokes and text it touches.</summary>
    Eraser,

    /// <summary>Straight line (Shift: 45° steps).</summary>
    Line,

    /// <summary>Line with an arrowhead at the end (Shift: 45° steps).</summary>
    Arrow,

    /// <summary>Rectangle outline (Shift: square).</summary>
    Rectangle,

    /// <summary>Ellipse outline (Shift: circle).</summary>
    Ellipse,

    /// <summary>Typed text.</summary>
    Text,
}

/// <summary>Helpers for <see cref="ToolType"/>.</summary>
public static class ToolTypeExtensions
{
    /// <summary>True for tools drawn by dragging from one corner/end to the other.</summary>
    public static bool IsShape(this ToolType tool) =>
        tool is ToolType.Line or ToolType.Arrow or ToolType.Rectangle or ToolType.Ellipse;
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
