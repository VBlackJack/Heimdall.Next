using TwinShell.Core.Enums;

namespace TwinShell.Persistence.Entities;

/// <summary>
/// Database entity for CommandHistory
/// </summary>
public class CommandHistoryEntity
{
    public string Id { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public ActionEntity? Action { get; set; }
    public string GeneratedCommand { get; set; } = string.Empty;

    /// <summary>
    /// Parameters stored as JSON
    /// </summary>
    public string ParametersJson { get; set; } = "{}";

    public Platform Platform { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Category { get; set; } = string.Empty;
    public string ActionTitle { get; set; } = string.Empty;

    // Sprint 4: Execution tracking fields
    public bool IsExecuted { get; set; }
    public int? ExitCode { get; set; }
    public long? ExecutionDurationTicks { get; set; } // Store as ticks for EF Core compatibility
    public bool? ExecutionSuccess { get; set; }
}
