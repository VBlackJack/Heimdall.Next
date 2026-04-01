namespace TwinShell.Core.Models;

/// <summary>
/// Represents a single line of output from a command execution
/// </summary>
public class OutputLine
{
    /// <summary>
    /// The output text
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Whether this line is from stderr (vs stdout)
    /// </summary>
    public bool IsError { get; set; }

    /// <summary>
    /// Timestamp when the line was received
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
