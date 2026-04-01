namespace TwinShell.Core.Models;

/// <summary>
/// Represents the result of a command execution
/// </summary>
public class ExecutionResult
{
    /// <summary>
    /// Whether the execution completed successfully (ExitCode == 0)
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Process exit code (0 = success, non-zero = error)
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Standard output stream content
    /// </summary>
    public string Stdout { get; set; } = string.Empty;

    /// <summary>
    /// Standard error stream content
    /// </summary>
    public string Stderr { get; set; } = string.Empty;

    /// <summary>
    /// Total execution duration
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Timestamp when execution started
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Error message if execution failed to start or was cancelled
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether the execution was cancelled by user
    /// </summary>
    public bool WasCancelled { get; set; }

    /// <summary>
    /// Whether the execution timed out
    /// </summary>
    public bool TimedOut { get; set; }
}
