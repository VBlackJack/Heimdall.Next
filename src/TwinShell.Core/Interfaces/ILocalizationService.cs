using System.Globalization;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for managing application localization
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets the current culture
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// Gets an array of supported cultures
    /// </summary>
    CultureInfo[] SupportedCultures { get; }

    /// <summary>
    /// Changes the application language
    /// </summary>
    /// <param name="culture">The culture to change to</param>
    void ChangeLanguage(CultureInfo culture);

    /// <summary>
    /// Changes the application language by culture code
    /// </summary>
    /// <param name="cultureCode">The culture code (e.g., "en", "fr", "es")</param>
    void ChangeLanguage(string cultureCode);

    /// <summary>
    /// Gets a localized string by key
    /// </summary>
    /// <param name="key">The resource key</param>
    /// <returns>Localized string</returns>
    string GetString(string key);

    /// <summary>
    /// Gets a localized string with fallback
    /// </summary>
    /// <param name="key">The resource key</param>
    /// <param name="fallback">Fallback value if key not found</param>
    /// <returns>Localized string or fallback</returns>
    string GetString(string key, string fallback);

    /// <summary>
    /// Gets a formatted localized string with parameters
    /// </summary>
    /// <param name="key">The resource key</param>
    /// <param name="args">Format arguments</param>
    /// <returns>Formatted localized string</returns>
    string GetFormattedString(string key, params object[] args);

    /// <summary>
    /// Event raised when the language changes
    /// </summary>
    event EventHandler? LanguageChanged;
}
