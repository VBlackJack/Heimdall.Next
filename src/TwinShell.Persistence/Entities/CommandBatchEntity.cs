using TwinShell.Core.Enums;

namespace TwinShell.Persistence.Entities;

/// <summary>
/// Database entity for CommandBatch
/// </summary>
public class CommandBatchEntity
{
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Universal unique identifier for GitOps synchronization.
    /// Used as the stable identifier across different environments.
    /// </summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public BatchExecutionMode ExecutionMode { get; set; }

    /// <summary>
    /// Commands stored as JSON array
    /// </summary>
    public string CommandsJson { get; set; } = "[]";

    /// <summary>
    /// Tags stored as JSON array
    /// </summary>
    public string TagsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastExecutedAt { get; set; }
    public bool IsUserCreated { get; set; }
}
