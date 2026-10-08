using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PowerPen.Settings;

namespace PowerPen.Core;

/// <summary>
/// Single source of truth for what the user has selected: mode, tool, color, pen size, and ink visibility.
/// The toolbar displays it and the overlays apply it. Change it through <see cref="AppCommands"/> so that
/// side effects (like focus handling) always happen the same way.
/// </summary>
public sealed partial class AppState : ObservableObject
{
    /// <summary>Pen width in WPF units for each <see cref="Core.PenSize"/>, indexed by the enum value.</summary>
    private double[] _penWidths = [];

    /// <summary>Creates the state from default settings (used by tests and the self-test).</summary>
    public AppState() : this(new AppSettings())
    {
    }

    /// <summary>Creates the state from <paramref name="settings"/>, which must already be validated.</summary>
    public AppState(AppSettings settings)
    {
        LoadSettings(settings);
        PenSize = PenSize.Medium;
        IsInkVisible = true;
    }

    /// <summary>The colors shown on the toolbar.</summary>
    public IReadOnlyList<NamedColor> Palette { get; private set; } = [];

    /// <summary>Highlighter strokes are this many times wider than pen strokes of the same size.</summary>
    public double HighlighterWidthMultiplier { get; private set; }

    /// <summary>True while the overlays capture the mouse for drawing; false in Pointer mode.</summary>
    [ObservableProperty]
    public partial bool IsDrawMode { get; set; }

    /// <summary>False while the user has hidden all ink (it isn't deleted, just not shown).</summary>
    [ObservableProperty]
    public partial bool IsInkVisible { get; set; }

    /// <summary>The tool used in Draw mode.</summary>
    [ObservableProperty]
    public partial ToolType CurrentTool { get; set; }

    /// <summary>Index into <see cref="Palette"/> of the selected color.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentColor))]
    public partial int ColorIndex { get; set; }

    /// <summary>The selected pen-size preset.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PenWidth))]
    public partial PenSize PenSize { get; set; }

    /// <summary>The selected color.</summary>
    public Color CurrentColor => Palette[ColorIndex].Color;

    /// <summary>Pen width in WPF units for the selected size (before the highlighter multiplier).</summary>
    public double PenWidth => _penWidths[(int)PenSize];

    /// <summary>
    /// Takes new colors and sizes from the Settings window. Everything listening (toolbar, overlays) updates right away.
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        LoadSettings(settings);

        // If the palette got shorter, the selected color may no longer exist.
        if (ColorIndex >= Palette.Count)
            ColorIndex = 0;

        OnPropertyChanged(nameof(Palette));
        OnPropertyChanged(nameof(CurrentColor));
        OnPropertyChanged(nameof(PenWidth));
        OnPropertyChanged(nameof(HighlighterWidthMultiplier));
    }

    /// <summary>Reads palette and sizes from validated settings.</summary>
    private void LoadSettings(AppSettings settings)
    {
        Palette = settings.Colors
            .Select(c => AppSettings.TryParseColor(c.Color, out Color color)
                ? new NamedColor(c.Name, color)
                : throw new ArgumentException($"Color \"{c.Color}\" wasn't validated.", nameof(settings)))
            .ToList();
        _penWidths = [settings.PenSizes.Small, settings.PenSizes.Medium, settings.PenSizes.Large];
        HighlighterWidthMultiplier = settings.HighlighterWidthMultiplier;
    }
}
