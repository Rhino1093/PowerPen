using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PowerPen.Core;

/// <summary>
/// Single source of truth for what the user has selected: mode, tool, color, and pen size.
/// The toolbar displays it and the overlays apply it. Change it through <see cref="AppCommands"/> so that
/// side effects (like focus handling) always happen the same way.
/// </summary>
public sealed partial class AppState : ObservableObject
{
    /// <summary>Pen width in WPF units for each <see cref="Core.PenSize"/>, indexed by the enum value.</summary>
    private static readonly double[] PenWidths = [2, 4, 8];

    /// <summary>Creates the state with default selections: Pointer mode, pen, red, medium.</summary>
    public AppState()
    {
        PenSize = PenSize.Medium;
    }

    /// <summary>The 8 default colors, in toolbar order.</summary>
    public static IReadOnlyList<NamedColor> DefaultPalette { get; } =
    [
        new("Red", Color.FromRgb(0xE5, 0x39, 0x35)),
        new("Orange", Color.FromRgb(0xFB, 0x8C, 0x00)),
        new("Yellow", Color.FromRgb(0xFD, 0xD8, 0x35)),
        new("Green", Color.FromRgb(0x43, 0xA0, 0x47)),
        new("Blue", Color.FromRgb(0x1E, 0x88, 0xE5)),
        new("Purple", Color.FromRgb(0x8E, 0x24, 0xAA)),
        new("Black", Color.FromRgb(0x00, 0x00, 0x00)),
        new("White", Color.FromRgb(0xFF, 0xFF, 0xFF)),
    ];

    /// <summary>Highlighter strokes are this many times wider than pen strokes of the same size.</summary>
    public const double HighlighterWidthMultiplier = 3;

    /// <summary>The colors shown on the toolbar.</summary>
    public IReadOnlyList<NamedColor> Palette { get; } = DefaultPalette;

    /// <summary>True while the overlays capture the mouse for drawing; false in Pointer mode.</summary>
    [ObservableProperty]
    public partial bool IsDrawMode { get; set; }

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
    public double PenWidth => PenWidths[(int)PenSize];
}
