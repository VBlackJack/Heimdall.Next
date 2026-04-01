using TwinShell.Core.Enums;

namespace TwinShell.Core.Models;

/// <summary>
/// Represents a single command within a batch
/// </summary>
public class BatchCommandItem
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Parent batch ID
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// Order within the batch (0-based)
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Action ID (reference to the Action being executed)
    /// </summary>
    public string? ActionId { get; set; }

    /// <summary>
    /// Action title (denormalized for display)
    /// </summary>
    public string ActionTitle { get; set; } = string.Empty;

    /// <summary>
    /// The actual command to execute
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Platform for this command
    /// </summary>
    public Platform Platform { get; set; }

    /// <summary>
    /// Optional description for this command in the batch
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this command has been executed
    /// </summary>
    public bool IsExecuted { get; set; }

    /// <summary>
    /// Execution result for this command (null if not yet executed)
    /// </summary>
    public ExecutionResult? ExecutionResult { get; set; }
}
