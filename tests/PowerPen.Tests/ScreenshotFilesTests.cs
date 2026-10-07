using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PowerPen.Capture;

namespace PowerPen.Tests;

/// <summary>Tests for screenshot file naming and saving, using a throwaway folder and a tiny generated image.</summary>
public sealed class ScreenshotFilesTests : IDisposable
{
    private static readonly DateTime SampleTime = new(2026, 10, 7, 14, 32, 5);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PowerPenTests", Guid.NewGuid().ToString("N"));

    /// <summary>Deletes the throwaway folder.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    /// <summary>File names sort by date and time and say where they came from.</summary>
    [Fact]
    public void CreateFileName_UsesDateAndTime()
    {
        Assert.Equal("PowerPen_2026-10-07_143205.png", ScreenshotFiles.CreateFileName(SampleTime));
    }

    /// <summary>Two screenshots in the same second get _2, _3, ... instead of overwriting each other.</summary>
    [Fact]
    public void GetUniquePath_AddsNumberWhenNameIsTaken()
    {
        Directory.CreateDirectory(_folder);
        string name = ScreenshotFiles.CreateFileName(SampleTime);

        Assert.Equal(Path.Combine(_folder, name), ScreenshotFiles.GetUniquePath(_folder, name));

        File.WriteAllText(Path.Combine(_folder, name), "");
        Assert.Equal(Path.Combine(_folder, "PowerPen_2026-10-07_143205_2.png"), ScreenshotFiles.GetUniquePath(_folder, name));

        File.WriteAllText(Path.Combine(_folder, "PowerPen_2026-10-07_143205_2.png"), "");
        Assert.Equal(Path.Combine(_folder, "PowerPen_2026-10-07_143205_3.png"), ScreenshotFiles.GetUniquePath(_folder, name));
    }

    /// <summary>Saving creates the folder if needed and writes a real PNG of the right size; a second save never overwrites the first.</summary>
    [Fact]
    public void SavePng_WritesReadablePngWithoutOverwriting()
    {
        BitmapSource image = CreateImage(width: 4, height: 3);

        string first = ScreenshotFiles.SavePng(image, _folder, SampleTime);
        string second = ScreenshotFiles.SavePng(image, _folder, SampleTime);

        Assert.NotEqual(first, second);
        foreach (string path in new[] { first, second })
        {
            using FileStream file = File.OpenRead(path);
            BitmapFrame frame = new PngBitmapDecoder(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal(4, frame.PixelWidth);
            Assert.Equal(3, frame.PixelHeight);
        }
    }

    /// <summary>Creates a small solid-gray image.</summary>
    private static BitmapSource CreateImage(int width, int height)
    {
        int stride = width * 3;
        byte[] pixels = Enumerable.Repeat((byte)0x80, stride * height).ToArray();
        BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
        image.Freeze();
        return image;
    }
}
