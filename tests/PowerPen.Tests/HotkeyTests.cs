using System.Windows.Input;
using PowerPen.Input;

namespace PowerPen.Tests;

/// <summary>Tests for parsing and formatting hotkey text like "Ctrl+Alt+D".</summary>
public class HotkeyTests
{
    /// <summary>Valid combinations parse to the expected modifiers and key.</summary>
    [Theory]
    [InlineData("Ctrl+Alt+D", ModifierKeys.Control | ModifierKeys.Alt, Key.D)]
    [InlineData(" ctrl + alt + d ", ModifierKeys.Control | ModifierKeys.Alt, Key.D)]
    [InlineData("Control+Shift+F5", ModifierKeys.Control | ModifierKeys.Shift, Key.F5)]
    [InlineData("Ctrl+Alt+1", ModifierKeys.Control | ModifierKeys.Alt, Key.D1)]
    [InlineData("Win+Alt+Home", ModifierKeys.Windows | ModifierKeys.Alt, Key.Home)]
    [InlineData("Alt+F11", ModifierKeys.Alt, Key.F11)]
    public void TryParse_Valid(string text, ModifierKeys modifiers, Key key)
    {
        Assert.True(Hotkey.TryParse(text, out Hotkey hotkey, out string? error), error);
        Assert.Equal(new Hotkey(modifiers, key), hotkey);
    }

    /// <summary>Invalid text is rejected with an explanation.</summary>
    [Theory]
    [InlineData("")]                    // empty
    [InlineData("Ctrl+Alt")]            // no key
    [InlineData("Shift+D")]             // would block typing capital D
    [InlineData("D")]                   // would block typing d
    [InlineData("Ctrl+D+E")]            // two keys
    [InlineData("Ctrl+Alt+Foo")]        // not a key
    [InlineData("Ctrl+Alt+65")]         // raw number, not a key name
    [InlineData("Ctrl++D")]             // empty part
    [InlineData("Ctrl+Alt+LeftCtrl")]   // modifier as the main key
    [InlineData("Ctrl+F12")]            // reserved by Windows for debuggers
    public void TryParse_Invalid(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>Formatting is readable and parses back to the same hotkey.</summary>
    [Theory]
    [InlineData("ctrl+alt+d", "Ctrl+Alt+D")]
    [InlineData("alt+ctrl+1", "Ctrl+Alt+1")]
    [InlineData("shift+win+ctrl+F2", "Ctrl+Shift+Win+F2")]
    public void ToString_IsNormalizedAndRoundTrips(string text, string expected)
    {
        Assert.True(Hotkey.TryParse(text, out Hotkey hotkey, out _));
        Assert.Equal(expected, hotkey.ToString());

        Assert.True(Hotkey.TryParse(hotkey.ToString(), out Hotkey reparsed, out _));
        Assert.Equal(hotkey, reparsed);
    }

    /// <summary>Modifier-only text (used for color hotkeys) accepts only modifiers, and needs Ctrl, Alt, or Win.</summary>
    [Theory]
    [InlineData("Ctrl+Alt", true)]
    [InlineData("Ctrl+Alt+Shift", true)]
    [InlineData("Win", true)]
    [InlineData("Shift", false)]
    [InlineData("Ctrl+Alt+D", false)]
    [InlineData("", false)]
    public void TryParseModifiers(string text, bool valid)
    {
        Assert.Equal(valid, Hotkey.TryParseModifiers(text, out _, out _));
    }
}
