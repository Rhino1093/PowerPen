using System.Windows;
using System.Windows.Input;
using PowerPen.Input;
using PowerPen.Interop;
using PowerPen.Overlay;

namespace PowerPen.Core;

/// <summary>
/// Every user action lives here. The toolbar, hotkeys, Draw-mode keys, and (later) the tray menu all call these
/// methods, so each action behaves the same no matter how it was triggered.
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

    /// <summary>Steps through Small → Medium → Large → Small (toolbar button).</summary>
    public void CycleSize()
    {
        _state.PenSize = _state.PenSize == PenSize.Large ? PenSize.Small : _state.PenSize + 1;
    }

    /// <summary>Makes lines thinner (<paramref name="steps"/> &lt; 0) or thicker (&gt; 0), stopping at Small/Large.</summary>
    public void ChangeSize(int steps)
    {
        _state.PenSize = (PenSize)Math.Clamp((int)_state.PenSize + steps, (int)PenSize.Small, (int)PenSize.Large);
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
    /// Starts capturing the mouse for drawing. Shows the ink if it was hidden, remembers which app had focus,
    /// then focuses the overlay under the mouse so keys like Esc reach PowerPen.
    /// </summary>
    public void EnterDrawMode()
    {
        _state.IsInkVisible = true; // Drawing on hidden ink would be invisible, so drawing always shows it.
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

    /// <summary>Hides all ink (without deleting it), or shows it again. Hiding also stops drawing.</summary>
    public void ToggleInkVisible()
    {
        if (_state.IsInkVisible)
        {
            EnterPointerMode();
            _state.IsInkVisible = false;
        }
        else
        {
            _state.IsInkVisible = true;
        }
    }

    /// <summary>Reverses the last change.</summary>
    public void Undo() => _undo.Undo();

    /// <summary>Re-applies the last undone change.</summary>
    public void Redo() => _undo.Redo();

    /// <summary>Removes all ink from every monitor (undoable).</summary>
    public void ClearAll() => _overlays.ClearAll();

    /// <summary>Exits PowerPen.</summary>
    public void Quit() => Application.Current.Shutdown();

    /// <summary>Runs the Draw-mode shortcut for a key press, if there is one.</summary>
    /// <returns><c>true</c> if the key was a shortcut (so it shouldn't be processed further).</returns>
    public bool HandleDrawModeKey(Key key, ModifierKeys modifiers)
    {
        // Plain-letter shortcuts must never fire outside Draw mode, even if an overlay somehow kept focus.
        if (!_state.IsDrawMode)
            return false;

        switch (DrawModeKeys.Map(key, modifiers, out int colorIndex))
        {
            case DrawModeAction.SelectPen: SelectTool(ToolType.Pen); break;
            case DrawModeAction.SelectHighlighter: SelectTool(ToolType.Highlighter); break;
            case DrawModeAction.SelectEraser: SelectTool(ToolType.Eraser); break;
            case DrawModeAction.SelectColor: SelectColor(colorIndex); break;
            case DrawModeAction.SizeDown: ChangeSize(-1); break;
            case DrawModeAction.SizeUp: ChangeSize(+1); break;
            case DrawModeAction.Undo: Undo(); break;
            case DrawModeAction.Redo: Redo(); break;
            case DrawModeAction.ClearAll: ClearAll(); break;
            case DrawModeAction.ExitDrawMode: EnterPointerMode(); break;
            default: return false;
        }
        return true;
    }
}
