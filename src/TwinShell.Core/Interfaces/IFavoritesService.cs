using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service interface for managing user favorites
/// </summary>
public interface IFavoritesService
{
    /// <summary>
    /// Add an action to favorites
    /// </summary>
    /// <param name="actionId">Action ID to favorite</param>
    /// <param name="userId">User ID (nullable for single-user mode)</param>
    /// <returns>Success result with error message if limit exceeded</returns>
    Task<(bool Success, string? ErrorMessage)> AddFavoriteAsync(string actionId, string? userId = null);

    /// <summary>
    /// Remove an action from favorites
    /// </summary>
    Task RemoveFavoriteAsync(string actionId, string? userId = null);

    /// <summary>
    /// Toggle favorite status for an action
    /// </summary>
    /// <returns>New favorite status (true if now favorited, false if unfavorited)</returns>
    Task<bool> ToggleFavoriteAsync(string actionId, string? userId = null);

    /// <summary>
    /// Check if an action is favorited
    /// </summary>
    Task<bool> IsFavoriteAsync(string actionId, string? userId = null);

    /// <summary>
    /// Get all favorite actions
    /// </summary>
    Task<IEnumerable<UserFavorite>> GetAllFavoritesAsync(string? userId = null);

    /// <summary>
    /// Get count of favorites
    /// </summary>
    Task<int> GetFavoriteCountAsync(string? userId = null);

    /// <summary>
    /// Reorder a favorite
    /// </summary>
    Task ReorderFavoriteAsync(string favoriteId, int newOrder);

    /// <summary>
    /// Clear all favorites
    /// </summary>
    Task ClearAllFavoritesAsync(string? userId = null);
}
