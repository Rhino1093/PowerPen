using System.Windows;
using System.Windows.Input;
using PowerPen.Capture;
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
    private readonly ScreenshotService _screenshots;
    private readonly FocusRestorer _focus = new();

    /// <summary>Creates the command set over the shared state, overlays, undo history, and screenshots.</summary>
    public AppCommands(AppState state, OverlayManager overlays, UndoManager undo, ScreenshotService screenshots)
    {
        _state = state;
        _overlays = overlays;
        _undo = undo;
        _screenshots = screenshots;
    }

    /// <summary>
    /// Takes a screenshot (drag an area or click a screen). Works in both modes, and afterwards puts focus back
    /// where it was: on the overlay if you were drawing, otherwise on the app you were using.
    /// </summary>
    public async Task TakeScreenshotAsync()
    {
        // Finish any text being typed so it's in the picture.
        _overlays.FinishTextEditing();

        bool wasDrawing = _state.IsDrawMode;
        var focus = new FocusRestorer(); // Separate from Draw mode's, which must keep its own remembered app.
        focus.Remember();

        await _screenshots.TakeAsync();

        // The selection windows took focus while they were up.
        if (wasDrawing && _state.IsDrawMode)
            _overlays.ActivateOverlayUnderCursor();
        else
            focus.Restore();
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

    /// <summary>Raised when the user asks for the Settings window; the app decides how to open it.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// Opens Settings. Switches to Pointer mode first: in Draw mode the overlays catch every click,
    /// so the Settings window would be visible but unclickable.
    /// </summary>
    public void OpenSettings()
    {
        _overlays.FinishTextEditing();
        EnterPointerMode();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Runs the Draw-mode shortcut for a key press, if there is one.</summary>
    /// <returns><c>true</c> if the key was a shortcut (so it shouldn't be processed further).</returns>
    public bool HandleDrawModeKey(Key key, ModifierKeys modifiers)
    {
        // Plain-letter shortcuts must never fire outside Draw mode, even if an overlay somehow kept focus.
        if (!_state.IsDrawMode)
            return false;

        DrawModeCommand command = DrawModeKeys.Map(key, modifiers);
        switch (command.Action)
        {
            case DrawModeAction.SelectTool: SelectTool(command.Tool); break;
            case DrawModeAction.SelectColor: SelectColor(command.ColorIndex); break;
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
