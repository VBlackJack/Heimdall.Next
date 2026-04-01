using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Repository for batch persistence
/// </summary>
public interface IBatchRepository
{
    /// <summary>
    /// Gets all batches
    /// </summary>
    Task<IEnumerable<CommandBatch>> GetAllAsync();

    /// <summary>
    /// Gets a batch by ID
    /// </summary>
    Task<CommandBatch?> GetByIdAsync(string id);

    /// <summary>
    /// Adds a new batch
    /// </summary>
    Task AddAsync(CommandBatch batch);

    /// <summary>
    /// Updates an existing batch
    /// </summary>
    Task UpdateAsync(CommandBatch batch);

    /// <summary>
    /// Deletes a batch
    /// </summary>
    Task DeleteAsync(string id);

    /// <summary>
    /// Searches batches by name or description
    /// </summary>
    Task<IEnumerable<CommandBatch>> SearchAsync(string query);

    /// <summary>
    /// Gets a batch by its public ID (for GitOps sync)
    /// </summary>
    Task<CommandBatch?> GetByPublicIdAsync(Guid publicId);
}
