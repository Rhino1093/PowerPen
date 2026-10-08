using System.Windows;
using PowerPen.Diagnostics;

namespace PowerPen.UI;

/// <summary>The Settings window: colors, sizes, hotkeys, screenshots, and starting with Windows.</summary>
public partial class SettingsWindow : Window
{
    /// <summary>Creates the window for <paramref name="viewModel"/>; it closes itself after Save or Cancel.</summary>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
    }

    /// <summary>Renders the window's content to a PNG without showing it (development aid; see <see cref="VisualSnapshot"/>).</summary>
    internal void SaveSnapshot(string path)
    {
        Root.DataContext ??= DataContext;
        VisualSnapshot.Save(Root, path, width: Width);
    }
}
