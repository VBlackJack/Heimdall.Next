using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;

namespace TwinShell.Infrastructure.Services;

/// <summary>
/// Service for managing package operations (Winget and Chocolatey)
/// </summary>
public class PackageManagerService : IPackageManagerService
{
    private const int DefaultTimeoutSeconds = 30;
    private static readonly Regex ValidSearchTermRegex = new(@"^[a-zA-Z0-9\s\-_.]+$", RegexOptions.Compiled);
    private readonly ILogger<PackageManagerService>? _logger;

    public PackageManagerService(ILogger<PackageManagerService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<PackageSearchResult>> SearchWingetPackagesAsync(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return Array.Empty<PackageSearchResult>();
        }

        // SECURITY: Validate searchTerm to prevent command injection
        if (!ValidateSearchTerm(searchTerm))
        {
            _logger?.LogWarning("Invalid search term rejected: {SearchTerm}", searchTerm);
            return Array.Empty<PackageSearchResult>();
        }

        var output = await ExecuteCommandAsync("winget", $"search \"{EscapeArgument(searchTerm)}\"");
        return ParseWingetSearchOutput(output);
    }

    public async Task<IEnumerable<PackageSearchResult>> SearchChocolateyPackagesAsync(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return Array.Empty<PackageSearchResult>();
        }

        // SECURITY: Validate searchTerm to prevent command injection
        if (!ValidateSearchTerm(searchTerm))
        {
            _logger?.LogWarning("Invalid search term rejected: {SearchTerm}", searchTerm);
            return Array.Empty<PackageSearchResult>();
        }

        var output = await ExecuteCommandAsync("choco", $"search \"{EscapeArgument(searchTerm)}\"");
        return ParseChocolateySearchOutput(output);
    }

    public async Task<PackageInfo?> GetWingetPackageInfoAsync(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        // SECURITY: Validate packageId to prevent command injection
        if (!ValidateSearchTerm(packageId))
        {
            _logger?.LogWarning("Invalid package ID rejected: {PackageId}", packageId);
            return null;
        }

        var output = await ExecuteCommandAsync("winget", $"show \"{EscapeArgument(packageId)}\"");
        return ParseWingetShowOutput(output, packageId);
    }

    public async Task<PackageInfo?> GetChocolateyPackageInfoAsync(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        // SECURITY: Validate packageId to prevent command injection
        if (!ValidateSearchTerm(packageId))
        {
            _logger?.LogWarning("Invalid package ID rejected: {PackageId}", packageId);
            return null;
        }

        var output = await ExecuteCommandAsync("choco", $"info \"{EscapeArgument(packageId)}\"");
        return ParseChocolateyInfoOutput(output, packageId);
    }

    public async Task<IEnumerable<PackageSearchResult>> ListWingetInstalledPackagesAsync()
    {
        var output = await ExecuteCommandAsync("winget", "list");
        return ParseWingetListOutput(output);
    }

    public async Task<IEnumerable<PackageSearchResult>> ListChocolateyInstalledPackagesAsync()
    {
        var output = await ExecuteCommandAsync("choco", "list --local-only");
        return ParseChocolateyListOutput(output);
    }

    public async Task<bool> IsWingetAvailableAsync()
    {
        try
        {
            var output = await ExecuteCommandAsync("winget", "--version", timeoutSeconds: 5);
            return !string.IsNullOrWhiteSpace(output);
        }
        catch (Exception)
        {
            // Winget not available or command failed
            return false;
        }
    }

    public async Task<bool> IsChocolateyAvailableAsync()
    {
        try
        {
            var output = await ExecuteCommandAsync("choco", "--version", timeoutSeconds: 5);
            return !string.IsNullOrWhiteSpace(output);
        }
        catch (Exception)
        {
            // Chocolatey not available or command failed
            return false;
        }
    }

    private async Task<string> ExecuteCommandAsync(string command, string arguments, int timeoutSeconds = DefaultTimeoutSeconds)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                process.Kill();
                throw new TimeoutException($"Command '{command} {arguments}' timed out after {timeoutSeconds} seconds");
            }

            var output = await outputTask;
            var error = await errorTask;

            return string.IsNullOrWhiteSpace(error) ? output : output + "\n" + error;
        }
        catch (Exception ex)
        {
            // SECURITY: Don't expose detailed error information
            _logger?.LogError(ex, "Failed to execute command");
            throw new InvalidOperationException("Command execution failed");
        }
    }

    /// <summary>
    /// Validates search term or package ID to prevent command injection
    /// </summary>
    private static bool ValidateSearchTerm(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return false;

        // Limit length to reasonable value
        if (term.Length > 200)
            return false;

        // Only allow alphanumeric, spaces, hyphens, underscores, and dots
        return ValidSearchTermRegex.IsMatch(term);
    }

    /// <summary>
    /// Escapes argument for command-line execution
    /// </summary>
    private static string EscapeArgument(string argument)
    {
        // Replace any potentially dangerous characters
        return argument.Replace("\"", "\\\"").Replace("$", "\\$");
    }

    private IEnumerable<PackageSearchResult> ParseWingetSearchOutput(string output)
    {
        var results = new List<PackageSearchResult>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return results;
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        bool inResultsSection = false;

        foreach (var line in lines)
        {
            // Skip header lines
            if (line.Contains("Name") && line.Contains("Id") && line.Contains("Version"))
            {
                inResultsSection = true;
                continue;
            }

            if (line.Contains("---") || !inResultsSection)
            {
                continue;
            }

            // Parse result line (format: Name  Id  Version  Source)
            var parts = Regex.Split(line.Trim(), @"\s{2,}");
            if (parts.Length >= 3)
            {
                results.Add(new PackageSearchResult
                {
                    Name = parts[0].Trim(),
                    Id = parts[1].Trim(),
                    Version = parts[2].Trim(),
                    Source = parts.Length > 3 ? parts[3].Trim() : "winget",
                    PackageManager = PackageManager.Winget
                });
            }
        }

        return results;
    }

    private IEnumerable<PackageSearchResult> ParseChocolateySearchOutput(string output)
    {
        var results = new List<PackageSearchResult>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return results;
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // Parse format: packagename version [Approved] Description
            var match = Regex.Match(line, @"^(\S+)\s+(\S+)");
            if (match.Success)
            {
                var name = match.Groups[1].Value;
                var version = match.Groups[2].Value;

                // Skip Chocolatey metadata lines
                if (name.Contains("packages found") || name.Contains("Chocolatey"))
                {
                    continue;
                }

                results.Add(new PackageSearchResult
                {
                    Id = name,
                    Name = name,
                    Version = version,
                    Source = "chocolatey",
                    PackageManager = PackageManager.Chocolatey
                });
            }
        }

        return results;
    }

    private IEnumerable<PackageSearchResult> ParseWingetListOutput(string output)
    {
        // Similar parsing to search output
        return ParseWingetSearchOutput(output);
    }

    private IEnumerable<PackageSearchResult> ParseChocolateyListOutput(string output)
    {
        // Similar parsing to search output
        return ParseChocolateySearchOutput(output);
    }

    private PackageInfo? ParseWingetShowOutput(string output, string packageId)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var info = new PackageInfo
        {
            Id = packageId,
            PackageManager = PackageManager.Winget
        };

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var key = parts[0].Trim().ToLowerInvariant();
            var value = parts[1].Trim();

            switch (key)
            {
                case "name":
                    info.Name = value;
                    break;
                case "version":
                    info.Version = value;
                    break;
                case "publisher":
                    info.Publisher = value;
                    break;
                case "author":
                    info.Author = value;
                    break;
                case "description":
                    info.Description = value;
                    break;
                case "homepage":
                    info.Homepage = value;
                    break;
                case "license":
                    info.License = value;
                    break;
                case "license url":
                    info.LicenseUrl = value;
                    break;
            }
        }

        return info;
    }

    private PackageInfo? ParseChocolateyInfoOutput(string output, string packageId)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var info = new PackageInfo
        {
            Id = packageId,
            Name = packageId,
            PackageManager = PackageManager.Chocolatey
        };

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Contains("Title:"))
            {
                info.Name = line.Split(':', 2)[1].Trim();
            }
            else if (line.Contains("Version:"))
            {
                info.Version = line.Split(':', 2)[1].Trim();
            }
            else if (line.Contains("Author:"))
            {
                info.Author = line.Split(':', 2)[1].Trim();
            }
            else if (line.Contains("Description:"))
            {
                info.Description = line.Split(':', 2)[1].Trim();
            }
        }

        return info;
    }
}
