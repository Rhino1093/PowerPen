using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using PowerPen.Interop;
using PowerPen.Settings;
using PowerPen.UI;

namespace PowerPen.Capture;

/// <summary>What happened when the user took a screenshot.</summary>
/// <param name="Image">The picture, or null if the user cancelled (or the screen couldn't be read).</param>
/// <param name="SavedPath">Where it was saved, or null if it wasn't.</param>
/// <param name="CopiedToClipboard">True if it's on the clipboard.</param>
public sealed record ScreenshotOutcome(BitmapSource? Image, string? SavedPath, bool CopiedToClipboard)
{
    /// <summary>The outcome when nothing was captured.</summary>
    public static ScreenshotOutcome Cancelled { get; } = new(null, null, false);
}

/// <summary>
/// The 📷 flow: freeze every monitor, let the user pick an area or a whole screen, then save it as a PNG,
/// copy it to the clipboard, and confirm with a toast.
/// </summary>
public sealed class ScreenshotService
{
    /// <summary>
    /// Pause after the 📷 click before freezing the screen, so the button's tooltip has time to disappear.
    /// (The toolbar itself is excluded from captures, but its tooltip is a separate window.)
    /// </summary>
    private const int SettleDelayMs = 150;

    private readonly AppSettings _settings;
    private ToastWindow? _toast;

    /// <summary>Creates the service using the screenshot settings in <paramref name="settings"/>.</summary>
    public ScreenshotService(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Raised after every screenshot attempt, including cancelled ones.</summary>
    public event EventHandler<ScreenshotOutcome>? Completed;

    /// <summary>
    /// Self-test only: keep the picture in memory instead of saving it, copying it, or showing a toast,
    /// so testing never writes the user's screen to disk or replaces their clipboard.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>True while a screenshot is in progress.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>The folder screenshots are saved to: the setting if set, otherwise Downloads.</summary>
    public string Folder => string.IsNullOrWhiteSpace(_settings.ScreenshotFolder)
        ? KnownFolders.Downloads
        : Environment.ExpandEnvironmentVariables(_settings.ScreenshotFolder);

    /// <summary>Runs the whole screenshot flow. Does nothing if one is already in progress.</summary>
    public async Task<ScreenshotOutcome> TakeAsync()
    {
        if (IsBusy)
            return ScreenshotOutcome.Cancelled;

        IsBusy = true;
        try
        {
            ScreenshotOutcome outcome = await TakeCoreAsync();
            Completed?.Invoke(this, outcome);
            return outcome;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Capture → select → crop → save/copy/toast.</summary>
    private async Task<ScreenshotOutcome> TakeCoreAsync()
    {
        await Task.Delay(SettleDelayMs);

        // Capture every monitor first, so the picture is exactly what was on screen at the click.
        List<CapturedScreen> screens;
        try
        {
            screens = MonitorService.GetMonitors()
                .Select(m => new CapturedScreen(m, ScreenCapture.CaptureImage(m.Bounds)))
                .ToList();
        }
        catch (Win32Exception ex)
        {
            ShowProblem($"PowerPen couldn't read the screen ({ex.Message}).\n\n" +
                        "To fix: try again. If a Windows security prompt is open, close it first.");
            return ScreenshotOutcome.Cancelled;
        }

        if (await RegionSelector.SelectAsync(screens) is not ScreenRegion region)
            return ScreenshotOutcome.Cancelled;

        var image = new CroppedBitmap(region.Screen.Image, region.Area);
        image.Freeze();

        if (DryRun)
            return new ScreenshotOutcome(image, null, false);

        bool copied = _settings.CopyScreenshotToClipboard && await CopyToClipboardAsync(image);
        string? path = Save(image, copied);
        if (path != null)
            ShowToast(path, copied, region.Screen.Monitor);
        return new ScreenshotOutcome(image, path, copied);
    }

    /// <summary>Saves the PNG, or explains what went wrong and how to fix it.</summary>
    private string? Save(BitmapSource image, bool copied)
    {
        string folder = Folder;
        try
        {
            return ScreenshotFiles.SavePng(image, folder, DateTime.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ShowProblem($"The screenshot couldn't be saved to:\n{folder}\n\n({ex.Message})\n\n" +
                        (copied ? "It was copied to the clipboard, so you can still paste it.\n\n" : "") +
                        "To fix: make sure that folder exists and you can save files there, " +
                        "or change \"screenshotFolder\" in the settings file (leave it empty for Downloads).");
            return null;
        }
    }

    /// <summary>Copies the picture to the clipboard. Returns false if another app kept the clipboard busy.</summary>
    private static async Task<bool> CopyToClipboardAsync(BitmapSource image)
    {
        // Other apps (clipboard managers, Remote Desktop) can hold the clipboard open for a moment; retry briefly.
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                return true;
            }
            catch (ExternalException)
            {
                await Task.Delay(100);
            }
        }
        return false;
    }

    /// <summary>Shows a "saved" toast on the monitor the screenshot came from. Clicking it opens the folder.</summary>
    private void ShowToast(string path, bool copied, DisplayMonitor monitor)
    {
        _toast?.Close(); // One at a time.

        string title = copied ? "Screenshot saved and copied" : "Screenshot saved";
        string detail = $"{Path.GetFileName(path)} · click to open the folder";
        if (_settings.CopyScreenshotToClipboard && !copied)
            detail = $"{Path.GetFileName(path)} · couldn't copy (clipboard busy)";

        _toast = new ToastWindow(title, detail, monitor, () => OpenFolder(path));
        _toast.Closed += (sender, _) =>
        {
            if (ReferenceEquals(_toast, sender))
                _toast = null;
        };
        _toast.Show();
    }

    /// <summary>Opens File Explorer with the screenshot selected.</summary>
    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            ShowProblem($"File Explorer couldn't be opened ({ex.Message}).\n\nThe screenshot is here:\n{path}");
        }
    }

    /// <summary>Shows a problem the user can act on.</summary>
    private static void ShowProblem(string message) =>
        MessageBox.Show(message, "PowerPen screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
}
