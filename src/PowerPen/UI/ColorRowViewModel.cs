using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerPen.Settings;

namespace PowerPen.UI;

/// <summary>One editable color in the Settings window: name, color code, and a live preview.</summary>
public sealed partial class ColorRowViewModel : ObservableObject
{
    private readonly Action<ColorRowViewModel> _remove;

    /// <summary>Creates a row.</summary>
    /// <param name="name">Name shown in the toolbar tooltip.</param>
    /// <param name="hex">Color code, e.g. "#E53935".</param>
    /// <param name="remove">Called when the row's Remove button is clicked.</param>
    public ColorRowViewModel(string name, string hex, Action<ColorRowViewModel> remove)
    {
        Name = name;
        Hex = hex;
        _remove = remove;
    }

    /// <summary>Name shown in the toolbar tooltip.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>Color code, e.g. "#E53935".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBrush))]
    public partial string Hex { get; set; }

    /// <summary>Which key selects this color while drawing ("Key 1" … "Key 8").</summary>
    [ObservableProperty]
    public partial string KeyHint { get; set; } = "";

    /// <summary>The color as typed, or transparent while the code isn't a valid color yet.</summary>
    public Brush PreviewBrush => AppSettings.TryParseColor(Hex, out Color color) ? new SolidColorBrush(color) : Brushes.Transparent;

    /// <summary>Opens the Windows color picker for this row.</summary>
    [RelayCommand]
    private void Pick()
    {
        Color current = AppSettings.TryParseColor(Hex, out Color parsed) ? parsed : Colors.Gray;
        if (Dialogs.PickColor(current) is Color picked)
            Hex = $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
    }

    /// <summary>Removes this row.</summary>
    [RelayCommand]
    private void Remove() => _remove(this);
}
