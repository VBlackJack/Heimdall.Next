using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for managing user settings and preferences.
/// Settings are persisted in JSON format at %APPDATA%/TwinShell/settings.json
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets the current user settings.
    /// </summary>
    UserSettings CurrentSettings { get; }

    /// <summary>
    /// Loads user settings from the JSON file.
    /// If the file doesn't exist, returns default settings.
    /// </summary>
    /// <returns>The loaded or default user settings.</returns>
    Task<UserSettings> LoadSettingsAsync();

    /// <summary>
    /// Saves user settings to the JSON file.
    /// </summary>
    /// <param name="settings">The settings to save.</param>
    /// <returns>True if save was successful, false otherwise.</returns>
    Task<bool> SaveSettingsAsync(UserSettings settings);

    /// <summary>
    /// Resets settings to default values and saves to file.
    /// </summary>
    /// <returns>The default settings.</returns>
    Task<UserSettings> ResetToDefaultAsync();

    /// <summary>
    /// Gets the path where settings are stored.
    /// </summary>
    /// <returns>The full path to settings.json</returns>
    string GetSettingsFilePath();

    /// <summary>
    /// Validates user settings to ensure all values are within acceptable ranges.
    /// </summary>
    /// <param name="settings">The settings to validate.</param>
    /// <returns>True if settings are valid, false otherwise.</returns>
    bool ValidateSettings(UserSettings settings);
}
