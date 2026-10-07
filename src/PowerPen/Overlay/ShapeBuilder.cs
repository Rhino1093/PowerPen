using System.Windows;
using PowerPen.Core;

namespace PowerPen.Overlay;

/// <summary>
/// Pure geometry: turns a drag (start → end) into the points of a shape. Each shape is ONE polyline,
/// so it becomes one stroke that erases and undoes as a unit.
/// </summary>
public static class ShapeBuilder
{
    /// <summary>Drags shorter than this (WPF units) are treated as clicks and produce no shape.</summary>
    public const double MinimumDragLength = 4;

    /// <summary>Points used to draw an ellipse. Enough that edges look smooth even on a full-screen circle.</summary>
    private const int EllipseSegments = 96;

    /// <summary>Half-angle of the arrowhead, in degrees.</summary>
    private const double ArrowHeadAngle = 28;

    /// <summary>
    /// Builds the points for <paramref name="shape"/> dragged from <paramref name="start"/> to <paramref name="end"/>.
    /// Returns an empty list if the drag is too short.
    /// </summary>
    /// <param name="shape">Line, Arrow, Rectangle, or Ellipse.</param>
    /// <param name="start">Where the drag began.</param>
    /// <param name="end">Where the mouse is now.</param>
    /// <param name="constrain">Shift held: lines snap to 45° steps; rectangles/ellipses become squares/circles.</param>
    /// <param name="lineWidth">Stroke width, used to size the arrowhead.</param>
    public static IReadOnlyList<Point> Build(ToolType shape, Point start, Point end, bool constrain, double lineWidth)
    {
        if (constrain)
            end = shape is ToolType.Line or ToolType.Arrow ? SnapAngle(start, end) : MakeSquare(start, end);

        if ((end - start).Length < MinimumDragLength)
            return [];

        return shape switch
        {
            ToolType.Line => [start, end],
            ToolType.Arrow => BuildArrow(start, end, lineWidth),
            ToolType.Rectangle => BuildRectangle(start, end),
            ToolType.Ellipse => BuildEllipse(start, end),
            _ => throw new ArgumentException($"{shape} is not a shape.", nameof(shape)),
        };
    }

    /// <summary>
    /// Shaft plus a two-sided head, as one path: start → tip → left wing → back to tip → right wing.
    /// Retracing the left wing is invisible but keeps the arrow a single stroke.
    /// </summary>
    private static List<Point> BuildArrow(Point start, Point tip, double lineWidth)
    {
        Vector shaft = tip - start;
        double shaftLength = shaft.Length;

        // The head grows with line width so thick arrows don't get a tiny head,
        // but never takes more than 40% of a short arrow.
        double headLength = Math.Min(Math.Max(12, lineWidth * 4), shaftLength * 0.4);

        Vector back = -shaft / shaftLength * headLength;
        Point leftWing = tip + Rotate(back, ArrowHeadAngle);
        Point rightWing = tip + Rotate(back, -ArrowHeadAngle);

        return [start, tip, leftWing, tip, rightWing];
    }

    /// <summary>Closed rectangle through the four corners.</summary>
    private static List<Point> BuildRectangle(Point a, Point b) =>
    [
        a,
        new Point(b.X, a.Y),
        b,
        new Point(a.X, b.Y),
        a,
    ];

    /// <summary>Closed ellipse that fits inside the rectangle from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static List<Point> BuildEllipse(Point a, Point b)
    {
        var center = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        double radiusX = Math.Abs(b.X - a.X) / 2;
        double radiusY = Math.Abs(b.Y - a.Y) / 2;

        var points = new List<Point>(EllipseSegments + 1);
        for (int i = 0; i < EllipseSegments; i++)
        {
            double angle = 2 * Math.PI * i / EllipseSegments;
            points.Add(new Point(center.X + radiusX * Math.Cos(angle), center.Y + radiusY * Math.Sin(angle)));
        }

        // Close the loop with the exact first point (computing angle 2π instead would be off by rounding).
        points.Add(points[0]);
        return points;
    }

    /// <summary>Keeps the drag length but snaps its direction to the nearest multiple of 45°.</summary>
    private static Point SnapAngle(Point start, Point end)
    {
        Vector drag = end - start;
        double step = Math.PI / 4;
        double angle = Math.Round(Math.Atan2(drag.Y, drag.X) / step) * step;
        return start + new Vector(Math.Cos(angle), Math.Sin(angle)) * drag.Length;
    }

    /// <summary>Makes width and height equal (the larger of the two), keeping the drag direction.</summary>
    private static Point MakeSquare(Point start, Point end)
    {
        double side = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        // Not Math.Sign: it returns 0 for a perfectly horizontal/vertical drag, which would collapse the square.
        double signX = end.X >= start.X ? 1 : -1;
        double signY = end.Y >= start.Y ? 1 : -1;
        return new Point(start.X + side * signX, start.Y + side * signY);
    }

    /// <summary>Rotates a vector by <paramref name="degrees"/>.</summary>
    private static Vector Rotate(Vector v, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        return new Vector(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
