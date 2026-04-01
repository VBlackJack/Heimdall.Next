namespace TwinShell.Core.Models;

/// <summary>
/// Represents a PowerShell module from the PowerShell Gallery
/// </summary>
public class PowerShellModule
{
    /// <summary>
    /// Module name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Module version
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Module description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Module author
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Company name
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// Download count
    /// </summary>
    public long DownloadCount { get; set; }

    /// <summary>
    /// Published date
    /// </summary>
    public DateTime PublishedDate { get; set; }

    /// <summary>
    /// Tags
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Project URI
    /// </summary>
    public string? ProjectUri { get; set; }

    /// <summary>
    /// License URI
    /// </summary>
    public string? LicenseUri { get; set; }

    /// <summary>
    /// Whether the module is installed locally
    /// </summary>
    public bool IsInstalled { get; set; }
}
