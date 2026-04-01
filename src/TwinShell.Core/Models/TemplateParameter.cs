namespace TwinShell.Core.Models;

/// <summary>
/// Represents a parameter in a command template
/// </summary>
public class TemplateParameter
{
    /// <summary>
    /// Parameter name (used in template substitution, e.g., {targetHost})
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Display label for the UI
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Parameter type (string, int, bool, etc.)
    /// </summary>
    public string Type { get; set; } = "string";

    /// <summary>
    /// Default value for the parameter
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Whether this parameter is required
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Description/help text for the parameter
    /// </summary>
    public string? Description { get; set; }
}
