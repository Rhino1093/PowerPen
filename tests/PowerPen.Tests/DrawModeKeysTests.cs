using System.Windows.Input;
using PowerPen.Input;

namespace PowerPen.Tests;

/// <summary>Tests for the Draw-mode single-key shortcuts.</summary>
public class DrawModeKeysTests
{
    /// <summary>Each shortcut maps to the right action.</summary>
    [Theory]
    [InlineData(Key.P, ModifierKeys.None, DrawModeAction.SelectPen)]
    [InlineData(Key.H, ModifierKeys.None, DrawModeAction.SelectHighlighter)]
    [InlineData(Key.E, ModifierKeys.None, DrawModeAction.SelectEraser)]
    [InlineData(Key.P, ModifierKeys.Shift, DrawModeAction.SelectPen)]          // Shift doesn't matter
    [InlineData(Key.OemOpenBrackets, ModifierKeys.None, DrawModeAction.SizeDown)]
    [InlineData(Key.OemMinus, ModifierKeys.None, DrawModeAction.SizeDown)]
    [InlineData(Key.OemCloseBrackets, ModifierKeys.None, DrawModeAction.SizeUp)]
    [InlineData(Key.OemPlus, ModifierKeys.Shift, DrawModeAction.SizeUp)]       // "+" is Shift+= on US keyboards
    [InlineData(Key.Z, ModifierKeys.Control, DrawModeAction.Undo)]
    [InlineData(Key.Y, ModifierKeys.Control, DrawModeAction.Redo)]
    [InlineData(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, DrawModeAction.Redo)]
    [InlineData(Key.Delete, ModifierKeys.None, DrawModeAction.ClearAll)]
    [InlineData(Key.Escape, ModifierKeys.None, DrawModeAction.ExitDrawMode)]
    public void Map_Shortcuts(Key key, ModifierKeys modifiers, DrawModeAction expected)
    {
        Assert.Equal(expected, DrawModeKeys.Map(key, modifiers, out _));
    }

    /// <summary>Number-row and numpad digits select colors by position.</summary>
    [Theory]
    [InlineData(Key.D1, 0)]
    [InlineData(Key.D8, 7)]
    [InlineData(Key.NumPad3, 2)]
    public void Map_Digits_SelectColor(Key key, int expectedIndex)
    {
        Assert.Equal(DrawModeAction.SelectColor, DrawModeKeys.Map(key, ModifierKeys.None, out int index));
        Assert.Equal(expectedIndex, index);
    }

    /// <summary>Ctrl/Alt combinations and unrelated keys don't trigger tools.</summary>
    [Theory]
    [InlineData(Key.P, ModifierKeys.Control)]
    [InlineData(Key.P, ModifierKeys.Alt)]
    [InlineData(Key.D1, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.Q, ModifierKeys.None)]
    [InlineData(Key.Z, ModifierKeys.None)]
    public void Map_NonShortcuts_ReturnNone(Key key, ModifierKeys modifiers)
    {
        Assert.Equal(DrawModeAction.None, DrawModeKeys.Map(key, modifiers, out _));
    }
}
