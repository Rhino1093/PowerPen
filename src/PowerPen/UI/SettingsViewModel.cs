using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerPen.Diagnostics;
using PowerPen.Input;
using PowerPen.Interop;
using PowerPen.Settings;

namespace PowerPen.UI;

/// <summary>Values chosen in the Settings window when the user clicks Save.</summary>
/// <param name="settings">The new, validated settings.</param>
/// <param name="startWithWindows">Whether PowerPen should start when Windows starts.</param>
public sealed class SettingsSavedEventArgs(AppSettings settings, bool startWithWindows) : EventArgs
{
    /// <summary>The new, validated settings.</summary>
    public AppSettings Settings { get; } = settings;

    /// <summary>Whether PowerPen should start when Windows starts.</summary>
    public bool StartWithWindows { get; } = startWithWindows;
}

/// <summary>
/// The Settings window's editable copy of the settings. Nothing changes in the app until Save, and Save only
/// succeeds when every value is valid. Otherwise the problems are listed and the window stays open.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>
    /// Settings-file names in validation messages, shown with the labels used in this window instead.
    /// The quoted "hotkeys.x" forms must come before the bare names.
    /// </summary>
    private static readonly (string FileName, string Label)[] FriendlyNames =
    [
        ("\"hotkeys.toggleDraw\"", "Draw / Pointer mode"),
        ("\"hotkeys.toggleInkVisible\"", "Hide / show ink"),
        ("\"hotkeys.clearAll\"", "Clear all ink"),
        ("\"hotkeys.quit\"", "Quit PowerPen"),
        ("\"hotkeys.colorModifiers\"", "Pick a color"),
        ("\"colors\"", "Colors"),
        ("\"penSizes\"", "Sizes"),
        ("\"highlighterWidthMultiplier\"", "Highlighter width"),
        ("\"screenshotFolder\"", "Screenshot folder"),
        ("toggleDraw", "Draw / Pointer mode"),
        ("toggleInkVisible", "Hide / show ink"),
        ("clearAll", "Clear all ink"),
        ("colorModifiers", "Pick a color"),
        ("quit", "Quit PowerPen"),
    ];

    private readonly ToolbarSettings _toolbar;

    /// <summary>Fills the window from the current settings.</summary>
    /// <param name="current">The settings in use (copied; never changed here).</param>
    /// <param name="startWithWindows">Whether PowerPen currently starts with Windows.</param>
    public SettingsViewModel(AppSettings current, bool startWithWindows)
    {
        Colors = [];
        _toolbar = current.Clone().Toolbar; // Not edited here; kept so saving doesn't move the toolbar.
        Load(current);
        StartWithWindows = startWithWindows;
    }

    /// <summary>Raised when Save succeeds.</summary>
    public event EventHandler<SettingsSavedEventArgs>? Saved;

    /// <summary>Raised when the window should close (after Save or Cancel).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The palette being edited.</summary>
    public ObservableCollection<ColorRowViewModel> Colors { get; }

    /// <summary>Thin line width, as typed.</summary>
    [ObservableProperty] public partial string SmallSize { get; set; } = "";

    /// <summary>Default line width, as typed.</summary>
    [ObservableProperty] public partial string MediumSize { get; set; } = "";

    /// <summary>Thick line width, as typed.</summary>
    [ObservableProperty] public partial string LargeSize { get; set; } = "";

    /// <summary>Highlighter width multiplier, as typed.</summary>
    [ObservableProperty] public partial string HighlighterMultiplier { get; set; } = "";

    /// <summary>Hotkey: switch between Draw and Pointer mode.</summary>
    [ObservableProperty] public partial string ToggleDraw { get; set; } = "";

    /// <summary>Hotkey: hide or show ink.</summary>
    [ObservableProperty] public partial string ToggleInkVisible { get; set; } = "";

    /// <summary>Hotkey: clear all ink.</summary>
    [ObservableProperty] public partial string ClearAll { get; set; } = "";

    /// <summary>Hotkey: quit PowerPen.</summary>
    [ObservableProperty] public partial string Quit { get; set; } = "";

    /// <summary>Color hotkeys use Ctrl.</summary>
    [ObservableProperty] public partial bool ColorCtrl { get; set; }

    /// <summary>Color hotkeys use Alt.</summary>
    [ObservableProperty] public partial bool ColorAlt { get; set; }

    /// <summary>Color hotkeys use Shift.</summary>
    [ObservableProperty] public partial bool ColorShift { get; set; }

    /// <summary>Color hotkeys use the Windows key.</summary>
    [ObservableProperty] public partial bool ColorWin { get; set; }

    /// <summary>Screenshot folder; empty means Downloads.</summary>
    [ObservableProperty] public partial string ScreenshotFolder { get; set; } = "";

    /// <summary>Also copy screenshots to the clipboard.</summary>
    [ObservableProperty] public partial bool CopyToClipboard { get; set; }

    /// <summary>Keep the toolbar out of screenshots and screen shares.</summary>
    [ObservableProperty] public partial bool HideToolbarFromCapture { get; set; }

    /// <summary>Start PowerPen when Windows starts.</summary>
    [ObservableProperty] public partial bool StartWithWindows { get; set; }

    /// <summary>What's wrong with the values, shown above the buttons; empty when nothing is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemsVisibility))]
    public partial string Problems { get; set; } = "";

    /// <summary>Shows the problems area only when there are problems.</summary>
    public Visibility ProblemsVisibility => string.IsNullOrEmpty(Problems) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Hint under the folder box, showing where "empty" saves to.</summary>
    public string FolderHint => $"Leave empty for your Downloads folder ({KnownFolders.Downloads}).";

    /// <summary>Where the settings file and logs are, for anyone who needs to find them.</summary>
    public string FilesHint => $"Settings file: {SettingsService.DefaultPath}\nLogs: {Log.Folder}";

    /// <summary>
    /// Builds settings from the window's values. Returns false, with every problem described, if anything is invalid.
    /// </summary>
    public bool TryBuild(out AppSettings settings, List<string> problems)
    {
        var sizes = new PenSizeSettings
        {
            Small = ParseNumber(SmallSize, "Small size", problems),
            Medium = ParseNumber(MediumSize, "Medium size", problems),
            Large = ParseNumber(LargeSize, "Large size", problems),
        };
        double multiplier = ParseNumber(HighlighterMultiplier, "Highlighter width", problems);

        settings = new AppSettings
        {
            Colors = Colors.Select(row => new ColorSetting { Name = row.Name.Trim(), Color = row.Hex.Trim() }).ToList(),
            PenSizes = sizes,
            HighlighterWidthMultiplier = multiplier,
            Hotkeys = new HotkeySettings
            {
                ToggleDraw = ToggleDraw,
                ToggleInkVisible = ToggleInkVisible,
                ClearAll = ClearAll,
                Quit = Quit,
                ColorModifiers = ColorModifiersText(),
            },
            ScreenshotFolder = ScreenshotFolder.Trim(),
            CopyScreenshotToClipboard = CopyToClipboard,
            HideToolbarFromCapture = HideToolbarFromCapture,
            Toolbar = _toolbar,
        };

        // Numbers that didn't parse would also fail validation; report them once, in plain words.
        if (problems.Count > 0)
            return false;

        var validation = new List<string>();
        settings.Validate(validation);
        problems.AddRange(validation.Select(Friendly));
        return problems.Count == 0;
    }

    /// <summary>Saves if everything is valid; otherwise lists the problems and keeps the window open.</summary>
    [RelayCommand]
    private void Save()
    {
        var problems = new List<string>();
        if (!TryBuild(out AppSettings settings, problems))
        {
            Problems = string.Join("\n", problems.Select(p => "• " + p));
            return;
        }

        Problems = "";
        Saved?.Invoke(this, new SettingsSavedEventArgs(settings, StartWithWindows));
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes without changing anything.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Fills the window with the default settings (nothing changes until Save).</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        Load(new AppSettings());
        Problems = "";
    }

    /// <summary>Adds a gray color at the end (up to 8).</summary>
    [RelayCommand(CanExecute = nameof(CanAddColor))]
    private void AddColor()
    {
        Colors.Add(new ColorRowViewModel($"Color {Colors.Count + 1}", "#808080", RemoveColor));
        OnColorsChanged();
    }

    /// <summary>Lets the user pick the screenshot folder.</summary>
    [RelayCommand]
    private void BrowseFolder()
    {
        string start = string.IsNullOrWhiteSpace(ScreenshotFolder)
            ? KnownFolders.Downloads
            : Environment.ExpandEnvironmentVariables(ScreenshotFolder);
        if (Dialogs.PickFolder(start) is string folder)
            ScreenshotFolder = folder;
    }

    /// <summary>True while there's room for another color.</summary>
    private bool CanAddColor => Colors.Count < AppSettings.MaxColors;

    /// <summary>Removes a color row (at least one must remain).</summary>
    private void RemoveColor(ColorRowViewModel row)
    {
        if (Colors.Count <= 1)
            return;
        Colors.Remove(row);
        OnColorsChanged();
    }

    /// <summary>Renumbers the "Key n" hints and updates the Add button after rows change.</summary>
    private void OnColorsChanged()
    {
        for (int i = 0; i < Colors.Count; i++)
            Colors[i].KeyHint = $"Key {i + 1}";
        AddColorCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Copies values from <paramref name="source"/> into the window.</summary>
    private void Load(AppSettings source)
    {
        Colors.Clear();
        foreach (ColorSetting color in source.Colors)
            Colors.Add(new ColorRowViewModel(color.Name, color.Color, RemoveColor));
        OnColorsChanged();

        SmallSize = FormatNumber(source.PenSizes.Small);
        MediumSize = FormatNumber(source.PenSizes.Medium);
        LargeSize = FormatNumber(source.PenSizes.Large);
        HighlighterMultiplier = FormatNumber(source.HighlighterWidthMultiplier);

        ToggleDraw = source.Hotkeys.ToggleDraw;
        ToggleInkVisible = source.Hotkeys.ToggleInkVisible;
        ClearAll = source.Hotkeys.ClearAll;
        Quit = source.Hotkeys.Quit;

        // Empty (off) parses as no modifiers, which shows as all boxes unticked.
        Hotkey.TryParseModifiers(source.Hotkeys.ColorModifiers, out ModifierKeys modifiers, out _);
        ColorCtrl = modifiers.HasFlag(ModifierKeys.Control);
        ColorAlt = modifiers.HasFlag(ModifierKeys.Alt);
        ColorShift = modifiers.HasFlag(ModifierKeys.Shift);
        ColorWin = modifiers.HasFlag(ModifierKeys.Windows);

        ScreenshotFolder = source.ScreenshotFolder;
        CopyToClipboard = source.CopyScreenshotToClipboard;
        HideToolbarFromCapture = source.HideToolbarFromCapture;
    }

    /// <summary>The color-hotkey checkboxes as settings text, e.g. "Ctrl+Alt"; empty when none are ticked (off).</summary>
    private string ColorModifiersText()
    {
        ModifierKeys modifiers = ModifierKeys.None;
        if (ColorCtrl) modifiers |= ModifierKeys.Control;
        if (ColorAlt) modifiers |= ModifierKeys.Alt;
        if (ColorShift) modifiers |= ModifierKeys.Shift;
        if (ColorWin) modifiers |= ModifierKeys.Windows;
        return Hotkey.FormatModifiers(modifiers);
    }

    /// <summary>Parses a number typed in the user's own format (e.g. "2,5" in Europe) or with a dot.</summary>
    private static double ParseNumber(string text, string label, List<string> problems)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return value;

        problems.Add($"{label}: \"{text}\" isn't a number.");
        return double.NaN;
    }

    /// <summary>Shows a number without needless decimals ("4", "2.5").</summary>
    private static string FormatNumber(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>
    /// Rewrites a settings-file validation message for this window: uses the labels shown here, and drops the
    /// "Using …" ending, since nothing is replaced here (the user just fixes the value).
    /// </summary>
    private static string Friendly(string message)
    {
        foreach ((string fileName, string label) in FriendlyNames)
            message = message.Replace(fileName, label, StringComparison.Ordinal);
        return Regex.Replace(message, @"\s+Using .*$", "");
    }
}
