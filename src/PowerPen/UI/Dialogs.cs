using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace PowerPen.UI;

/// <summary>Standard Windows pickers used by the Settings window.</summary>
internal static class Dialogs
{
    /// <summary>Shows the Windows color picker. Returns the chosen color, or null if cancelled.</summary>
    public static Color? PickColor(Color initial)
    {
        // WPF has no color picker; the classic Windows one (via WinForms) is familiar and good enough here.
        using var dialog = new WinForms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(initial.R, initial.G, initial.B),
            FullOpen = true,
        };

        if (dialog.ShowDialog(ActiveWindowOwner()) != WinForms.DialogResult.OK)
            return null;

        return Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
    }

    /// <summary>Shows the Windows folder picker. Returns the chosen folder, or null if cancelled.</summary>
    public static string? PickFolder(string initialFolder)
    {
        var dialog = new OpenFolderDialog { Title = "Save screenshots to", InitialDirectory = initialFolder };
        Window? owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        bool picked = owner != null ? dialog.ShowDialog(owner) == true : dialog.ShowDialog() == true;
        return picked ? dialog.FolderName : null;
    }

    /// <summary>The active PowerPen window as a WinForms dialog owner, so the dialog stays on top of it.</summary>
    private static WinForms.IWin32Window? ActiveWindowOwner()
    {
        Window? active = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        return active == null ? null : new DialogOwner(new WindowInteropHelper(active).Handle);
    }

    /// <summary>Wraps a window handle for WinForms dialogs.</summary>
    private sealed class DialogOwner(IntPtr handle) : WinForms.IWin32Window
    {
        /// <summary>The owner's window handle.</summary>
        public IntPtr Handle { get; } = handle;
    }
}
