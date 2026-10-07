using System.Windows.Input;

namespace PowerPen.Input;

/// <summary>A key combination such as Ctrl+Alt+D.</summary>
/// <param name="Modifiers">Ctrl/Alt/Shift/Win keys that must be held.</param>
/// <param name="Key">The main key.</param>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    /// <summary>Example combination used in error messages.</summary>
    private const string Example = "Ctrl+Alt+D";

    /// <summary>
    /// Parses text like "Ctrl+Alt+D" (any case, spaces allowed). Global hotkeys must include Ctrl, Alt, or Win;
    /// otherwise they would swallow normal typing in every app.
    /// </summary>
    /// <param name="text">The combination as written in settings.</param>
    /// <param name="hotkey">The parsed hotkey on success.</param>
    /// <param name="error">On failure: what's wrong and how to fix it.</param>
    public static bool TryParse(string? text, out Hotkey hotkey, out string? error)
    {
        hotkey = default;
        if (!TrySplit(text, out ModifierKeys modifiers, out List<string> keys, out error))
            return false;

        if (keys.Count == 0)
        {
            error = $"\"{text}\" has no key. Add one at the end, like {Example}.";
            return false;
        }
        if (keys.Count > 1)
        {
            error = $"\"{text}\" has more than one key. Use exactly one, like {Example}.";
            return false;
        }
        if (!TryParseKey(keys[0], out Key key))
        {
            error = $"\"{keys[0]}\" in \"{text}\" isn't a key name. Use a letter, a digit, or F1–F24.";
            return false;
        }
        if (key == Key.F12)
        {
            // From Microsoft's RegisterHotKey docs: F12 is reserved for debuggers at all times.
            // Registration succeeds, but the hotkey may never fire, which would be very confusing.
            error = $"\"{text}\" uses F12, which Windows reserves for debugging, so it may not work. Pick another key.";
            return false;
        }
        if (!HasRequiredModifier(modifiers))
        {
            error = $"\"{text}\" needs Ctrl, Alt, or Win, otherwise it would block normal typing.";
            return false;
        }

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    /// <summary>
    /// Parses modifier-only text like "Ctrl+Alt". Used for the color hotkeys, where the digit 1–8 is added automatically.
    /// </summary>
    /// <param name="text">The modifiers as written in settings.</param>
    /// <param name="modifiers">The parsed modifiers on success.</param>
    /// <param name="error">On failure: what's wrong and how to fix it.</param>
    public static bool TryParseModifiers(string? text, out ModifierKeys modifiers, out string? error)
    {
        if (!TrySplit(text, out modifiers, out List<string> keys, out error))
            return false;

        if (keys.Count > 0)
        {
            error = $"\"{text}\" should only contain Ctrl, Alt, Shift, or Win. The digit is added automatically.";
            return false;
        }
        if (!HasRequiredModifier(modifiers))
        {
            error = $"\"{text}\" needs Ctrl, Alt, or Win, otherwise it would block normal typing.";
            return false;
        }
        return true;
    }

    /// <summary>Formats modifiers in a fixed order, e.g. "Ctrl+Alt+Shift".</summary>
    public static string FormatModifiers(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return string.Join("+", parts);
    }

    /// <summary>Readable form that <see cref="TryParse"/> accepts back, e.g. "Ctrl+Alt+1".</summary>
    public override string ToString()
    {
        // WPF names the number-row keys D0–D9; show them as plain digits.
        string key = Key is >= Key.D0 and <= Key.D9 ? ((int)(Key - Key.D0)).ToString() : Key.ToString();
        string modifiers = FormatModifiers(Modifiers);
        return modifiers.Length == 0 ? key : $"{modifiers}+{key}";
    }

    /// <summary>Splits text on '+' into modifiers and the remaining (non-modifier) parts.</summary>
    private static bool TrySplit(string? text, out ModifierKeys modifiers, out List<string> keys, out string? error)
    {
        modifiers = ModifierKeys.None;
        keys = [];
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = $"The hotkey is empty. Use a combination like {Example}.";
            return false;
        }

        foreach (string raw in text.Split('+'))
        {
            string part = raw.Trim();
            if (part.Length == 0)
            {
                error = $"\"{text}\" has an empty part. Check for a doubled or trailing +.";
                return false;
            }

            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default: keys.Add(part); break;
            }
        }
        return true;
    }

    /// <summary>Parses one key name: a single digit, a letter, F1–F24, or another WPF key name such as "Home".</summary>
    private static bool TryParseKey(string text, out Key key)
    {
        key = Key.None;

        // A single digit means the number-row key.
        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            key = Key.D0 + (text[0] - '0');
            return true;
        }

        // Enum.TryParse would accept "65" (a raw enum value) or "A,B" (combined values); neither is a real key name.
        if (text.All(char.IsAsciiDigit) || text.Contains(','))
            return false;

        if (!Enum.TryParse(text, ignoreCase: true, out key) || !Enum.IsDefined(key))
            return false;

        // Modifier keys and internal values can't be the main key.
        return key is not (Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed
            or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin);
    }

    /// <summary>True if the modifiers include Ctrl, Alt, or Win (Shift alone isn't enough).</summary>
    private static bool HasRequiredModifier(ModifierKeys modifiers) =>
        (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;
}
