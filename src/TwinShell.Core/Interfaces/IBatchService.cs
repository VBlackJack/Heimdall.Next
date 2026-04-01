using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for managing command batches
/// </summary>
public interface IBatchService
{
    /// <summary>
    /// Gets all batches
    /// </summary>
    /// <returns>List of all batches</returns>
    Task<IEnumerable<CommandBatch>> GetAllBatchesAsync();

    /// <summary>
    /// Gets a batch by ID
    /// </summary>
    /// <param name="id">Batch ID</param>
    /// <returns>The batch, or null if not found</returns>
    Task<CommandBatch?> GetBatchByIdAsync(string id);

    /// <summary>
    /// Creates a new batch
    /// </summary>
    /// <param name="batch">The batch to create</param>
    /// <returns>The created batch with ID</returns>
    Task<CommandBatch> CreateBatchAsync(CommandBatch batch);

    /// <summary>
    /// Updates an existing batch
    /// </summary>
    /// <param name="batch">The batch to update</param>
    Task UpdateBatchAsync(CommandBatch batch);

    /// <summary>
    /// Deletes a batch
    /// </summary>
    /// <param name="id">Batch ID</param>
    Task DeleteBatchAsync(string id);

    /// <summary>
    /// Searches batches by name or description
    /// </summary>
    /// <param name="query">Search query</param>
    /// <returns>Matching batches</returns>
    Task<IEnumerable<CommandBatch>> SearchBatchesAsync(string query);

    /// <summary>
    /// Exports a batch to JSON
    /// </summary>
    /// <param name="batch">The batch to export</param>
    /// <returns>JSON string representation</returns>
    string ExportBatchToJson(CommandBatch batch);

    /// <summary>
    /// Imports a batch from JSON
    /// </summary>
    /// <param name="json">JSON string</param>
    /// <returns>The imported batch</returns>
    CommandBatch ImportBatchFromJson(string json);
}
