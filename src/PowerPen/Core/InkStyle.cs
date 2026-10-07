using System.Windows.Controls;
using System.Windows.Ink;

namespace PowerPen.Core;

/// <summary>Translates the user's selections in <see cref="AppState"/> into InkCanvas settings.</summary>
public static class InkStyle
{
    /// <summary>Creates new drawing settings (color, width, tip shape) for the current tool, color, and size.</summary>
    public static DrawingAttributes Create(AppState state)
    {
        double width = state.PenWidth;

        if (state.CurrentTool == ToolType.Highlighter)
        {
            width *= AppState.HighlighterWidthMultiplier;
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

    /// <summary>The InkCanvas editing mode to use for a tool while in Draw mode.</summary>
    public static InkCanvasEditingMode EditingModeFor(ToolType tool) => tool switch
    {
        ToolType.Eraser => InkCanvasEditingMode.EraseByStroke,
        _ => InkCanvasEditingMode.Ink,
    };
}
