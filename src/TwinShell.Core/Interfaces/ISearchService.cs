using TwinShell.Core.Models;
using ActionModel = TwinShell.Core.Models.Action;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for searching actions
/// </summary>
public interface ISearchService
{
    Task<IEnumerable<ActionModel>> SearchAsync(
        IEnumerable<ActionModel> actions,
        string searchTerm);
}
