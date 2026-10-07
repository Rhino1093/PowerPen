using CommunityToolkit.Mvvm.ComponentModel;

namespace PowerPen.Core;

/// <summary>
/// One app-wide undo/redo history covering every monitor, so Undo always reverses the last thing you did,
/// wherever you did it.
/// </summary>
public sealed class UndoManager : ObservableObject
{
    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();
    private List<IUndoableAction>? _openGroup;
    private bool _isApplying;

    /// <summary>True if there is something to undo.</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>True if there is something to redo.</summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Adds a change to the history. Ignored while an undo or redo is running, because those changes
    /// fire the same "strokes changed" events as user edits and must not be recorded as new steps.
    /// </summary>
    public void Record(IUndoableAction action)
    {
        if (_isApplying)
            return;

        if (_openGroup != null)
            _openGroup.Add(action);
        else
            Push(action);
    }

    /// <summary>
    /// Starts collecting changes into a single undo step (e.g. everything erased in one drag).
    /// If a group is already open, changes keep going into it.
    /// </summary>
    /// <remarks>
    /// Groups don't nest on purpose: a missed "end" (e.g. the mouse released outside the window)
    /// is fixed by the next <see cref="EndGroup"/> instead of leaving history stuck open forever.
    /// </remarks>
    public void BeginGroup() => _openGroup ??= [];

    /// <summary>Closes the open group and records it as one step. Empty groups record nothing.</summary>
    public void EndGroup()
    {
        if (_openGroup == null)
            return;

        List<IUndoableAction> group = _openGroup;
        _openGroup = null;

        if (group.Count == 1)
            Push(group[0]);
        else if (group.Count > 1)
            Push(new CompositeAction(group));
    }

    /// <summary>Reverses the most recent step.</summary>
    public void Undo()
    {
        EndGroup();
        if (!CanUndo)
            return;

        IUndoableAction action = _undoStack.Pop();
        Apply(action.Undo);
        _redoStack.Push(action);
        NotifyChanged();
    }

    /// <summary>Re-applies the most recently undone step.</summary>
    public void Redo()
    {
        EndGroup();
        if (!CanRedo)
            return;

        IUndoableAction action = _redoStack.Pop();
        Apply(action.Redo);
        _undoStack.Push(action);
        NotifyChanged();
    }

    /// <summary>Forgets all history (e.g. after the overlays are rebuilt and the old strokes are gone).</summary>
    public void Clear()
    {
        _openGroup = null;
        _undoStack.Clear();
        _redoStack.Clear();
        NotifyChanged();
    }

    /// <summary>Adds a finished step. A new change makes the redo history invalid, so it's cleared.</summary>
    private void Push(IUndoableAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear();
        NotifyChanged();
    }

    /// <summary>Runs an undo/redo operation with recording switched off.</summary>
    private void Apply(Action operation)
    {
        _isApplying = true;
        try
        {
            operation();
        }
        finally
        {
            _isApplying = false;
        }
    }

    /// <summary>Tells the UI that CanUndo/CanRedo may have changed.</summary>
    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }
}
