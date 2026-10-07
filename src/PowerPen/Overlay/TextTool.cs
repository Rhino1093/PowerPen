using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PowerPen.Overlay;

/// <summary>Event data for a text item being added to or removed from an overlay.</summary>
/// <param name="item">The text item.</param>
/// <param name="wasAdded">True if added; false if removed.</param>
public sealed class TextItemChangedEventArgs(UIElement item, bool wasAdded) : EventArgs
{
    /// <summary>The text item.</summary>
    public UIElement Item { get; } = item;

    /// <summary>True if added; false if removed.</summary>
    public bool WasAdded { get; } = wasAdded;
}

/// <summary>
/// Typed text on one overlay: click to place a box, type, then Enter (or click elsewhere) to finish.
/// </summary>
/// <remarks>
/// A finished item is the same TextBox made read-only and borderless, rather than a new TextBlock.
/// That way the text doesn't shift by a pixel or two when you finish typing.
/// Items are marked with <see cref="ItemTag"/> so they can be told apart from the box being edited.
/// </remarks>
internal sealed class TextTool
{
    /// <summary>Marks finished text items among the InkCanvas children.</summary>
    private const string ItemTag = "PowerPen.TextItem";

    /// <summary>Faint border shown only while editing, so you can see where the box is.</summary>
    private static readonly Brush EditingBorder = Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0x80, 0x80, 0x80)));

    private static readonly FontFamily TextFont = new("Segoe UI");

    private readonly InkCanvas _canvas;
    private TextBox? _editor;
    private Color _color = Colors.Red;
    private double _fontSize = 28;

    /// <summary>Creates a text tool that places its items on <paramref name="canvas"/>.</summary>
    public TextTool(InkCanvas canvas)
    {
        _canvas = canvas;
    }

    /// <summary>Raised when a finished text item is added or removed (for undo).</summary>
    public event EventHandler<TextItemChangedEventArgs>? ItemChanged;

    /// <summary>True while a text box is open for typing.</summary>
    public bool IsEditing => _editor != null;

    /// <summary>Finished text items on this overlay.</summary>
    public IEnumerable<TextBox> Items => _canvas.Children.OfType<TextBox>().Where(t => Equals(t.Tag, ItemTag));

    /// <summary>Sets the color and size for new text, and restyles the box being typed in (so picking a color applies right away).</summary>
    public void SetStyle(Color color, double fontSize)
    {
        _color = color;
        _fontSize = fontSize;
        if (_editor != null)
            ApplyStyle(_editor);
    }

    /// <summary>True if <paramref name="source"/> is (inside) the box being edited, e.g. a click to move the caret.</summary>
    public bool IsInsideEditor(object source) =>
        _editor != null && source is DependencyObject element && (element == _editor || _editor.IsAncestorOf(element));

    /// <summary>Opens a text box at <paramref name="position"/> (overlay coordinates) and focuses it.</summary>
    public void BeginAt(Point position)
    {
        Commit();

        var editor = new TextBox
        {
            AcceptsReturn = true,
            MinWidth = 40,
            Background = Brushes.Transparent,
            BorderBrush = EditingBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2, 0, 2, 0),
            FontFamily = TextFont,
            FontWeight = FontWeights.SemiBold,
        };
        ApplyStyle(editor);

        // Center the first line on the click point, which feels like "type here".
        InkCanvas.SetLeft(editor, position.X);
        InkCanvas.SetTop(editor, position.Y - _fontSize * 0.7);

        editor.PreviewKeyDown += OnEditorKeyDown;
        editor.LostKeyboardFocus += OnEditorLostFocus;
        // Focus only once the box is on screen; focusing earlier is silently ignored.
        editor.Loaded += (_, _) => editor.Focus();

        _editor = editor;
        _canvas.Children.Add(editor);
    }

    /// <summary>Finishes the box being edited: keeps it as a text item, or removes it if it's empty.</summary>
    public void Commit()
    {
        if (_editor is not TextBox editor)
            return;

        // Clear the field first: the changes below make the box lose focus, which calls Commit again.
        _editor = null;
        editor.PreviewKeyDown -= OnEditorKeyDown;
        editor.LostKeyboardFocus -= OnEditorLostFocus;
        ReturnFocusToOverlay(editor);

        if (string.IsNullOrWhiteSpace(editor.Text))
        {
            _canvas.Children.Remove(editor);
            return;
        }

        // Turn the box into a finished item: same element, so the text stays exactly where it was typed.
        // The border keeps its thickness (just invisible) for the same reason.
        editor.IsReadOnly = true;
        editor.BorderBrush = Brushes.Transparent;
        editor.Focusable = false;
        editor.IsHitTestVisible = false; // Lets the pen draw over text; the eraser finds text by position instead.
        editor.Tag = ItemTag;
        ItemChanged?.Invoke(this, new TextItemChangedEventArgs(editor, wasAdded: true));
    }

    /// <summary>Discards the box being edited.</summary>
    public void Cancel()
    {
        if (_editor is not TextBox editor)
            return;

        _editor = null;
        editor.PreviewKeyDown -= OnEditorKeyDown;
        editor.LostKeyboardFocus -= OnEditorLostFocus;
        ReturnFocusToOverlay(editor);
        _canvas.Children.Remove(editor);
    }

    /// <summary>
    /// If the box being closed has keyboard focus, hands it to the overlay window, so the next key press
    /// reaches the Draw-mode shortcuts instead of a read-only (or removed) box.
    /// </summary>
    private void ReturnFocusToOverlay(TextBox editor)
    {
        if (editor.IsKeyboardFocusWithin)
            Window.GetWindow(_canvas)?.Focus();
    }

    /// <summary>Removes every text item within <paramref name="radius"/> of <paramref name="position"/>.</summary>
    public void EraseAt(Point position, double radius)
    {
        foreach (TextBox item in Items.ToList()) // Copy: removing while looping over the live list would fail.
        {
            var bounds = new Rect(InkCanvas.GetLeft(item), InkCanvas.GetTop(item), item.ActualWidth, item.ActualHeight);
            bounds.Inflate(radius, radius);
            if (bounds.Contains(position))
                Remove(item);
        }
    }

    /// <summary>Removes all text items (for Clear all).</summary>
    public void RemoveAll()
    {
        Cancel();
        foreach (TextBox item in Items.ToList())
            Remove(item);
    }

    /// <summary>Removes one item and reports it for undo.</summary>
    private void Remove(TextBox item)
    {
        _canvas.Children.Remove(item);
        ItemChanged?.Invoke(this, new TextItemChangedEventArgs(item, wasAdded: false));
    }

    /// <summary>Enter finishes, Shift+Enter adds a line, Esc cancels.</summary>
    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            Commit();
            e.Handled = true;
        }
    }

    /// <summary>Finishes the text when focus moves away (e.g. switching apps).</summary>
    private void OnEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    /// <summary>Applies the current color and size to a text box.</summary>
    private void ApplyStyle(TextBox box)
    {
        var brush = Frozen(new SolidColorBrush(_color));
        box.Foreground = brush;
        box.CaretBrush = brush;
        box.FontSize = _fontSize;
    }

    /// <summary>Freezes a brush (read-only, faster to render) and returns it.</summary>
    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
