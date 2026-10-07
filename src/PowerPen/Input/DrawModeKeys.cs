using System.Windows.Input;

namespace PowerPen.Input;

/// <summary>What a Draw-mode key press should do.</summary>
public enum DrawModeAction
{
    /// <summary>Not a shortcut.</summary>
    None,

    /// <summary>P: pen.</summary>
    SelectPen,

    /// <summary>H: highlighter.</summary>
    SelectHighlighter,

    /// <summary>E: eraser.</summary>
    SelectEraser,

    /// <summary>1–9: color by position.</summary>
    SelectColor,

    /// <summary>[ or -: thinner.</summary>
    SizeDown,

    /// <summary>] or =: thicker.</summary>
    SizeUp,

    /// <summary>Ctrl+Z.</summary>
    Undo,

    /// <summary>Ctrl+Y or Ctrl+Shift+Z.</summary>
    Redo,

    /// <summary>Delete.</summary>
    ClearAll,

    /// <summary>Esc: back to Pointer mode.</summary>
    ExitDrawMode,
}

/// <summary>
/// Single-key shortcuts that only work while drawing, i.e. while a PowerPen overlay has keyboard focus.
/// They can never interfere with typing in other apps, so unlike global hotkeys they aren't configurable.
/// </summary>
public static class DrawModeKeys
{
    /// <summary>Maps a key press to its Draw-mode action.</summary>
    /// <param name="key">The key pressed.</param>
    /// <param name="modifiers">Modifier keys held at the time.</param>
    /// <param name="colorIndex">For <see cref="DrawModeAction.SelectColor"/>: zero-based palette position; otherwise -1.</param>
    public static DrawModeAction Map(Key key, ModifierKeys modifiers, out int colorIndex)
    {
        colorIndex = -1;

        if (modifiers == ModifierKeys.Control)
            return key switch { Key.Z => DrawModeAction.Undo, Key.Y => DrawModeAction.Redo, _ => DrawModeAction.None };

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            return key == Key.Z ? DrawModeAction.Redo : DrawModeAction.None;

        // Single keys work with or without Shift (no shortcut depends on it), but not with Ctrl/Alt/Win,
        // so combinations meant for other things don't trigger tools by accident.
        if ((modifiers & ~ModifierKeys.Shift) != ModifierKeys.None)
            return DrawModeAction.None;

        switch (key)
        {
            case Key.P: return DrawModeAction.SelectPen;
            case Key.H: return DrawModeAction.SelectHighlighter;
            case Key.E: return DrawModeAction.SelectEraser;
            case >= Key.D1 and <= Key.D9:
                colorIndex = key - Key.D1;
                return DrawModeAction.SelectColor;
            case >= Key.NumPad1 and <= Key.NumPad9:
                colorIndex = key - Key.NumPad1;
                return DrawModeAction.SelectColor;
            case Key.OemOpenBrackets or Key.OemMinus or Key.Subtract: return DrawModeAction.SizeDown;
            case Key.OemCloseBrackets or Key.OemPlus or Key.Add: return DrawModeAction.SizeUp;
            case Key.Delete: return DrawModeAction.ClearAll;
            case Key.Escape: return DrawModeAction.ExitDrawMode;
            default: return DrawModeAction.None;
        }
    }
}
