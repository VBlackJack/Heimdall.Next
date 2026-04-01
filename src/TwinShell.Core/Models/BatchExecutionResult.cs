namespace TwinShell.Core.Models;

/// <summary>
/// Represents the result of executing a batch of commands
/// </summary>
public class BatchExecutionResult
{
    /// <summary>
    /// The batch that was executed
    /// </summary>
    public CommandBatch Batch { get; set; } = new();

    /// <summary>
    /// Overall success (true if all commands succeeded)
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of commands executed
    /// </summary>
    public int ExecutedCount { get; set; }

    /// <summary>
    /// Number of commands that succeeded
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of commands that failed
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Number of commands that were skipped (due to StopOnError)
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Total execution duration
    /// </summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>
    /// Timestamp when batch execution started
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when batch execution completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Whether the batch execution was cancelled by user
    /// </summary>
    public bool WasCancelled { get; set; }

    /// <summary>
    /// Error message if batch execution failed to start
    /// </summary>
    public string? ErrorMessage { get; set; }
}
