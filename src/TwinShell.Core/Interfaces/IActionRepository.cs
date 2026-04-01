using TwinShell.Core.Models;
using ActionModel = TwinShell.Core.Models.Action;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Repository interface for Action persistence
/// </summary>
public interface IActionRepository
{
    Task<IEnumerable<ActionModel>> GetAllAsync();
    Task<ActionModel?> GetByIdAsync(string id);
    Task<IEnumerable<ActionModel>> GetByCategoryAsync(string category);
    Task<IEnumerable<string>> GetAllCategoriesAsync();
    Task AddAsync(ActionModel action);
    Task UpdateAsync(ActionModel action);
    Task DeleteAsync(string id);
    Task<bool> ExistsAsync(string id);
    Task<int> CountAsync();

    /// <summary>
    /// Efficiently counts actions in a category using database-level COUNT
    /// </summary>
    /// <param name="category">Category to count</param>
    /// <returns>Number of actions in the category</returns>
    Task<int> CountByCategoryAsync(string category);

    /// <summary>
    /// Batch update category for all actions in a category (prevents N+1 queries)
    /// </summary>
    /// <param name="oldCategory">Current category name</param>
    /// <param name="newCategory">New category name (null or empty to clear)</param>
    /// <returns>Number of actions updated</returns>
    Task<int> UpdateCategoryForActionsAsync(string oldCategory, string? newCategory);

    /// <summary>
    /// Gets an action by its public ID (for GitOps sync)
    /// </summary>
    Task<ActionModel?> GetByPublicIdAsync(Guid publicId);

    /// <summary>
    /// Gets all actions with their associated command templates
    /// </summary>
    Task<IEnumerable<ActionModel>> GetAllWithTemplatesAsync();

    /// <summary>
    /// Adds multiple actions in a single batch operation (performance optimization)
    /// </summary>
    Task AddRangeAsync(IEnumerable<ActionModel> actions);

    /// <summary>
    /// Updates multiple actions in a single batch operation (performance optimization)
    /// </summary>
    Task UpdateRangeAsync(IEnumerable<ActionModel> actions);
}
