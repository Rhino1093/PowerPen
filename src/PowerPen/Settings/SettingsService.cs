using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PowerPen.Settings;

/// <summary>Reads and writes settings.json.</summary>
public static class SettingsService
{
    /// <summary>Where settings live: %AppData%\PowerPen\settings.json.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerPen", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        // People edit this file by hand, so tolerate comments and trailing commas.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // The default encoder escapes '+' as + (to be safe inside HTML), turning "Ctrl+Alt+D" into
        // "Ctrl+Alt+D". This file is never put in a web page, so write characters as-is.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Loads settings. Never throws: a missing file is created with defaults, an unreadable file is set aside as
    /// settings.bad.json, and invalid values are replaced with defaults. Each fix is described in <paramref name="problems"/>.
    /// </summary>
    /// <param name="path">The settings file.</param>
    /// <param name="problems">Receives user-friendly descriptions of anything that had to be fixed.</param>
    public static AppSettings Load(string path, List<string> problems)
    {
        AppSettings? settings = null;

        if (File.Exists(path))
        {
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                    ?? throw new JsonException("The file contains no settings.");
            }
            catch (JsonException ex)
            {
                // Keep the user's broken file so their edits aren't lost, then start fresh.
                string backupPath = Path.ChangeExtension(path, ".bad.json");
                string where = ex.LineNumber is long line ? $" near line {line + 1}" : "";
                try
                {
                    File.Move(path, backupPath, overwrite: true);
                    problems.Add($"The settings file has a mistake{where}, so default settings were restored. Your file was saved as {Path.GetFileName(backupPath)}.");
                }
                catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
                {
                    problems.Add($"The settings file has a mistake{where}. Using default settings until it's fixed.");
                    return Validated(new AppSettings(), problems);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file exists but can't be opened (e.g. locked by an editor). Don't overwrite it.
                problems.Add($"The settings file couldn't be opened ({ex.Message}). Using default settings for now.");
                return Validated(new AppSettings(), problems);
            }
        }

        if (settings is null)
        {
            // First run (or the old file was set aside): write the defaults so there's a file to edit.
            settings = new AppSettings();
            try
            {
                Save(path, settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"The settings file couldn't be created ({ex.Message}). PowerPen will use default settings.");
            }
        }

        return Validated(settings, problems);
    }

    /// <summary>Writes settings to <paramref name="path"/>, creating the folder if needed.</summary>
    public static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }

    /// <summary>Validates <paramref name="settings"/> and returns it.</summary>
    private static AppSettings Validated(AppSettings settings, List<string> problems)
    {
        settings.Validate(problems);
        return settings;
    }
}
