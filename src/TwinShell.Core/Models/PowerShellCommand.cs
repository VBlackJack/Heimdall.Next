namespace TwinShell.Core.Models;

/// <summary>
/// Represents a PowerShell cmdlet or function
/// </summary>
public class PowerShellCommand
{
    /// <summary>
    /// Command name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Module name the command belongs to
    /// </summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>
    /// Command type (Cmdlet, Function, Alias, etc.)
    /// </summary>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// Synopsis from Get-Help
    /// </summary>
    public string Synopsis { get; set; } = string.Empty;

    /// <summary>
    /// Description from Get-Help
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Syntax examples
    /// </summary>
    public List<string> Syntax { get; set; } = new();

    /// <summary>
    /// Parameters
    /// </summary>
    public List<PowerShellParameter> Parameters { get; set; } = new();

    /// <summary>
    /// Examples from Get-Help
    /// </summary>
    public List<string> Examples { get; set; } = new();
}

/// <summary>
/// Represents a PowerShell command parameter
/// </summary>
public class PowerShellParameter
{
    /// <summary>
    /// Parameter name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parameter type
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Whether the parameter is mandatory
    /// </summary>
    public bool IsMandatory { get; set; }

    /// <summary>
    /// Parameter description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Default value if any
    /// </summary>
    public string? DefaultValue { get; set; }
}
