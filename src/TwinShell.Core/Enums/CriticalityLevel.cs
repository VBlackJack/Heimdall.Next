namespace TwinShell.Core.Enums;

/// <summary>
/// Represents the criticality level of an action
/// </summary>
public enum CriticalityLevel
{
    /// <summary>
    /// Informational command (read-only, no system changes)
    /// </summary>
    Info = 0,

    /// <summary>
    /// Execution command (may modify system state)
    /// </summary>
    Run = 1,

    /// <summary>
    /// Dangerous command (can cause significant system changes or data loss)
    /// </summary>
    Dangerous = 2
}
