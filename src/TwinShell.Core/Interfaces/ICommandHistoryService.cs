using TwinShell.Core.Enums;
using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service interface for command history management
/// </summary>
public interface ICommandHistoryService
{
    /// <summary>
    /// Add a command to history
    /// </summary>
    /// <param name="actionId">ID of the action</param>
    /// <param name="generatedCommand">The generated command text</param>
    /// <param name="parameters">Parameters used to generate the command</param>
    /// <param name="platform">Platform for which the command was generated</param>
    /// <param name="actionTitle">Title of the action (for denormalization)</param>
    /// <param name="category">Category of the action (for denormalization)</param>
    /// <returns>The ID of the created history entry</returns>
    Task<string> AddCommandAsync(
        string actionId,
        string generatedCommand,
        Dictionary<string, string> parameters,
        Platform platform,
        string actionTitle,
        string category);

    /// <summary>
    /// Update a command history entry with execution results
    /// </summary>
    /// <param name="historyId">ID of the history entry to update</param>
    /// <param name="exitCode">Exit code from the command execution</param>
    /// <param name="duration">Execution duration</param>
    /// <param name="success">Whether the execution was successful</param>
    Task UpdateWithExecutionResultsAsync(
        string historyId,
        int exitCode,
        TimeSpan duration,
        bool success);

    /// <summary>
    /// Get the most recent command history entries
    /// </summary>
    /// <param name="count">Number of entries to retrieve (default: 50)</param>
    Task<IEnumerable<CommandHistory>> GetRecentAsync(int count = 50);

    /// <summary>
    /// Search command history with filters
    /// </summary>
    Task<IEnumerable<CommandHistory>> SearchAsync(
        string? searchText = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        Platform? platform = null,
        string? category = null);

    /// <summary>
    /// Get command history by ID
    /// </summary>
    Task<CommandHistory?> GetByIdAsync(string id);

    /// <summary>
    /// Delete a specific history entry
    /// </summary>
    Task DeleteAsync(string id);

    /// <summary>
    /// Delete multiple history entries
    /// </summary>
    Task DeleteRangeAsync(IEnumerable<string> ids);

    /// <summary>
    /// Clean up old history entries (older than specified days)
    /// </summary>
    /// <param name="daysToKeep">Number of days to keep (default: 90)</param>
    Task CleanupOldEntriesAsync(int daysToKeep = 90);

    /// <summary>
    /// Get total count of history entries
    /// </summary>
    Task<int> GetCountAsync();

    /// <summary>
    /// Clear all command history
    /// </summary>
    Task ClearAllAsync();
}
