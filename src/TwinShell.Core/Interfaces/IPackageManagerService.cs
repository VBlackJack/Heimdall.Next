using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for managing package operations (Winget and Chocolatey)
/// </summary>
public interface IPackageManagerService
{
    /// <summary>
    /// Search for packages using Winget
    /// </summary>
    /// <param name="searchTerm">The search term</param>
    /// <returns>List of package search results</returns>
    Task<IEnumerable<PackageSearchResult>> SearchWingetPackagesAsync(string searchTerm);

    /// <summary>
    /// Search for packages using Chocolatey
    /// </summary>
    /// <param name="searchTerm">The search term</param>
    /// <returns>List of package search results</returns>
    Task<IEnumerable<PackageSearchResult>> SearchChocolateyPackagesAsync(string searchTerm);

    /// <summary>
    /// Get detailed information about a Winget package
    /// </summary>
    /// <param name="packageId">The package ID</param>
    /// <returns>Detailed package information</returns>
    Task<PackageInfo?> GetWingetPackageInfoAsync(string packageId);

    /// <summary>
    /// Get detailed information about a Chocolatey package
    /// </summary>
    /// <param name="packageId">The package ID</param>
    /// <returns>Detailed package information</returns>
    Task<PackageInfo?> GetChocolateyPackageInfoAsync(string packageId);

    /// <summary>
    /// List all installed Winget packages
    /// </summary>
    /// <returns>List of installed packages</returns>
    Task<IEnumerable<PackageSearchResult>> ListWingetInstalledPackagesAsync();

    /// <summary>
    /// List all installed Chocolatey packages
    /// </summary>
    /// <returns>List of installed packages</returns>
    Task<IEnumerable<PackageSearchResult>> ListChocolateyInstalledPackagesAsync();

    /// <summary>
    /// Check if Winget is available on the system
    /// </summary>
    /// <returns>True if Winget is available</returns>
    Task<bool> IsWingetAvailableAsync();

    /// <summary>
    /// Check if Chocolatey is available on the system
    /// </summary>
    /// <returns>True if Chocolatey is available</returns>
    Task<bool> IsChocolateyAvailableAsync();
}
