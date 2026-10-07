using System.Windows.Controls;
using System.Windows.Ink;

namespace PowerPen.Core;

/// <summary>Translates the user's selections in <see cref="AppState"/> into InkCanvas settings.</summary>
public static class InkStyle
{
    /// <summary>Text font size (WPF units) for each <see cref="PenSize"/>, indexed by the enum value.</summary>
    private static readonly double[] TextFontSizes = [18, 28, 40];

    /// <summary>
    /// Creates new drawing settings (color, width, tip shape) for the current tool, color, and size.
    /// Shapes and text use the pen settings.
    /// </summary>
    public static DrawingAttributes Create(AppState state)
    {
        double width = state.PenWidth;

        if (state.CurrentTool == ToolType.Highlighter)
        {
            width *= state.HighlighterWidthMultiplier;
            return new DrawingAttributes
            {
                Color = state.CurrentColor,
                Width = width,
                Height = width,
                IsHighlighter = true, // WPF draws highlighter strokes semi-transparent and behind pen strokes.
                StylusTip = StylusTip.Rectangle,
                FitToCurve = true,
                IgnorePressure = true, // A marker should look even, not taper.
            };
        }

        return new DrawingAttributes
        {
            Color = state.CurrentColor,
            Width = width,
            Height = width,
            FitToCurve = true, // Smooths mouse jitter into curves.
        };
    }

    /// <summary>
    /// Adapts pen settings for shapes: no curve smoothing (it would round off rectangle corners and arrowheads)
    /// and even width (shape points carry no real pressure).
    /// </summary>
    public static DrawingAttributes ForShape(DrawingAttributes penAttributes)
    {
        DrawingAttributes shape = penAttributes.Clone();
        shape.FitToCurve = false;
        shape.IgnorePressure = true;
        shape.IsHighlighter = false;
        shape.StylusTip = StylusTip.Ellipse;
        return shape;
    }

    /// <summary>Font size for typed text at the given pen size.</summary>
    public static double TextFontSize(PenSize size) => TextFontSizes[(int)size];

    /// <summary>
    /// The InkCanvas editing mode for a tool while in Draw mode. Shapes and text are drawn by PowerPen itself,
    /// so the InkCanvas must not collect ink for them.
    /// </summary>
    public static InkCanvasEditingMode EditingModeFor(ToolType tool) => tool switch
    {
        ToolType.Pen or ToolType.Highlighter => InkCanvasEditingMode.Ink,
        ToolType.Eraser => InkCanvasEditingMode.EraseByStroke,
        _ => InkCanvasEditingMode.None,
    };
}
