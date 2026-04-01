namespace TwinShell.Core.Models;

/// <summary>
/// Represents an external documentation link
/// </summary>
public class ExternalLink
{
    /// <summary>
    /// Display title for the link
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// URL of the external resource
    /// </summary>
    public string Url { get; set; } = string.Empty;
}
