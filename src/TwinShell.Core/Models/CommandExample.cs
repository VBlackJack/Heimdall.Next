using TwinShell.Core.Enums;

namespace TwinShell.Core.Models;

/// <summary>
/// Represents a concrete example of a command
/// </summary>
public class CommandExample
{
    /// <summary>
    /// The actual command to execute
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this example demonstrates
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Target platform for this example (Windows, Linux, or Both for generic examples)
    /// </summary>
    public Platform Platform { get; set; } = Platform.Both;
}
