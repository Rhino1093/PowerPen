using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerPen.Core;
using PowerPen.Input;
using PowerPen.Settings;

namespace PowerPen.UI;

/// <summary>
/// What the toolbar shows and what its buttons do. Reads from <see cref="AppState"/> and sends every click
/// to <see cref="AppCommands"/>. Tooltips list each button's shortcuts so people learn them by using the toolbar.
/// </summary>
public sealed partial class ToolbarViewModel : ObservableObject
{
    /// <summary>Diameter in WPF units of the size-indicator dot for each <see cref="PenSize"/>.</summary>
    private static readonly double[] SizeDotDiameters = [6, 10, 16];

    /// <summary>Icon shown while ink is visible (eye).</summary>
    private const string EyeGlyph = "";

    /// <summary>Icon shown while ink is hidden (crossed-out eye).</summary>
    private const string EyeHiddenGlyph = "";

    private readonly AppState _state;
    private readonly AppCommands _commands;
    private readonly UndoManager _undo;
    private readonly AppSettings _settings;

    /// <summary>Creates the toolbar's view model.</summary>
    /// <param name="state">The shared app state.</param>
    /// <param name="commands">Where button clicks go.</param>
    /// <param name="undo">Undo history (enables/disables the Undo button).</param>
    /// <param name="settings">The shared settings (hotkeys and screenshot options, shown in tooltips). Read live.</param>
    public ToolbarViewModel(AppState state, AppCommands commands, UndoManager undo, AppSettings settings)
    {
        _state = state;
        _commands = commands;
        _undo = undo;
        _settings = settings;

        Swatches = BuildSwatches();
        IsCollapsed = settings.Toolbar.Collapsed;

        _state.PropertyChanged += OnStateChanged;
        _undo.PropertyChanged += (_, _) => UndoCommand.NotifyCanExecuteChanged();
        RefreshSwatchSelection();
    }

    /// <summary>The color swatches, in palette order. Rebuilt when the palette changes in Settings.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SwatchViewModel> Swatches { get; set; }

    /// <summary>True when the toolbar is shrunk to a single button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandedVisibility), nameof(CollapsedVisibility))]
    public partial bool IsCollapsed { get; set; }

    /// <summary>Shows the full toolbar unless collapsed.</summary>
    public Visibility ExpandedVisibility => IsCollapsed ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Shows the single button only when collapsed.</summary>
    public Visibility CollapsedVisibility => IsCollapsed ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True in Draw mode; lights up the collapsed button so you can tell drawing is on.</summary>
    public bool IsDrawing => _state.IsDrawMode;

    /// <summary>True in Pointer mode.</summary>
    public bool IsPointerActive => !_state.IsDrawMode;

    /// <summary>True when drawing with the pen.</summary>
    public bool IsPenActive => IsToolActive(ToolType.Pen);

    /// <summary>True when drawing with the highlighter.</summary>
    public bool IsHighlighterActive => IsToolActive(ToolType.Highlighter);

    /// <summary>True when erasing.</summary>
    public bool IsEraserActive => IsToolActive(ToolType.Eraser);

    /// <summary>True when drawing lines.</summary>
    public bool IsLineActive => IsToolActive(ToolType.Line);

    /// <summary>True when drawing arrows.</summary>
    public bool IsArrowActive => IsToolActive(ToolType.Arrow);

    /// <summary>True when drawing rectangles.</summary>
    public bool IsRectangleActive => IsToolActive(ToolType.Rectangle);

    /// <summary>True when drawing ellipses.</summary>
    public bool IsEllipseActive => IsToolActive(ToolType.Ellipse);

    /// <summary>True when placing text.</summary>
    public bool IsTextActive => IsToolActive(ToolType.Text);

    /// <summary>True while ink is hidden; highlights the eye button so the hidden state is never a mystery.</summary>
    public bool IsInkHidden => !_state.IsInkVisible;

    /// <summary>Eye icon, crossed out while ink is hidden.</summary>
    public string InkToggleGlyph => _state.IsInkVisible ? EyeGlyph : EyeHiddenGlyph;

    /// <summary>Current color, used to fill the size-indicator dot.</summary>
    public Brush CurrentBrush => new SolidColorBrush(_state.CurrentColor);

    /// <summary>Size of the dot on the size button, so the button shows the current size at a glance.</summary>
    public double SizeDotDiameter => SizeDotDiameters[(int)_state.PenSize];

    /// <summary>Tooltip for the Pointer button.</summary>
    public string PointerToolTip => Tip("Pointer: ink stays, clicks go to your apps", "Esc", Hotkeys.ToggleDraw);

    /// <summary>Tooltip for the Pen button.</summary>
    public string PenToolTip => Tip("Pen", "P", null);

    /// <summary>Tooltip for the Highlighter button.</summary>
    public string HighlighterToolTip => Tip("Highlighter", "H", null);

    /// <summary>Tooltip for the Eraser button.</summary>
    public string EraserToolTip => Tip("Eraser: drag over strokes or text to remove them", "E", null);

    /// <summary>Tooltip for the Line button.</summary>
    public string LineToolTip => Tip("Line: drag to draw", "L · hold Shift for 45° angles", null);

    /// <summary>Tooltip for the Arrow button.</summary>
    public string ArrowToolTip => Tip("Arrow: drag from the tail to the tip", "A · hold Shift for 45° angles", null);

    /// <summary>Tooltip for the Rectangle button.</summary>
    public string RectangleToolTip => Tip("Rectangle: drag from corner to corner", "R · hold Shift for a square", null);

    /// <summary>Tooltip for the Ellipse button.</summary>
    public string EllipseToolTip => Tip("Ellipse: drag from corner to corner", "O · hold Shift for a circle", null);

    /// <summary>Tooltip for the Text button.</summary>
    public string TextToolTip => Tip("Text: click where you want to type", "T · Enter to finish, Shift+Enter for a new line, Esc to cancel", null);

    /// <summary>Tooltip for the size button.</summary>
    public string SizeToolTip => Tip($"Line and text size: {_state.PenSize} (click to change)", "[ and ]", null);

    /// <summary>Tooltip for the Undo button.</summary>
    public string UndoToolTip => Tip("Undo", "Ctrl+Z (Ctrl+Y to redo)", null);

    /// <summary>Tooltip for the hide/show ink button.</summary>
    public string InkToggleToolTip => Tip(_state.IsInkVisible ? "Hide ink" : "Show ink", null, Hotkeys.ToggleInkVisible);

    /// <summary>Tooltip for the Clear all button.</summary>
    public string ClearAllToolTip => Tip("Clear all ink", "Delete", Hotkeys.ClearAll);

    /// <summary>Tooltip for the Screenshot button, including where pictures go.</summary>
    public string ScreenshotToolTip
    {
        get
        {
            // "Downloads" by default, otherwise the last part of the chosen folder (e.g. "Screenshots").
            string folder = string.IsNullOrWhiteSpace(_settings.ScreenshotFolder)
                ? "Downloads"
                : Path.GetFileName(Path.TrimEndingDirectorySeparator(Environment.ExpandEnvironmentVariables(_settings.ScreenshotFolder)));
            string clipboard = _settings.CopyScreenshotToClipboard ? ", and copied to the clipboard" : "";
            return $"Screenshot: drag an area, or click a screen\nSaved to {folder}{clipboard}";
        }
    }

    /// <summary>Tooltip for the Settings button.</summary>
    public string SettingsToolTip => "Settings: colors, sizes, hotkeys, screenshots";

    /// <summary>Tooltip for the Collapse button.</summary>
    public string CollapseToolTip => "Collapse to a single button (click it to bring the toolbar back)";

    /// <summary>Tooltip for the single button shown while collapsed.</summary>
    public string ExpandToolTip => _state.IsDrawMode
        ? "PowerPen: drawing is on. Click to show the toolbar."
        : "PowerPen: click to show the toolbar";

    /// <summary>Live hotkey settings (they can change in the Settings window).</summary>
    private HotkeySettings Hotkeys => _settings.Hotkeys;

    /// <summary>Re-reads everything that depends on settings (palette, tooltips) after Settings are saved.</summary>
    public void RefreshFromSettings()
    {
        Swatches = BuildSwatches();
        RefreshSwatchSelection();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Switches to Pointer mode.</summary>
    [RelayCommand]
    private void Pointer() => _commands.EnterPointerMode();

    /// <summary>Selects a tool and starts drawing with it. The toolbar passes the tool as the button's CommandParameter.</summary>
    [RelayCommand]
    private void SelectTool(ToolType tool) => _commands.SelectTool(tool);

    /// <summary>Cycles the pen size.</summary>
    [RelayCommand]
    private void CycleSize() => _commands.CycleSize();

    /// <summary>Undoes the last change. Disabled when there's nothing to undo.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _commands.Undo();

    /// <summary>Hides or shows all ink.</summary>
    [RelayCommand]
    private void ToggleInk() => _commands.ToggleInkVisible();

    /// <summary>Clears all ink.</summary>
    [RelayCommand]
    private void ClearAll() => _commands.ClearAll();

    /// <summary>
    /// Takes a screenshot. As an async command, the button is disabled until the screenshot is finished,
    /// so a double-click can't start two.
    /// </summary>
    [RelayCommand]
    private Task Screenshot() => _commands.TakeScreenshotAsync();

    /// <summary>Opens the Settings window.</summary>
    [RelayCommand]
    private void OpenSettings() => _commands.OpenSettings();

    /// <summary>Shrinks the toolbar to a single button.</summary>
    [RelayCommand]
    private void Collapse() => IsCollapsed = true;

    /// <summary>Brings the full toolbar back.</summary>
    [RelayCommand]
    private void Expand() => IsCollapsed = false;

    /// <summary>Whether the Undo button is enabled.</summary>
    private bool CanUndo => _undo.CanUndo;

    /// <summary>
    /// Builds a two-line tooltip: what the button does, then its shortcuts.
    /// </summary>
    /// <param name="description">What the button does.</param>
    /// <param name="drawModeKey">Key that works while drawing, or null.</param>
    /// <param name="globalHotkey">Hotkey that works from any app (from settings; empty = turned off), or null.</param>
    private static string Tip(string description, string? drawModeKey, string? globalHotkey)
    {
        var shortcuts = new List<string>();
        if (!string.IsNullOrEmpty(drawModeKey))
            shortcuts.Add(drawModeKey);
        // Re-format through the parser so "ctrl+alt+d" in settings displays as "Ctrl+Alt+D".
        if (Hotkey.TryParse(globalHotkey, out Hotkey hotkey, out _))
            shortcuts.Add($"{hotkey} from any app");

        return shortcuts.Count == 0 ? description : $"{description}\n{string.Join(", or ", shortcuts)}";
    }

    /// <summary>Creates one swatch per palette color, with tooltips showing its shortcuts.</summary>
    private IReadOnlyList<SwatchViewModel> BuildSwatches()
    {
        // Color hotkeys exist only when colorModifiers is set (it's empty when turned off).
        bool hasColorHotkeys = Hotkey.TryParseModifiers(Hotkeys.ColorModifiers, out ModifierKeys colorModifiers, out _);
        return _state.Palette
            .Select((color, i) =>
            {
                string? global = hasColorHotkeys ? new Hotkey(colorModifiers, Key.D1 + i).ToString() : null;
                return new SwatchViewModel(color, i, Tip(color.Name, (i + 1).ToString(), global), _commands.SelectColor);
            })
            .ToList();
    }

    /// <summary>True when in Draw mode with <paramref name="tool"/> selected.</summary>
    private bool IsToolActive(ToolType tool) => _state.IsDrawMode && _state.CurrentTool == tool;

    /// <summary>Refreshes everything the toolbar displays when the app state changes.</summary>
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.Palette))
            Swatches = BuildSwatches();

        // An empty property name tells WPF to re-read every binding. Simpler than tracking which
        // derived property depends on which state property, and the toolbar is tiny.
        OnPropertyChanged(string.Empty);
        RefreshSwatchSelection();
    }

    /// <summary>Marks only the current color's swatch as selected.</summary>
    private void RefreshSwatchSelection()
    {
        for (int i = 0; i < Swatches.Count; i++)
            Swatches[i].IsSelected = i == _state.ColorIndex;
    }
}
