using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Repository interface for UserFavorite persistence
/// </summary>
public interface IFavoritesRepository
{
    /// <summary>
    /// Add a new favorite
    /// </summary>
    Task AddAsync(UserFavorite favorite);

    /// <summary>
    /// PERFORMANCE: Add multiple favorites at once
    /// </summary>
    Task AddRangeAsync(IEnumerable<UserFavorite> favorites);

    /// <summary>
    /// Get all favorites for a user, ordered by DisplayOrder
    /// </summary>
    /// <param name="userId">User ID (nullable for single-user mode)</param>
    Task<IEnumerable<UserFavorite>> GetAllAsync(string? userId = null);

    /// <summary>
    /// Get favorite by action ID
    /// </summary>
    Task<UserFavorite?> GetByActionIdAsync(string actionId, string? userId = null);

    /// <summary>
    /// Check if an action is favorited
    /// </summary>
    Task<bool> IsFavoriteAsync(string actionId, string? userId = null);

    /// <summary>
    /// Remove a favorite
    /// </summary>
    Task RemoveAsync(string favoriteId);

    /// <summary>
    /// Remove favorite by action ID
    /// </summary>
    Task RemoveByActionIdAsync(string actionId, string? userId = null);

    /// <summary>
    /// Get count of favorites for a user
    /// </summary>
    Task<int> GetCountAsync(string? userId = null);

    /// <summary>
    /// Update favorite display order
    /// </summary>
    Task UpdateDisplayOrderAsync(string favoriteId, int newOrder);

    /// <summary>
    /// Clear all favorites for a user
    /// </summary>
    Task ClearAllAsync(string? userId = null);
}
