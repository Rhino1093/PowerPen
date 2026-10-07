using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using PowerPen.Core;

namespace PowerPen.Overlay;

/// <summary>
/// Handles one shape drag on an overlay: shows a live preview while dragging and produces the final stroke on release.
/// </summary>
/// <remarks>
/// The preview lives in a separate <see cref="InkPresenter"/> instead of the real stroke collection, so dragging
/// doesn't fire stroke-changed events (which would flood the undo history). It renders exactly like real ink.
/// </remarks>
internal sealed class ShapeTool
{
    private readonly InkPresenter _preview;
    private ToolType _shape;
    private Point _start;
    private Point _current;
    private DrawingAttributes _attributes = new();

    /// <summary>Creates a shape tool that draws its preview into <paramref name="preview"/>.</summary>
    public ShapeTool(InkPresenter preview)
    {
        _preview = preview;
    }

    /// <summary>True between mouse-down and mouse-up of a shape drag.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Starts a drag at <paramref name="start"/>.</summary>
    /// <param name="shape">Which shape to draw.</param>
    /// <param name="start">Drag start in overlay (WPF) coordinates.</param>
    /// <param name="penAttributes">Current pen color/width; adapted for shapes.</param>
    public void Begin(ToolType shape, Point start, DrawingAttributes penAttributes)
    {
        _shape = shape;
        _start = start;
        _current = start;
        _attributes = InkStyle.ForShape(penAttributes);
        IsDragging = true;
        _preview.Strokes.Clear();
    }

    /// <summary>Updates the preview for the mouse at <paramref name="current"/>.</summary>
    public void Update(Point current)
    {
        _current = current;
        RefreshPreview();
    }

    /// <summary>
    /// Re-draws the preview with the current Shift state. Called when Shift is pressed or released mid-drag,
    /// so the shape snaps without having to move the mouse.
    /// </summary>
    public void RefreshPreview()
    {
        if (!IsDragging)
            return;

        _preview.Strokes.Clear();
        if (BuildStroke() is Stroke stroke)
            _preview.Strokes.Add(stroke);
    }

    /// <summary>Ends the drag and returns the finished shape, or null if the drag was too short to count.</summary>
    public Stroke? Finish(Point end)
    {
        if (!IsDragging)
            return null;

        _current = end;
        Stroke? stroke = BuildStroke();
        Cancel();
        return stroke;
    }

    /// <summary>Abandons the drag and clears the preview.</summary>
    public void Cancel()
    {
        IsDragging = false;
        _preview.Strokes.Clear();
    }

    /// <summary>Builds the stroke for the current drag, or null if it's too short.</summary>
    private Stroke? BuildStroke()
    {
        bool constrain = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        IReadOnlyList<Point> points = ShapeBuilder.Build(_shape, _start, _current, constrain, _attributes.Width);
        if (points.Count < 2)
            return null;

        return new Stroke(new StylusPointCollection(points.Select(p => new StylusPoint(p.X, p.Y))), _attributes.Clone());
    }
}
