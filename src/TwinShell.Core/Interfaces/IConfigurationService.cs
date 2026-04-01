using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service interface for configuration export/import
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Export user configuration to JSON file
    /// </summary>
    /// <param name="filePath">Path where to save the JSON file</param>
    /// <param name="userId">User ID (nullable for single-user mode)</param>
    /// <param name="includeHistory">Include command history in export</param>
    Task<(bool Success, string? ErrorMessage)> ExportToJsonAsync(
        string filePath,
        string? userId = null,
        bool includeHistory = true);

    /// <summary>
    /// Import user configuration from JSON file
    /// </summary>
    /// <param name="filePath">Path to the JSON file to import</param>
    /// <param name="userId">User ID (nullable for single-user mode)</param>
    /// <param name="mergeMode">If true, merge with existing data; if false, replace</param>
    Task<(bool Success, string? ErrorMessage, int FavoritesImported, int HistoryImported)> ImportFromJsonAsync(
        string filePath,
        string? userId = null,
        bool mergeMode = true);

    /// <summary>
    /// Validate a configuration JSON file
    /// </summary>
    Task<(bool IsValid, string? ErrorMessage, string? Version)> ValidateConfigurationFileAsync(string filePath);
}
