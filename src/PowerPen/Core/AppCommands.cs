using System.Windows;
using PowerPen.Interop;
using PowerPen.Overlay;

namespace PowerPen.Core;

/// <summary>
/// Every user action lives here. The toolbar, hotkeys, and (later) the tray menu all call these methods,
/// so each action behaves the same no matter how it was triggered.
/// </summary>
public sealed class AppCommands
{
    private readonly AppState _state;
    private readonly OverlayManager _overlays;
    private readonly UndoManager _undo;
    private readonly FocusRestorer _focus = new();

    /// <summary>Creates the command set over the shared state, overlays, and undo history.</summary>
    public AppCommands(AppState state, OverlayManager overlays, UndoManager undo)
    {
        _state = state;
        _overlays = overlays;
        _undo = undo;
    }

    /// <summary>Selects a drawing tool and starts drawing with it.</summary>
    public void SelectTool(ToolType tool)
    {
        _state.CurrentTool = tool;
        EnterDrawMode();
    }

    /// <summary>
    /// Selects a palette color and starts drawing with it. If the eraser was active, switches to the pen,
    /// because picking a color means you want to draw.
    /// </summary>
    public void SelectColor(int index)
    {
        if (index < 0 || index >= _state.Palette.Count)
            return;

        _state.ColorIndex = index;
        if (_state.CurrentTool == ToolType.Eraser)
            _state.CurrentTool = ToolType.Pen;
        EnterDrawMode();
    }

    /// <summary>Steps through Small → Medium → Large → Small.</summary>
    public void CycleSize()
    {
        _state.PenSize = _state.PenSize switch
        {
            PenSize.Small => PenSize.Medium,
            PenSize.Medium => PenSize.Large,
            _ => PenSize.Small,
        };
    }

    /// <summary>Switches between Draw and Pointer mode.</summary>
    public void ToggleDrawMode()
    {
        if (_state.IsDrawMode)
            EnterPointerMode();
        else
            EnterDrawMode();
    }

    /// <summary>
    /// Starts capturing the mouse for drawing. Remembers which app had focus, then focuses the overlay
    /// under the mouse so keys like Esc reach PowerPen.
    /// </summary>
    public void EnterDrawMode()
    {
        if (_state.IsDrawMode)
            return;

        _focus.Remember();
        _state.IsDrawMode = true;
        _overlays.ActivateOverlayUnderCursor();
    }

    /// <summary>Stops drawing: clicks go to your apps again, and focus returns to the app you were in.</summary>
    public void EnterPointerMode()
    {
        if (!_state.IsDrawMode)
            return;

        _state.IsDrawMode = false;
        _focus.Restore();
    }

    /// <summary>Reverses the last change.</summary>
    public void Undo() => _undo.Undo();

    /// <summary>Re-applies the last undone change.</summary>
    public void Redo() => _undo.Redo();

    /// <summary>Removes all ink from every monitor (undoable).</summary>
    public void ClearAll() => _overlays.ClearAll();

    /// <summary>Exits PowerPen.</summary>
    public void Quit() => Application.Current.Shutdown();
}
