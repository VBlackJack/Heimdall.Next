namespace TwinShell.Core.Enums;

/// <summary>
/// Represents the platform on which a command can be executed
/// </summary>
public enum Platform
{
    /// <summary>
    /// Windows platform (PowerShell)
    /// </summary>
    Windows = 0,

    /// <summary>
    /// Linux platform (Bash)
    /// </summary>
    Linux = 1,

    /// <summary>
    /// Both Windows and Linux platforms
    /// </summary>
    Both = 2
}
