using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerPen.Core;

namespace PowerPen.UI;

/// <summary>
/// What the toolbar shows and what its buttons do. Reads from <see cref="AppState"/> and sends every click
/// to <see cref="AppCommands"/>.
/// </summary>
public sealed partial class ToolbarViewModel : ObservableObject
{
    /// <summary>Diameter in WPF units of the size-indicator dot for each <see cref="PenSize"/>.</summary>
    private static readonly double[] SizeDotDiameters = [6, 10, 16];

    private readonly AppState _state;
    private readonly AppCommands _commands;
    private readonly UndoManager _undo;

    /// <summary>Creates the toolbar's view model.</summary>
    public ToolbarViewModel(AppState state, AppCommands commands, UndoManager undo)
    {
        _state = state;
        _commands = commands;
        _undo = undo;

        Swatches = state.Palette.Select((color, i) => new SwatchViewModel(color, i, commands.SelectColor)).ToList();

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

    /// <summary>Current color, used to fill the size-indicator dot.</summary>
    public Brush CurrentBrush => new SolidColorBrush(_state.CurrentColor);

    /// <summary>Size of the dot on the pen-size button, so the button shows the current size at a glance.</summary>
    public double SizeDotDiameter => SizeDotDiameters[(int)_state.PenSize];

    /// <summary>Tooltip for the pen-size button.</summary>
    public string SizeToolTip => $"Pen size: {_state.PenSize} (click to change)";

    /// <summary>Switches to Pointer mode.</summary>
    [RelayCommand]
    private void Pointer() => _commands.EnterPointerMode();

    /// <summary>Starts drawing with the pen.</summary>
    [RelayCommand]
    private void Pen() => _commands.SelectTool(ToolType.Pen);

    /// <summary>Starts drawing with the highlighter.</summary>
    [RelayCommand]
    private void Highlighter() => _commands.SelectTool(ToolType.Highlighter);

    /// <summary>Starts erasing.</summary>
    [RelayCommand]
    private void Eraser() => _commands.SelectTool(ToolType.Eraser);

    /// <summary>Cycles the pen size.</summary>
    [RelayCommand]
    private void CycleSize() => _commands.CycleSize();

    /// <summary>Undoes the last change. Disabled when there's nothing to undo.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _commands.Undo();

    /// <summary>Clears all ink.</summary>
    [RelayCommand]
    private void ClearAll() => _commands.ClearAll();

    /// <summary>Whether the Undo button is enabled.</summary>
    private bool CanUndo => _undo.CanUndo;

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
