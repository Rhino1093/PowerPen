using System.Windows;
using System.Windows.Controls;

namespace PowerPen.UI;

/// <summary>
/// A toolbar button that can show an "active" highlight (current tool, selected color).
/// </summary>
/// <remarks>
/// Plain RadioButton/ToggleButton would flip their own checked state when clicked, which breaks the link to
/// <see cref="Core.AppState"/>. This button only ever displays the state it's bound to.
/// </remarks>
public class ToolButton : Button
{
    /// <summary>Identifies the <see cref="IsActive"/> dependency property.</summary>
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(ToolButton), new PropertyMetadata(false));

    /// <summary>True to show the button highlighted as the current selection.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }
}
