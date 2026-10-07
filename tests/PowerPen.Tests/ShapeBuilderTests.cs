using System.Windows;
using PowerPen.Core;
using PowerPen.Overlay;

namespace PowerPen.Tests;

/// <summary>Tests for the shape geometry (drag start/end → points).</summary>
public class ShapeBuilderTests
{
    private const double Tolerance = 0.001;

    /// <summary>A line is exactly its two end points.</summary>
    [Fact]
    public void Line_IsStartAndEnd()
    {
        IReadOnlyList<Point> points = ShapeBuilder.Build(ToolType.Line, new(10, 10), new(110, 60), constrain: false, lineWidth: 4);

        Assert.Equal([new Point(10, 10), new Point(110, 60)], points);
    }

    /// <summary>Shift snaps a line to the nearest 45° step while keeping its length.</summary>
    [Theory]
    [InlineData(100, 8, 0)]      // Nearly horizontal → horizontal.
    [InlineData(70, 75, 45)]     // Near the diagonal → 45°.
    [InlineData(-5, -100, -90)]  // Nearly straight up → vertical.
    public void Line_Constrained_SnapsTo45Degrees(double dx, double dy, double expectedDegrees)
    {
        var start = new Point(200, 200);
        IReadOnlyList<Point> points = ShapeBuilder.Build(ToolType.Line, start, start + new Vector(dx, dy), constrain: true, lineWidth: 4);

        Vector result = points[1] - start;
        double degrees = Math.Atan2(result.Y, result.X) * 180 / Math.PI;
        Assert.Equal(expectedDegrees, degrees, 3);
        Assert.Equal(new Vector(dx, dy).Length, result.Length, 3);
    }

    /// <summary>An arrow is one path: tail → tip → wing → tip → wing, with both wings behind the tip.</summary>
    [Fact]
    public void Arrow_IsOnePathThroughTheTip()
    {
        Point tail = new(0, 0), tip = new(100, 0);
        IReadOnlyList<Point> points = ShapeBuilder.Build(ToolType.Arrow, tail, tip, constrain: false, lineWidth: 4);

        Assert.Equal(5, points.Count);
        Assert.Equal(tail, points[0]);
        Assert.Equal(tip, points[1]);
        Assert.Equal(tip, points[3]);
        Assert.True(points[2].X < tip.X && points[4].X < tip.X, "Wings should point back toward the tail.");
        Assert.True(points[2].Y * points[4].Y < 0, "Wings should be on opposite sides of the shaft.");
    }

    /// <summary>The arrowhead never takes more than 40% of a short arrow.</summary>
    [Fact]
    public void Arrow_HeadIsLimitedOnShortArrows()
    {
        IReadOnlyList<Point> points = ShapeBuilder.Build(ToolType.Arrow, new(0, 0), new(20, 0), constrain: false, lineWidth: 8);

        double headLength = (points[1] - points[2]).Length;
        Assert.True(headLength <= 20 * 0.4 + Tolerance, $"Head length {headLength} is too long for a 20-unit arrow.");
    }

    /// <summary>A rectangle is a closed path through its four corners.</summary>
    [Fact]
    public void Rectangle_IsClosedThroughCorners()
    {
        IReadOnlyList<Point> points = ShapeBuilder.Build(ToolType.Rectangle, new(10, 20), new(50, 80), constrain: false, lineWidth: 4);

        Assert.Equal([new Point(10, 20), new Point(50, 20), new Point(50, 80), new Point(10, 80), new Point(10, 20)], points);
    }

    /// <summary>Shift makes a square using the larger side, in the direction of the drag (any direction).</summary>
    [Theory]
    [InlineData(40, 60)]
    [InlineData(-40, 60)]
    [InlineData(40, -60)]
    [InlineData(-60, -40)]
    public void Rectangle_Constrained_IsSquareInDragDirection(double dx, double dy)
    {
        var start = new Point(100, 100);
        IReadOnlyList<Point> points = ShapeBuilder.Build(ToolType.Rectangle, start, start + new Vector(dx, dy), constrain: true, lineWidth: 4);

        Point opposite = points[2];
        Assert.Equal(60, Math.Abs(opposite.X - start.X), 3);
        Assert.Equal(60, Math.Abs(opposite.Y - start.Y), 3);
        Assert.Equal(Math.Sign(dx), Math.Sign(opposite.X - start.X));
        Assert.Equal(Math.Sign(dy), Math.Sign(opposite.Y - start.Y));
    }

    /// <summary>Shift on a perfectly horizontal drag still makes a square (not a flat line).</summary>
    [Fact]
    public void Rectangle_ConstrainedFlatDrag_StillSquare()
    {
        IReadOnlyList<Point> points = ShapeBuilder.Build(ToolType.Rectangle, new(0, 0), new(50, 0), constrain: true, lineWidth: 4);

        Assert.Equal(new Point(50, 50), points[2]);
    }

    /// <summary>Every ellipse point lies on the ellipse that fits the drag rectangle, and the path is closed.</summary>
    [Fact]
    public void Ellipse_PointsLieOnEllipseAndClose()
    {
        IReadOnlyList<Point> points = ShapeBuilder.Build(ToolType.Ellipse, new(0, 0), new(200, 100), constrain: false, lineWidth: 4);

        Assert.Equal(points[0], points[^1]);
        foreach (Point p in points)
        {
            // (x - cx)²/rx² + (y - cy)²/ry² = 1 for every point on the ellipse.
            double value = Math.Pow((p.X - 100) / 100, 2) + Math.Pow((p.Y - 50) / 50, 2);
            Assert.Equal(1, value, 6);
        }
    }

    /// <summary>Shift makes a circle.</summary>
    [Fact]
    public void Ellipse_Constrained_IsCircle()
    {
        IReadOnlyList<Point> points = ShapeBuilder.Build(ToolType.Ellipse, new(0, 0), new(200, 100), constrain: true, lineWidth: 4);

        double width = points.Max(p => p.X) - points.Min(p => p.X);
        double height = points.Max(p => p.Y) - points.Min(p => p.Y);
        Assert.Equal(width, height, 3);
    }

    /// <summary>A click (or tiny drag) makes no shape.</summary>
    [Theory]
    [InlineData(ToolType.Line)]
    [InlineData(ToolType.Arrow)]
    [InlineData(ToolType.Rectangle)]
    [InlineData(ToolType.Ellipse)]
    public void TinyDrag_MakesNothing(ToolType shape)
    {
        Assert.Empty(ShapeBuilder.Build(shape, new(10, 10), new(12, 11), constrain: false, lineWidth: 4));
    }
}
