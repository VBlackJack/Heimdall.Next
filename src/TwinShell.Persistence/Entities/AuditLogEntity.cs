using TwinShell.Core.Enums;

namespace TwinShell.Persistence.Entities;

/// <summary>
/// Database entity for AuditLog
/// </summary>
public class AuditLogEntity
{
    public string Id { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? UserId { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public Platform Platform { get; set; }
    public int ExitCode { get; set; }
    public bool Success { get; set; }
    public long DurationTicks { get; set; }  // TimeSpan stored as ticks
    public string ActionTitle { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool WasDangerous { get; set; }
}
