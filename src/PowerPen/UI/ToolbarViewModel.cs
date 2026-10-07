using System.ComponentModel;
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
    private readonly HotkeySettings _hotkeys;

    /// <summary>Creates the toolbar's view model.</summary>
    /// <param name="state">The shared app state.</param>
    /// <param name="commands">Where button clicks go.</param>
    /// <param name="undo">Undo history (enables/disables the Undo button).</param>
    /// <param name="hotkeys">Validated global hotkeys, shown in tooltips.</param>
    public ToolbarViewModel(AppState state, AppCommands commands, UndoManager undo, HotkeySettings hotkeys)
    {
        _state = state;
        _commands = commands;
        _undo = undo;
        _hotkeys = hotkeys;

        // Color hotkeys exist only when colorModifiers is set (it's empty when turned off).
        bool hasColorHotkeys = Hotkey.TryParseModifiers(hotkeys.ColorModifiers, out ModifierKeys colorModifiers, out _);
        Swatches = state.Palette
            .Select((color, i) =>
            {
                string? global = hasColorHotkeys ? new Hotkey(colorModifiers, Key.D1 + i).ToString() : null;
                return new SwatchViewModel(color, i, Tip(color.Name, (i + 1).ToString(), global), commands.SelectColor);
            })
            .ToList();

        _state.PropertyChanged += OnStateChanged;
        _undo.PropertyChanged += (_, _) => UndoCommand.NotifyCanExecuteChanged();
        RefreshSwatches();
    }

    /// <summary>The color swatches, in palette order.</summary>
    public IReadOnlyList<SwatchViewModel> Swatches { get; }

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

    /// <summary>Size of the dot on the pen-size button, so the button shows the current size at a glance.</summary>
    public double SizeDotDiameter => SizeDotDiameters[(int)_state.PenSize];

    /// <summary>Tooltip for the Pointer button.</summary>
    public string PointerToolTip => Tip("Pointer: ink stays, clicks go to your apps", "Esc", _hotkeys.ToggleDraw);

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
    public string InkToggleToolTip => Tip(_state.IsInkVisible ? "Hide ink" : "Show ink", null, _hotkeys.ToggleInkVisible);

    /// <summary>Tooltip for the Clear all button.</summary>
    public string ClearAllToolTip => Tip("Clear all ink", "Delete", _hotkeys.ClearAll);

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

    /// <summary>True when in Draw mode with <paramref name="tool"/> selected.</summary>
    private bool IsToolActive(ToolType tool) => _state.IsDrawMode && _state.CurrentTool == tool;

    /// <summary>Refreshes everything the toolbar displays when the app state changes.</summary>
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        // An empty property name tells WPF to re-read every binding. Simpler than tracking which
        // derived property depends on which state property, and the toolbar is tiny.
        OnPropertyChanged(string.Empty);
        RefreshSwatches();
    }

    /// <summary>Marks only the current color's swatch as selected.</summary>
    private void RefreshSwatches()
    {
        for (int i = 0; i < Swatches.Count; i++)
            Swatches[i].IsSelected = i == _state.ColorIndex;
    }
}
