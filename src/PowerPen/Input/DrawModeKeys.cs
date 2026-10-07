using System.Windows.Input;
using PowerPen.Core;

namespace PowerPen.Input;

/// <summary>What a Draw-mode key press should do.</summary>
public enum DrawModeAction
{
    /// <summary>Not a shortcut.</summary>
    None,

    /// <summary>Switch tool (see <see cref="DrawModeCommand.Tool"/>).</summary>
    SelectTool,

    /// <summary>1–9: color by position (see <see cref="DrawModeCommand.ColorIndex"/>).</summary>
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

/// <summary>The result of mapping a key press: an action plus its details.</summary>
/// <param name="Action">What to do.</param>
/// <param name="Tool">For <see cref="DrawModeAction.SelectTool"/>: which tool.</param>
/// <param name="ColorIndex">For <see cref="DrawModeAction.SelectColor"/>: zero-based palette position.</param>
public readonly record struct DrawModeCommand(DrawModeAction Action, ToolType Tool = ToolType.Pen, int ColorIndex = -1)
{
    /// <summary>Not a shortcut.</summary>
    public static DrawModeCommand None { get; } = new(DrawModeAction.None);
}

/// <summary>
/// Single-key shortcuts that only work while drawing, i.e. while a PowerPen overlay has keyboard focus and no
/// text box is open. They can never interfere with typing in other apps, so unlike global hotkeys they aren't
/// configurable.
/// </summary>
/// <remarks>
/// P pen · H highlighter · E eraser · L line · A arrow · R rectangle · O ellipse · T text ·
/// 1–9 colors · [ ] or - = size · Ctrl+Z undo · Ctrl+Y / Ctrl+Shift+Z redo · Delete clear all · Esc stop drawing.
/// </remarks>
public static class DrawModeKeys
{
    /// <summary>Maps a key press to its Draw-mode command.</summary>
    /// <param name="key">The key pressed.</param>
    /// <param name="modifiers">Modifier keys held at the time.</param>
    public static DrawModeCommand Map(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Control)
        {
            return key switch
            {
                Key.Z => new(DrawModeAction.Undo),
                Key.Y => new(DrawModeAction.Redo),
                _ => DrawModeCommand.None,
            };
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            return key == Key.Z ? new(DrawModeAction.Redo) : DrawModeCommand.None;

        // Single keys work with or without Shift (no shortcut depends on it), but not with Ctrl/Alt/Win,
        // so combinations meant for other things don't trigger tools by accident.
        if ((modifiers & ~ModifierKeys.Shift) != ModifierKeys.None)
            return DrawModeCommand.None;

        return key switch
        {
            Key.P => Tool(ToolType.Pen),
            Key.H => Tool(ToolType.Highlighter),
            Key.E => Tool(ToolType.Eraser),
            Key.L => Tool(ToolType.Line),
            Key.A => Tool(ToolType.Arrow),
            Key.R => Tool(ToolType.Rectangle),
            Key.O => Tool(ToolType.Ellipse),
            Key.T => Tool(ToolType.Text),
            >= Key.D1 and <= Key.D9 => new(DrawModeAction.SelectColor, ColorIndex: key - Key.D1),
            >= Key.NumPad1 and <= Key.NumPad9 => new(DrawModeAction.SelectColor, ColorIndex: key - Key.NumPad1),
            Key.OemOpenBrackets or Key.OemMinus or Key.Subtract => new(DrawModeAction.SizeDown),
            Key.OemCloseBrackets or Key.OemPlus or Key.Add => new(DrawModeAction.SizeUp),
            Key.Delete => new(DrawModeAction.ClearAll),
            Key.Escape => new(DrawModeAction.ExitDrawMode),
            _ => DrawModeCommand.None,
        };
    }

    /// <summary>Shorthand for a tool-switch command.</summary>
    private static DrawModeCommand Tool(ToolType tool) => new(DrawModeAction.SelectTool, tool);
}
