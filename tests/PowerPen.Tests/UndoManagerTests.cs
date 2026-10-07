using System.Windows.Ink;
using System.Windows.Input;
using PowerPen.Core;

namespace PowerPen.Tests;

/// <summary>Tests for <see cref="UndoManager"/>, wired to a stroke collection the same way the overlays wire it.</summary>
public class UndoManagerTests
{
    private readonly UndoManager _undo = new();
    private readonly StrokeCollection _strokes = new();

    /// <summary>Connects the collection to the undo history exactly like OverlayManager does.</summary>
    public UndoManagerTests()
    {
        _strokes.StrokesChanged += (_, e) =>
        {
            if (e.Added.Count > 0 || e.Removed.Count > 0)
                _undo.Record(new StrokesChangedAction(_strokes, e.Added, e.Removed));
        };
    }

    /// <summary>Undo removes a drawn stroke; Redo puts it back.</summary>
    [Fact]
    public void UndoAndRedo_DrawnStroke()
    {
        Stroke stroke = NewStroke();
        _strokes.Add(stroke);

        _undo.Undo();
        Assert.Empty(_strokes);
        Assert.False(_undo.CanUndo);
        Assert.True(_undo.CanRedo);

        _undo.Redo();
        Assert.Same(stroke, Assert.Single(_strokes));
    }

    /// <summary>Undoing an erase brings the erased stroke back.</summary>
    [Fact]
    public void Undo_ErasedStroke_RestoresIt()
    {
        Stroke stroke = NewStroke();
        _strokes.Add(stroke);
        _strokes.Remove(stroke);

        _undo.Undo();
        Assert.Same(stroke, Assert.Single(_strokes));
    }

    /// <summary>Everything erased in one drag undoes as one step.</summary>
    [Fact]
    public void Group_UndoesAsOneStep()
    {
        Stroke a = NewStroke(), b = NewStroke();
        _strokes.Add(a);
        _strokes.Add(b);

        _undo.BeginGroup();
        _strokes.Remove(a);
        _strokes.Remove(b);
        _undo.EndGroup();

        _undo.Undo();
        Assert.Equal(2, _strokes.Count);

        // The two adds are still separately undoable.
        _undo.Undo();
        _undo.Undo();
        Assert.Empty(_strokes);
    }

    /// <summary>A group with no changes (e.g. a click on empty space with the eraser) adds no undo step.</summary>
    [Fact]
    public void EmptyGroup_RecordsNothing()
    {
        _undo.BeginGroup();
        _undo.EndGroup();
        Assert.False(_undo.CanUndo);
    }

    /// <summary>Making a new change after undoing throws away the redo history.</summary>
    [Fact]
    public void NewChange_ClearsRedo()
    {
        _strokes.Add(NewStroke());
        _undo.Undo();
        Assert.True(_undo.CanRedo);

        _strokes.Add(NewStroke());
        Assert.False(_undo.CanRedo);
    }

    /// <summary>The stroke changes caused by undo itself must not be recorded as new steps.</summary>
    [Fact]
    public void Undo_DoesNotRecordItsOwnChanges()
    {
        _strokes.Add(NewStroke());
        _undo.Undo();

        // If undo's own removal had been recorded, there would be something left to undo.
        Assert.False(_undo.CanUndo);
    }

    /// <summary>Undo starts by closing a group left open by an interrupted gesture.</summary>
    [Fact]
    public void Undo_ClosesOpenGroupFirst()
    {
        _undo.BeginGroup();
        _strokes.Add(NewStroke());

        _undo.Undo();
        Assert.Empty(_strokes);
    }

    /// <summary>Clear forgets all history.</summary>
    [Fact]
    public void Clear_ForgetsHistory()
    {
        _strokes.Add(NewStroke());
        _undo.Clear();
        Assert.False(_undo.CanUndo);
        Assert.False(_undo.CanRedo);
    }

    /// <summary>Creates a simple two-point stroke.</summary>
    private static Stroke NewStroke() =>
        new(new StylusPointCollection([new StylusPoint(0, 0), new StylusPoint(10, 10)]));
}
