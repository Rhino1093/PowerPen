using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using PowerPen.Diagnostics;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace PowerPen.UI;

/// <summary>
/// The PowerPen icon in the notification area (by the clock): click it to bring back the toolbar; right-click for
/// Settings, folders, and Exit. It's always there, so PowerPen can never get "lost" while running.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly Drawing.Icon _image;

    /// <summary>Shows the tray icon.</summary>
    /// <param name="host">The running app.</param>
    /// <param name="version">Shown in the hover text, e.g. "1.0.0".</param>
    public TrayIcon(AppHost host, string version)
    {
        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.Add("Show toolbar", null, (_, _) => host.ShowToolbar());
        _menu.Items.Add("Settings…", null, (_, _) => host.Commands.OpenSettings());
        _menu.Items.Add("Open screenshots folder", null, (_, _) => OpenFolder(host.Screenshots.Folder));
        _menu.Items.Add("Open log folder", null, (_, _) => OpenFolder(Log.Folder));
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Exit PowerPen", null, (_, _) => host.Commands.Quit());

        _image = LoadIcon();
        _icon = new WinForms.NotifyIcon
        {
            Icon = _image,
            Text = $"PowerPen {version}",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                host.ShowToolbar();
        };
    }

    /// <summary>Removes the icon from the notification area.</summary>
    public void Dispose()
    {
        // Hide first, otherwise a "ghost" icon can linger until the mouse passes over it.
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }

    /// <summary>Loads PowerPen's icon at the size Windows uses for the notification area.</summary>
    private static Drawing.Icon LoadIcon()
    {
        try
        {
            using Stream stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/PowerPen.ico")).Stream;
            return new Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            Log.Error("Couldn't load the tray icon", ex);
            return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }
    }

    /// <summary>Opens a folder in File Explorer, creating it first if it doesn't exist yet.</summary>
    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            MessageBox.Show($"This folder couldn't be opened:\n{folder}\n\n({ex.Message})", "PowerPen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
