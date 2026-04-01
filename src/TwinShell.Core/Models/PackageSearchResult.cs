namespace TwinShell.Core.Models;

/// <summary>
/// Represents a package search result from Winget or Chocolatey
/// </summary>
public class PackageSearchResult
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PackageManager PackageManager { get; set; }
}

/// <summary>
/// Package manager type
/// </summary>
public enum PackageManager
{
    Winget,
    Chocolatey
}
