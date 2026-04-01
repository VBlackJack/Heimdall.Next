using TwinShell.Core.Enums;

namespace TwinShell.Core.Models;

/// <summary>
/// Represents an audit log entry for command execution
/// </summary>
public class AuditLog
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp when the event occurred (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User identifier (nullable for single-user mode)
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Action ID that was executed
    /// </summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>
    /// Command that was executed
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Platform where the command was executed
    /// </summary>
    public Platform Platform { get; set; }

    /// <summary>
    /// Exit code from execution
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Whether the execution was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Execution duration
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Action title (denormalized)
    /// </summary>
    public string ActionTitle { get; set; } = string.Empty;

    /// <summary>
    /// Category (denormalized)
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Whether this was a dangerous command
    /// </summary>
    public bool WasDangerous { get; set; }
}
