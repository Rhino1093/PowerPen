using PowerPen.Settings;
using PowerPen.UI;

namespace PowerPen.Tests;

/// <summary>Tests for the Settings window's logic: loading, validating, and saving values.</summary>
public class SettingsViewModelTests
{
    /// <summary>The window starts with the current settings.</summary>
    [Fact]
    public void Loads_CurrentSettings()
    {
        var current = new AppSettings();
        current.Hotkeys.ToggleDraw = "Ctrl+Alt+P";
        current.Hotkeys.ColorModifiers = "Ctrl+Shift";

        var vm = new SettingsViewModel(current, startWithWindows: true);

        Assert.Equal(8, vm.Colors.Count);
        Assert.Equal("Key 8", vm.Colors[7].KeyHint);
        Assert.Equal("Ctrl+Alt+P", vm.ToggleDraw);
        Assert.True(vm.ColorCtrl && vm.ColorShift);
        Assert.False(vm.ColorAlt || vm.ColorWin);
        Assert.True(vm.StartWithWindows);
    }

    /// <summary>Save with valid values raises Saved with exactly those values, and asks the window to close.</summary>
    [Fact]
    public void Save_Valid_RaisesSavedAndCloses()
    {
        var vm = new SettingsViewModel(new AppSettings(), startWithWindows: false);
        vm.MediumSize = "5";
        vm.Colors[0].Name = "Brand red";
        vm.ColorAlt = false;
        vm.ColorShift = true; // Ctrl+Shift

        SettingsSavedEventArgs? saved = null;
        bool closed = false;
        vm.Saved += (_, e) => saved = e;
        vm.CloseRequested += (_, _) => closed = true;
        vm.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.True(closed);
        Assert.Equal(5, saved.Settings.PenSizes.Medium);
        Assert.Equal("Brand red", saved.Settings.Colors[0].Name);
        Assert.Equal("Ctrl+Shift", saved.Settings.Hotkeys.ColorModifiers);
        Assert.Equal("", vm.Problems);
    }

    /// <summary>A number that isn't a number blocks Save and is explained by its label.</summary>
    [Fact]
    public void Save_BadNumber_ShowsProblemAndStaysOpen()
    {
        var vm = new SettingsViewModel(new AppSettings(), startWithWindows: false);
        vm.SmallSize = "thin";

        bool saved = false;
        vm.Saved += (_, _) => saved = true;
        vm.SaveCommand.Execute(null);

        Assert.False(saved);
        Assert.Contains("Small size", vm.Problems);
    }

    /// <summary>Problems use this window's labels and don't claim a default is "used" (nothing is replaced here).</summary>
    [Fact]
    public void Save_InvalidHotkey_ShowsFriendlyProblem()
    {
        var vm = new SettingsViewModel(new AppSettings(), startWithWindows: false);
        vm.ToggleDraw = "Shift+D";

        vm.SaveCommand.Execute(null);

        Assert.Contains("Draw / Pointer mode", vm.Problems);
        Assert.DoesNotContain("hotkeys.", vm.Problems);
        Assert.DoesNotContain("Using", vm.Problems);
    }

    /// <summary>Unticking every color modifier turns the color hotkeys off (allowed, not a problem).</summary>
    [Fact]
    public void NoColorModifiers_TurnsColorHotkeysOff()
    {
        var vm = new SettingsViewModel(new AppSettings(), startWithWindows: false) { ColorCtrl = false, ColorAlt = false };

        Assert.True(vm.TryBuild(out AppSettings settings, []));
        Assert.Equal("", settings.Hotkeys.ColorModifiers);
    }

    /// <summary>Colors: at most 8, at least 1, and the "Key n" hints follow the order.</summary>
    [Fact]
    public void Colors_AddAndRemoveRespectLimits()
    {
        var vm = new SettingsViewModel(new AppSettings(), startWithWindows: false);
        Assert.False(vm.AddColorCommand.CanExecute(null)); // Already 8.

        vm.Colors[0].RemoveCommand.Execute(null);
        Assert.Equal(7, vm.Colors.Count);
        Assert.Equal("Key 1", vm.Colors[0].KeyHint);
        Assert.True(vm.AddColorCommand.CanExecute(null));

        while (vm.Colors.Count > 1)
            vm.Colors[^1].RemoveCommand.Execute(null);
        vm.Colors[0].RemoveCommand.Execute(null); // The last one can't be removed.
        Assert.Single(vm.Colors);
    }

    /// <summary>Saving from the window keeps the toolbar's remembered position.</summary>
    [Fact]
    public void Save_KeepsToolbarPosition()
    {
        var current = new AppSettings();
        current.Toolbar.X = 300;
        current.Toolbar.Y = 400;
        current.Toolbar.Collapsed = true;

        var vm = new SettingsViewModel(current, startWithWindows: false);
        Assert.True(vm.TryBuild(out AppSettings settings, []));

        Assert.Equal(300, settings.Toolbar.X);
        Assert.Equal(400, settings.Toolbar.Y);
        Assert.True(settings.Toolbar.Collapsed);
    }

    /// <summary>Reset puts default values back in the window (nothing is saved until Save).</summary>
    [Fact]
    public void ResetToDefaults_RestoresDefaultValues()
    {
        var current = new AppSettings();
        current.Hotkeys.ToggleDraw = "Ctrl+Alt+P";
        current.Colors.RemoveRange(1, 7);
        var vm = new SettingsViewModel(current, startWithWindows: false);

        vm.ResetToDefaultsCommand.Execute(null);

        Assert.Equal("Ctrl+Alt+D", vm.ToggleDraw);
        Assert.Equal(8, vm.Colors.Count);
    }
}
