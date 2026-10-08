using System.IO;
using PowerPen.Diagnostics;

namespace PowerPen.Settings;

/// <summary>Where the settings file is, and whether it's safe to write to it this session.</summary>
public sealed class SettingsStore
{
    /// <summary>Creates a store for the file at <paramref name="path"/>.</summary>
    /// <param name="path">The settings file, or null to never write anything.</param>
    /// <param name="canSave">False if writing could destroy the user's settings (see <see cref="SettingsService.Load(string, List{string}, out bool)"/>).</param>
    public SettingsStore(string? path, bool canSave)
    {
        Path = path;
        CanSave = canSave && path != null;
    }

    /// <summary>The settings file, or null for an in-memory store.</summary>
    public string? Path { get; }

    /// <summary>True if saving is allowed.</summary>
    public bool CanSave { get; }

    /// <summary>A store that never writes to disk (self-test and dev tools).</summary>
    public static SettingsStore InMemory() => new(null, false);

    /// <summary>Saves <paramref name="settings"/>. Never throws; returns false and explains why if the file can't be written.</summary>
    public bool TrySave(AppSettings settings, out string? error)
    {
        error = null;
        if (!CanSave)
        {
            error = "the settings file couldn't be read when PowerPen started, so it isn't being overwritten";
            return false;
        }

        try
        {
            SettingsService.Save(Path!, settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            Log.Error($"Couldn't save settings to {Path}", ex);
            return false;
        }
    }
}
