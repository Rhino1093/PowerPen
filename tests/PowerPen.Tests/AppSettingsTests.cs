using PowerPen.Settings;

namespace PowerPen.Tests;

/// <summary>Tests for settings validation: bad values are replaced with defaults and explained.</summary>
public class AppSettingsTests
{
    private readonly List<string> _problems = [];

    /// <summary>The defaults are valid as-is.</summary>
    [Fact]
    public void Defaults_HaveNoProblems()
    {
        new AppSettings().Validate(_problems);
        Assert.Empty(_problems);
    }

    /// <summary>More than 8 colors falls back to the default palette.</summary>
    [Fact]
    public void TooManyColors_UsesDefaults()
    {
        var settings = new AppSettings();
        settings.Colors.AddRange(AppSettings.DefaultColors());

        settings.Validate(_problems);

        Assert.Equal(AppSettings.MaxColors, settings.Colors.Count);
        Assert.Single(_problems);
    }

    /// <summary>An unreadable color falls back to the default palette and names the bad entry.</summary>
    [Fact]
    public void InvalidColor_UsesDefaultsAndNamesEntry()
    {
        var settings = new AppSettings();
        settings.Colors[2].Color = "#GGG";

        settings.Validate(_problems);

        Assert.Equal("#FDD835", settings.Colors[2].Color);
        Assert.Contains("entry 3", Assert.Single(_problems));
    }

    /// <summary>A color without a name uses its code as the tooltip name; that's not a problem.</summary>
    [Fact]
    public void MissingColorName_UsesColorCode()
    {
        var settings = new AppSettings();
        settings.Colors[0].Name = "";

        settings.Validate(_problems);

        Assert.Equal("#E53935", settings.Colors[0].Name);
        Assert.Empty(_problems);
    }

    /// <summary>Out-of-range pen sizes fall back to defaults.</summary>
    [Fact]
    public void InvalidPenSize_UsesDefaults()
    {
        var settings = new AppSettings();
        settings.PenSizes.Large = 500;

        settings.Validate(_problems);

        Assert.Equal(8, settings.PenSizes.Large);
        Assert.Single(_problems);
    }

    /// <summary>A section set to null in the JSON falls back to defaults.</summary>
    [Fact]
    public void NullSections_UseDefaults()
    {
        var settings = new AppSettings { Colors = null!, PenSizes = null!, Hotkeys = null! };

        settings.Validate(_problems);

        Assert.Equal(AppSettings.MaxColors, settings.Colors.Count);
        Assert.NotNull(settings.PenSizes);
        Assert.Equal("Ctrl+Alt+D", settings.Hotkeys.ToggleDraw);
    }

    /// <summary>An invalid hotkey falls back to its default and the message explains why.</summary>
    [Fact]
    public void InvalidHotkey_UsesDefault()
    {
        var settings = new AppSettings();
        settings.Hotkeys.ToggleDraw = "Shift+D";

        settings.Validate(_problems);

        Assert.Equal("Ctrl+Alt+D", settings.Hotkeys.ToggleDraw);
        Assert.Contains("toggleDraw", Assert.Single(_problems));
    }

    /// <summary>An empty hotkey means "turned off" and is not a problem.</summary>
    [Fact]
    public void EmptyHotkey_IsTurnedOff()
    {
        var settings = new AppSettings();
        settings.Hotkeys.ClearAll = "";

        settings.Validate(_problems);

        Assert.Equal("", settings.Hotkeys.ClearAll);
        Assert.Empty(_problems);
    }

    /// <summary>The same combination for two actions: the first keeps it, the second is turned off.</summary>
    [Fact]
    public void DuplicateHotkey_SecondIsTurnedOff()
    {
        var settings = new AppSettings();
        settings.Hotkeys.ClearAll = "ctrl+alt+d"; // Same as toggleDraw, different spelling.

        settings.Validate(_problems);

        Assert.Equal("Ctrl+Alt+D", settings.Hotkeys.ToggleDraw);
        Assert.Equal("", settings.Hotkeys.ClearAll);
        Assert.Single(_problems);
    }

    /// <summary>A named hotkey that collides with a color hotkey turns the color hotkeys off.</summary>
    [Fact]
    public void HotkeyCollidingWithColorHotkey_TurnsColorHotkeysOff()
    {
        var settings = new AppSettings();
        settings.Hotkeys.ToggleDraw = "Ctrl+Alt+3";

        settings.Validate(_problems);

        Assert.Equal("Ctrl+Alt+3", settings.Hotkeys.ToggleDraw);
        Assert.Equal("", settings.Hotkeys.ColorModifiers);
        Assert.Single(_problems);
    }

    /// <summary>Only as many color hotkeys as there are colors are checked for collisions.</summary>
    [Fact]
    public void ColorHotkeys_OnlyCoverExistingColors()
    {
        var settings = new AppSettings();
        settings.Colors.RemoveRange(2, 6); // 2 colors left: Ctrl+Alt+1 and Ctrl+Alt+2.
        settings.Hotkeys.ToggleDraw = "Ctrl+Alt+3";

        settings.Validate(_problems);

        Assert.Equal("Ctrl+Alt", settings.Hotkeys.ColorModifiers);
        Assert.Empty(_problems);
    }
}
