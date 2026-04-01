using TwinShell.Core.Enums;
using TwinShell.Core.Models;
using ActionModel = TwinShell.Core.Models.Action;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for managing actions
/// </summary>
public interface IActionService
{
    Task<IEnumerable<ActionModel>> GetAllActionsAsync();
    Task<ActionModel?> GetActionByIdAsync(string id);
    Task<ActionModel?> GetActionByPublicIdAsync(Guid publicId);
    Task<IEnumerable<ActionModel>> GetActionsByCategoryAsync(string category);
    Task<IEnumerable<string>> GetAllCategoriesAsync();
    Task<IEnumerable<ActionModel>> FilterActionsAsync(
        IEnumerable<ActionModel> actions,
        Platform? platform = null,
        CriticalityLevel? level = null);
    Task<ActionModel> CreateActionAsync(ActionModel action);
    Task UpdateActionAsync(ActionModel action);
    Task DeleteActionAsync(string id);

    /// <summary>
    /// Gets the count of actions for a specific category
    /// </summary>
    Task<int> GetActionCountByCategoryAsync(string category);

    /// <summary>
    /// Renames a category across all actions that use it
    /// </summary>
    Task<bool> RenameCategoryAsync(string oldName, string newName);

    /// <summary>
    /// Deletes a category by removing it from all actions (sets to empty string)
    /// </summary>
    Task<bool> DeleteCategoryAsync(string categoryName);
}
