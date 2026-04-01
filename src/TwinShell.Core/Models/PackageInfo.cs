namespace TwinShell.Core.Models;

/// <summary>
/// Detailed information about a package
/// </summary>
public class PackageInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Homepage { get; set; }
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }
    public string Source { get; set; } = string.Empty;
    public PackageManager PackageManager { get; set; }
    public DateTime? PublishedDate { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IsInstalled { get; set; }
}
