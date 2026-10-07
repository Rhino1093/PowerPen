using System.IO;
using PowerPen.Settings;

namespace PowerPen.Tests;

/// <summary>Tests for reading and writing settings.json, using a throwaway folder.</summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PowerPenTests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _problems = [];

    /// <summary>Path of the settings file inside the throwaway folder (which doesn't exist yet).</summary>
    private string SettingsPath => Path.Combine(_folder, "settings.json");

    /// <summary>Deletes the throwaway folder.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    /// <summary>First run: the file is created with defaults so the user has something to edit.</summary>
    [Fact]
    public void MissingFile_CreatesDefaults()
    {
        AppSettings settings = SettingsService.Load(SettingsPath, _problems);

        Assert.True(File.Exists(SettingsPath));
        Assert.Equal("Ctrl+Alt+D", settings.Hotkeys.ToggleDraw);
        Assert.Empty(_problems);
    }

    /// <summary>Saved values load back unchanged.</summary>
    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var saved = new AppSettings { HighlighterWidthMultiplier = 5 };
        saved.Hotkeys.ToggleDraw = "Ctrl+Alt+P";
        saved.Colors[0] = new ColorSetting { Name = "Teal", Color = "#008080" };
        SettingsService.Save(SettingsPath, saved);

        AppSettings loaded = SettingsService.Load(SettingsPath, _problems);

        Assert.Equal(5, loaded.HighlighterWidthMultiplier);
        Assert.Equal("Ctrl+Alt+P", loaded.Hotkeys.ToggleDraw);
        Assert.Equal("Teal", loaded.Colors[0].Name);
        Assert.Empty(_problems);
    }

    /// <summary>Hotkeys are written readably ("Ctrl+Alt+D"), not escaped ("Ctrl+Alt+D").</summary>
    [Fact]
    public void Save_WritesHotkeysReadably()
    {
        SettingsService.Save(SettingsPath, new AppSettings());

        string text = File.ReadAllText(SettingsPath);
        Assert.Contains("\"Ctrl+Alt+D\"", text);
        Assert.DoesNotContain("\\u002B", text);
    }

    /// <summary>A hand-written file with only some values, comments, and a trailing comma loads fine.</summary>
    [Fact]
    public void PartialHandEditedFile_KeepsDefaultsForTheRest()
    {
        WriteFile("""
            {
              // Wider highlighter
              "highlighterWidthMultiplier": 4,
              "hotkeys": { "quit": "" },
            }
            """);

        AppSettings settings = SettingsService.Load(SettingsPath, _problems);

        Assert.Equal(4, settings.HighlighterWidthMultiplier);
        Assert.Equal("", settings.Hotkeys.Quit);              // Turned off.
        Assert.Equal("Ctrl+Alt+D", settings.Hotkeys.ToggleDraw); // Not in the file: default.
        Assert.Equal(AppSettings.MaxColors, settings.Colors.Count);
        Assert.Empty(_problems);
    }

    /// <summary>A broken file is set aside as settings.bad.json, defaults are restored, and the user is told where.</summary>
    [Fact]
    public void CorruptFile_IsSetAsideAndDefaultsRestored()
    {
        WriteFile("{ \"highlighterWidthMultiplier\": 4, oops }");

        AppSettings settings = SettingsService.Load(SettingsPath, _problems);

        Assert.Equal(3, settings.HighlighterWidthMultiplier);
        Assert.True(File.Exists(Path.Combine(_folder, "settings.bad.json")));
        Assert.True(File.Exists(SettingsPath)); // Fresh defaults written.
        Assert.Contains("settings.bad.json", Assert.Single(_problems));
    }

    /// <summary>Invalid values inside a readable file are fixed in memory but the file is left alone for the user to fix.</summary>
    [Fact]
    public void InvalidValue_IsReportedButFileUntouched()
    {
        string json = """{ "highlighterWidthMultiplier": 50 }""";
        WriteFile(json);

        AppSettings settings = SettingsService.Load(SettingsPath, _problems);

        Assert.Equal(3, settings.HighlighterWidthMultiplier);
        Assert.Single(_problems);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    /// <summary>Writes raw text to the settings file.</summary>
    private void WriteFile(string text)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, text);
    }
}
