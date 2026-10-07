using System.Windows.Ink;

namespace PowerPen.Core;

/// <summary>A change that can be undone and redone.</summary>
public interface IUndoableAction
{
    /// <summary>Reverses the change.</summary>
    void Undo();

    /// <summary>Re-applies the change after it was undone.</summary>
    void Redo();
}

/// <summary>Strokes were added to and/or removed from one monitor's ink (drawing, erasing, or clearing).</summary>
/// <param name="target">The stroke collection that changed.</param>
/// <param name="added">Strokes that were added.</param>
/// <param name="removed">Strokes that were removed.</param>
public sealed class StrokesChangedAction(StrokeCollection target, StrokeCollection added, StrokeCollection removed) : IUndoableAction
{
    /// <summary>Removes the added strokes and puts the removed ones back.</summary>
    public void Undo()
    {
        RemoveIfPresent(added);
        AddIfMissing(removed);
    }

    /// <summary>Removes the removed strokes again and re-adds the added ones.</summary>
    public void Redo()
    {
        RemoveIfPresent(removed);
        AddIfMissing(added);
    }

    // StrokeCollection throws if you remove a stroke that isn't there or add one that already is.
    // Checking each stroke keeps undo from crashing if the collection was changed some other way.

    /// <summary>Removes each stroke that is currently in the target collection.</summary>
    private void RemoveIfPresent(StrokeCollection strokes)
    {
        foreach (Stroke stroke in strokes)
        {
            if (target.Contains(stroke))
                target.Remove(stroke);
        }
    }

    /// <summary>Adds each stroke that isn't already in the target collection.</summary>
    private void AddIfMissing(StrokeCollection strokes)
    {
        foreach (Stroke stroke in strokes)
        {
            if (!target.Contains(stroke))
                target.Add(stroke);
        }
    }
}

/// <summary>Several actions that undo and redo together as one step (e.g. one eraser drag, or Clear all).</summary>
/// <param name="actions">The actions in the order they happened.</param>
public sealed class CompositeAction(IReadOnlyList<IUndoableAction> actions) : IUndoableAction
{
    /// <summary>Undoes every action, newest first.</summary>
    public void Undo()
    {
        for (int i = actions.Count - 1; i >= 0; i--)
            actions[i].Undo();
    }

    /// <summary>Redoes every action, oldest first.</summary>
    public void Redo()
    {
        foreach (IUndoableAction action in actions)
            action.Redo();
    }
}
