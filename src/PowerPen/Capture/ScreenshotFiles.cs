using System.IO;
using System.Windows.Media.Imaging;

namespace PowerPen.Capture;

/// <summary>Naming and saving screenshot files.</summary>
public static class ScreenshotFiles
{
    /// <summary>File name for a screenshot taken at <paramref name="time"/>, e.g. "PowerPen_2026-10-07_143205.png".</summary>
    public static string CreateFileName(DateTime time) => $"PowerPen_{time:yyyy-MM-dd_HHmmss}.png";

    /// <summary>
    /// Full path for <paramref name="fileName"/> in <paramref name="folder"/>. If that file already exists
    /// (two screenshots in the same second), adds _2, _3, ... so nothing is ever overwritten.
    /// </summary>
    public static string GetUniquePath(string folder, string fileName)
    {
        string path = Path.Combine(folder, fileName);
        string name = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{name}_{n}{extension}");
        return path;
    }

    /// <summary>
    /// Saves <paramref name="image"/> as a PNG in <paramref name="folder"/> (created if missing) and returns the file path.
    /// </summary>
    /// <exception cref="IOException">The folder can't be created or written to.</exception>
    /// <exception cref="UnauthorizedAccessException">No permission to write to the folder.</exception>
    public static string SavePng(BitmapSource image, string folder, DateTime time)
    {
        Directory.CreateDirectory(folder);
        string path = GetUniquePath(folder, CreateFileName(time));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        // CreateNew (not Create) so an existing file can never be overwritten, even in a race with another save.
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write);
        encoder.Save(file);
        return path;
    }
}
