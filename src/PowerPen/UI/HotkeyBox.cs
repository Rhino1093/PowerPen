using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerPen.Input;

namespace PowerPen.UI;

/// <summary>
/// A box that records a key combination: click it and press the keys (e.g. Ctrl+Alt+D). Backspace or Delete turns
/// the hotkey off. Invalid combinations (like Shift+D) are allowed here and explained when Settings are saved.
/// </summary>
/// <remarks>
/// PowerPen's global hotkeys are paused while the Settings window is open; otherwise Windows would swallow those
/// combinations before they reach this box, and the current hotkey could never be re-recorded.
/// </remarks>
public class HotkeyBox : TextBox
{
    /// <summary>Identifies the <see cref="Combination"/> dependency property.</summary>
    public static readonly DependencyProperty CombinationProperty = DependencyProperty.Register(
        nameof(Combination), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (box, _) => ((HotkeyBox)box).ShowCombination()));

    /// <summary>Creates an empty recorder.</summary>
    public HotkeyBox()
    {
        IsReadOnly = true; // Typing is replaced by recording.
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        ToolTip = "Click here, then press the keys. Backspace turns this hotkey off.";
        ShowCombination();
    }

    /// <summary>The recorded combination, e.g. "Ctrl+Alt+D"; empty means off.</summary>
    public string Combination
    {
        get => (string)GetValue(CombinationProperty);
        set => SetValue(CombinationProperty, value);
    }

    /// <summary>Records the pressed combination.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // While Alt is held, WPF reports the real key in SystemKey.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;

        // Plain Tab moves to the next field and plain Esc reaches the window's Cancel button, as usual.
        if (modifiers == ModifierKeys.None && key is Key.Tab or Key.Escape)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;

        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            Combination = "";
        }
        else if (IsModifierKey(key))
        {
            // Only modifiers held so far: show progress, like "Ctrl+Alt+…".
            Text = $"{Hotkey.FormatModifiers(modifiers)}+…";
        }
        else
        {
            Combination = new Hotkey(modifiers, key).ToString();
            ShowCombination(); // Needed when the same combination is recorded again (no property change).
        }
    }

    /// <summary>Drops the "Ctrl+Alt+…" progress text once all keys are released without a main key.</summary>
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (Keyboard.Modifiers == ModifierKeys.None)
            ShowCombination();
    }

    /// <summary>Drops any progress text when focus leaves the box.</summary>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        ShowCombination();
    }

    /// <summary>Shows the recorded combination, or "Off".</summary>
    private void ShowCombination() => Text = string.IsNullOrWhiteSpace(Combination) ? "Off" : Combination;

    /// <summary>True for Ctrl, Alt, Shift, and Win keys.</summary>
    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}
