using System.Windows.Input;
using PowerPen.Core;
using PowerPen.Input;

namespace PowerPen.Tests;

/// <summary>Tests for the Draw-mode single-key shortcuts.</summary>
public class DrawModeKeysTests
{
    /// <summary>Each tool letter selects its tool, with or without Shift.</summary>
    [Theory]
    [InlineData(Key.P, ToolType.Pen)]
    [InlineData(Key.H, ToolType.Highlighter)]
    [InlineData(Key.E, ToolType.Eraser)]
    [InlineData(Key.L, ToolType.Line)]
    [InlineData(Key.A, ToolType.Arrow)]
    [InlineData(Key.R, ToolType.Rectangle)]
    [InlineData(Key.O, ToolType.Ellipse)]
    [InlineData(Key.T, ToolType.Text)]
    public void Map_ToolLetters(Key key, ToolType expected)
    {
        Assert.Equal(new DrawModeCommand(DrawModeAction.SelectTool, expected), DrawModeKeys.Map(key, ModifierKeys.None));
        Assert.Equal(new DrawModeCommand(DrawModeAction.SelectTool, expected), DrawModeKeys.Map(key, ModifierKeys.Shift));
    }

    /// <summary>Each non-tool shortcut maps to the right action.</summary>
    [Theory]
    [InlineData(Key.OemOpenBrackets, ModifierKeys.None, DrawModeAction.SizeDown)]
    [InlineData(Key.OemMinus, ModifierKeys.None, DrawModeAction.SizeDown)]
    [InlineData(Key.OemCloseBrackets, ModifierKeys.None, DrawModeAction.SizeUp)]
    [InlineData(Key.OemPlus, ModifierKeys.Shift, DrawModeAction.SizeUp)]       // "+" is Shift+= on US keyboards
    [InlineData(Key.Z, ModifierKeys.Control, DrawModeAction.Undo)]
    [InlineData(Key.Y, ModifierKeys.Control, DrawModeAction.Redo)]
    [InlineData(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, DrawModeAction.Redo)]
    [InlineData(Key.Delete, ModifierKeys.None, DrawModeAction.ClearAll)]
    [InlineData(Key.Escape, ModifierKeys.None, DrawModeAction.ExitDrawMode)]
    public void Map_Actions(Key key, ModifierKeys modifiers, DrawModeAction expected)
    {
        Assert.Equal(expected, DrawModeKeys.Map(key, modifiers).Action);
    }

    /// <summary>Number-row and numpad digits select colors by position.</summary>
    [Theory]
    [InlineData(Key.D1, 0)]
    [InlineData(Key.D8, 7)]
    [InlineData(Key.NumPad3, 2)]
    public void Map_Digits_SelectColor(Key key, int expectedIndex)
    {
        DrawModeCommand command = DrawModeKeys.Map(key, ModifierKeys.None);
        Assert.Equal(DrawModeAction.SelectColor, command.Action);
        Assert.Equal(expectedIndex, command.ColorIndex);
    }

    /// <summary>Ctrl/Alt combinations and unrelated keys don't trigger anything.</summary>
    [Theory]
    [InlineData(Key.P, ModifierKeys.Control)]
    [InlineData(Key.A, ModifierKeys.Control)]   // Ctrl+A is "select all" elsewhere, not the arrow tool.
    [InlineData(Key.P, ModifierKeys.Alt)]
    [InlineData(Key.D1, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.Q, ModifierKeys.None)]
    [InlineData(Key.Z, ModifierKeys.None)]
    public void Map_NonShortcuts_ReturnNone(Key key, ModifierKeys modifiers)
    {
        Assert.Equal(DrawModeCommand.None, DrawModeKeys.Map(key, modifiers));
    }
}
