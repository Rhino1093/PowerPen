using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerPen.Core;

namespace PowerPen.UI;

/// <summary>One color swatch on the toolbar.</summary>
public sealed partial class SwatchViewModel : ObservableObject
{
    /// <summary>Creates a swatch for <paramref name="color"/> at position <paramref name="index"/> in the palette.</summary>
    /// <param name="color">The palette color.</param>
    /// <param name="index">Position in the palette.</param>
    /// <param name="toolTip">Tooltip text (color name and its shortcuts).</param>
    /// <param name="select">Called with <paramref name="index"/> when the swatch is clicked.</param>
    public SwatchViewModel(NamedColor color, int index, string toolTip, Action<int> select)
    {
        var brush = new SolidColorBrush(color.Color);
        brush.Freeze();
        Brush = brush;
        ToolTip = toolTip;
        SelectCommand = new RelayCommand(() => select(index));
    }

    /// <summary>Fill for the swatch circle.</summary>
    public Brush Brush { get; }

    /// <summary>Color name and shortcuts, shown on hover.</summary>
    public string ToolTip { get; }

    /// <summary>Selects this color.</summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>True when this is the current color.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
