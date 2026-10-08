using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using PowerPen.Input;

namespace PowerPen.Settings;

/// <summary>
/// Everything the user can configure, as stored in settings.json. Anything missing from the file keeps these defaults.
/// Later phases add their own settings (screenshot folder, toolbar position, ...).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Most colors the toolbar (and the 1–8 keys) can hold.</summary>
    public const int MaxColors = 8;

    /// <summary>Default highlighter width multiplier.</summary>
    private const double DefaultHighlighterMultiplier = 3;

    /// <summary>Toolbar colors in order. Keys 1–8 select them.</summary>
    public List<ColorSetting> Colors { get; set; } = DefaultColors();

    /// <summary>Line widths for the three pen sizes.</summary>
    public PenSizeSettings PenSizes { get; set; } = new();

    /// <summary>Highlighter strokes are this many times wider than pen strokes of the same size.</summary>
    public double HighlighterWidthMultiplier { get; set; } = DefaultHighlighterMultiplier;

    /// <summary>System-wide hotkeys.</summary>
    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>
    /// Where screenshots are saved. Empty means the Downloads folder. Environment variables work,
    /// e.g. "%USERPROFILE%\Pictures\Screenshots".
    /// </summary>
    public string ScreenshotFolder { get; set; } = "";

    /// <summary>Also copy each screenshot to the clipboard, ready to paste.</summary>
    public bool CopyScreenshotToClipboard { get; set; } = true;

    /// <summary>Keep the toolbar out of screenshots and screen shares (Teams, Zoom).</summary>
    public bool HideToolbarFromCapture { get; set; } = true;

    /// <summary>Where the toolbar was left, and whether it was collapsed. Saved automatically.</summary>
    public ToolbarSettings Toolbar { get; set; } = new();

    /// <summary>A fully independent copy (changing the copy never changes this one).</summary>
    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this))!;

    /// <summary>
    /// Replaces every value with (a copy of) <paramref name="other"/>'s. Used to apply the Settings window's changes
    /// to the one settings object the whole app shares, so every part sees the new values.
    /// </summary>
    public void CopyFrom(AppSettings other)
    {
        AppSettings copy = other.Clone();
        Colors = copy.Colors;
        PenSizes = copy.PenSizes;
        HighlighterWidthMultiplier = copy.HighlighterWidthMultiplier;
        Hotkeys = copy.Hotkeys;
        ScreenshotFolder = copy.ScreenshotFolder;
        CopyScreenshotToClipboard = copy.CopyScreenshotToClipboard;
        HideToolbarFromCapture = copy.HideToolbarFromCapture;
        Toolbar = copy.Toolbar;
    }

    /// <summary>The 8 default colors.</summary>
    public static List<ColorSetting> DefaultColors() =>
    [
        new() { Name = "Red", Color = "#E53935" },
        new() { Name = "Orange", Color = "#FB8C00" },
        new() { Name = "Yellow", Color = "#FDD835" },
        new() { Name = "Green", Color = "#43A047" },
        new() { Name = "Blue", Color = "#1E88E5" },
        new() { Name = "Purple", Color = "#8E24AA" },
        new() { Name = "Black", Color = "#000000" },
        new() { Name = "White", Color = "#FFFFFF" },
    ];

    /// <summary>Parses a color like "#E53935" or "Red". Returns false if it isn't a valid color.</summary>
    public static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            // WPF's converter accepts #RGB, #RRGGBB, #AARRGGBB, and named colors.
            if (ColorConverter.ConvertFromString(text) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // Not a color; reported by the caller.
        }
        return false;
    }

    /// <summary>
    /// Replaces any invalid values with defaults and describes each fix in <paramref name="problems"/>,
    /// so a typo in settings.json never stops PowerPen from starting.
    /// </summary>
    public void Validate(List<string> problems)
    {
        ValidateColors(problems);
        ValidatePenSizes(problems);

        if (!double.IsFinite(HighlighterWidthMultiplier) || HighlighterWidthMultiplier < 1 || HighlighterWidthMultiplier > 10)
        {
            problems.Add($"\"highlighterWidthMultiplier\" must be a number from 1 to 10. Using {DefaultHighlighterMultiplier}.");
            HighlighterWidthMultiplier = DefaultHighlighterMultiplier;
        }

        ValidateHotkeys(problems);
        ValidateScreenshotFolder(problems);

        // Any saved position is fine here; whether it's still on a connected monitor is checked when the toolbar opens.
        Toolbar ??= new ToolbarSettings();
    }

    /// <summary>
    /// Checks that the screenshot folder is empty (Downloads) or a full path. Whether it exists is only checked
    /// when saving, because a network or USB drive may simply not be connected yet when PowerPen starts.
    /// </summary>
    private void ValidateScreenshotFolder(List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(ScreenshotFolder))
        {
            ScreenshotFolder = "";
            return;
        }

        string expanded = Environment.ExpandEnvironmentVariables(ScreenshotFolder);
        bool isFullPath;
        try
        {
            isFullPath = Path.IsPathFullyQualified(expanded) && expanded.IndexOfAny(Path.GetInvalidPathChars()) < 0;
        }
        catch (ArgumentException)
        {
            isFullPath = false;
        }

        if (!isFullPath)
        {
            problems.Add($"\"screenshotFolder\" (\"{ScreenshotFolder}\") must be a full folder path like C:\\Users\\you\\Pictures, " +
                         "or empty for Downloads. Using Downloads.");
            ScreenshotFolder = "";
        }
    }

    /// <summary>Checks the palette: 1–8 entries, each a valid color. Any problem restores all default colors.</summary>
    private void ValidateColors(List<string> problems)
    {
        // JSON can set a property to null even though the C# type says it can't be.
        if (Colors is null || Colors.Count is 0 or > MaxColors)
        {
            problems.Add($"\"colors\" must list 1 to {MaxColors} colors. Using the default colors.");
            Colors = DefaultColors();
            return;
        }

        for (int i = 0; i < Colors.Count; i++)
        {
            ColorSetting? entry = Colors[i];
            if (entry is null || !TryParseColor(entry.Color, out _))
            {
                problems.Add($"\"colors\" entry {i + 1} (\"{entry?.Color}\") isn't a valid color. Use a hex code like #E53935. Using the default colors.");
                Colors = DefaultColors();
                return;
            }

            // The name is only used for tooltips; fall back to the color code.
            if (string.IsNullOrWhiteSpace(entry.Name))
                entry.Name = entry.Color;
        }
    }

    /// <summary>Checks that each pen size is a sensible width.</summary>
    private void ValidatePenSizes(List<string> problems)
    {
        static bool IsValid(double width) => double.IsFinite(width) && width >= 0.5 && width <= 100;

        if (PenSizes is null || !IsValid(PenSizes.Small) || !IsValid(PenSizes.Medium) || !IsValid(PenSizes.Large))
        {
            var defaults = new PenSizeSettings();
            problems.Add($"\"penSizes\" values must be numbers from 0.5 to 100. Using {defaults.Small}, {defaults.Medium}, {defaults.Large}.");
            PenSizes = defaults;
        }
    }

    /// <summary>
    /// Checks each hotkey (empty means "turned off"), then makes sure no combination is used twice,
    /// including the color hotkeys (modifiers + 1–8).
    /// </summary>
    private void ValidateHotkeys(List<string> problems)
    {
        var defaults = new HotkeySettings();
        if (Hotkeys is null)
        {
            problems.Add("\"hotkeys\" is missing. Using the default hotkeys.");
            Hotkeys = defaults;
        }

        Hotkeys.ToggleDraw = CheckHotkey("toggleDraw", Hotkeys.ToggleDraw, defaults.ToggleDraw, problems);
        Hotkeys.ToggleInkVisible = CheckHotkey("toggleInkVisible", Hotkeys.ToggleInkVisible, defaults.ToggleInkVisible, problems);
        Hotkeys.ClearAll = CheckHotkey("clearAll", Hotkeys.ClearAll, defaults.ClearAll, problems);
        Hotkeys.Quit = CheckHotkey("quit", Hotkeys.Quit, defaults.Quit, problems);

        if (!string.IsNullOrWhiteSpace(Hotkeys.ColorModifiers)
            && !Hotkey.TryParseModifiers(Hotkeys.ColorModifiers, out _, out string? modifierError))
        {
            problems.Add($"\"hotkeys.colorModifiers\": {modifierError} Using {defaults.ColorModifiers}.");
            Hotkeys.ColorModifiers = defaults.ColorModifiers;
        }

        // Find combinations used twice. The first one listed keeps it; later ones are turned off.
        var owners = new Dictionary<Hotkey, string>();

        bool Claim(string name, string combo)
        {
            if (!Hotkey.TryParse(combo, out Hotkey hotkey, out _))
                return true; // Empty (turned off); nothing to claim.
            if (owners.TryGetValue(hotkey, out string? owner))
            {
                problems.Add($"{hotkey} is set for both \"hotkeys.{owner}\" and \"hotkeys.{name}\". Only {owner} will work; change one of them.");
                return false;
            }
            owners[hotkey] = name;
            return true;
        }

        if (!Claim("toggleDraw", Hotkeys.ToggleDraw)) Hotkeys.ToggleDraw = "";
        if (!Claim("toggleInkVisible", Hotkeys.ToggleInkVisible)) Hotkeys.ToggleInkVisible = "";
        if (!Claim("clearAll", Hotkeys.ClearAll)) Hotkeys.ClearAll = "";
        if (!Claim("quit", Hotkeys.Quit)) Hotkeys.Quit = "";

        if (Hotkey.TryParseModifiers(Hotkeys.ColorModifiers, out ModifierKeys colorModifiers, out _))
        {
            for (int i = 0; i < Colors.Count; i++)
            {
                if (!Claim("colorModifiers", new Hotkey(colorModifiers, Key.D1 + i).ToString()))
                {
                    Hotkeys.ColorModifiers = "";
                    break;
                }
            }
        }
    }

    /// <summary>Returns the hotkey text if it's valid or empty; otherwise reports it and returns the default.</summary>
    private static string CheckHotkey(string name, string? combo, string defaultCombo, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(combo))
            return ""; // Turned off on purpose.

        if (Hotkey.TryParse(combo, out _, out string? error))
            return combo;

        problems.Add($"\"hotkeys.{name}\": {error} Using {defaultCombo}.");
        return defaultCombo;
    }
}

/// <summary>Toolbar placement, remembered between runs.</summary>
public sealed class ToolbarSettings
{
    /// <summary>Left edge in physical screen pixels, or null for the default spot.</summary>
    public int? X { get; set; }

    /// <summary>Top edge in physical screen pixels, or null for the default spot.</summary>
    public int? Y { get; set; }

    /// <summary>True if the toolbar was collapsed to a single button.</summary>
    public bool Collapsed { get; set; }
}

/// <summary>One palette color.</summary>
public sealed class ColorSetting
{
    /// <summary>Name shown in the tooltip, e.g. "Red".</summary>
    public string Name { get; set; } = "";

    /// <summary>Color code, e.g. "#E53935".</summary>
    public string Color { get; set; } = "";
}

/// <summary>Line widths (in WPF units, 1 = one pixel at 100% scaling) for the three pen sizes.</summary>
public sealed class PenSizeSettings
{
    /// <summary>Thin line.</summary>
    public double Small { get; set; } = 2;

    /// <summary>Default line.</summary>
    public double Medium { get; set; } = 4;

    /// <summary>Thick line.</summary>
    public double Large { get; set; } = 8;
}

/// <summary>
/// System-wide hotkeys, written like "Ctrl+Alt+D". An empty value turns that hotkey off.
/// </summary>
public sealed class HotkeySettings
{
    /// <summary>Switch between Draw and Pointer mode.</summary>
    public string ToggleDraw { get; set; } = "Ctrl+Alt+D";

    /// <summary>Hide or show all ink ("I" for ink). Not Ctrl+Alt+H: another app on the company image already owns it.</summary>
    public string ToggleInkVisible { get; set; } = "Ctrl+Alt+I";

    /// <summary>Clear all ink.</summary>
    public string ClearAll { get; set; } = "Ctrl+Alt+C";

    /// <summary>Quit PowerPen.</summary>
    public string Quit { get; set; } = "Ctrl+Alt+Q";

    /// <summary>Modifiers for the color hotkeys; the digit 1–8 is added automatically (e.g. Ctrl+Alt+1 = color 1).</summary>
    public string ColorModifiers { get; set; } = "Ctrl+Alt";
}
