using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TwinShell.Core.Constants;
using TwinShell.Core.Enums;
using TwinShell.Core.Helpers;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;

namespace TwinShell.Core.Services;

/// <summary>
/// Service for configuration export/import
/// </summary>
public sealed class ConfigurationService : IConfigurationService
{
    private readonly IFavoritesRepository _favoritesRepository;
    private readonly ICommandHistoryRepository _historyRepository;
    private readonly IActionRepository _actionRepository;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly string _baseExportDirectory;

    private static JsonSerializerOptions JsonOptions => JsonOptionsHelper.Default;

    public ConfigurationService(
        IFavoritesRepository favoritesRepository,
        ICommandHistoryRepository historyRepository,
        IActionRepository actionRepository,
        ILogger<ConfigurationService> logger)
    {
        _favoritesRepository = favoritesRepository;
        _historyRepository = historyRepository;
        _actionRepository = actionRepository;
        _logger = logger;

        // Configure base directory for exports (sandbox all file operations)
        _baseExportDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TwinShell",
            "Exports");

        // Ensure base directory exists
        if (!Directory.Exists(_baseExportDirectory))
        {
            Directory.CreateDirectory(_baseExportDirectory);
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> ExportToJsonAsync(
        string filePath,
        string? userId = null,
        bool includeHistory = true)
    {
        try
        {
            // SECURITY: Validate file path (allow user-chosen paths, but validate)
            if (!PathValidator.IsExportPathValid(filePath))
            {
                return (false, "Invalid file path");
            }

            // SECURITY: Validate file extension
            if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Invalid file extension: file must be a .json file");
            }

            var config = new UserConfigurationDto
            {
                Version = "1.0",
                ExportDate = DateTime.UtcNow,
                UserId = userId
            };

            // Export favorites
            var favorites = await _favoritesRepository.GetAllAsync(userId).ConfigureAwait(false);
            config.Favorites = favorites.Select(f => new FavoriteDto
            {
                ActionId = f.ActionId,
                CreatedAt = f.CreatedAt,
                DisplayOrder = f.DisplayOrder
            }).ToList();

            // Export history if requested
            if (includeHistory)
            {
                var history = await _historyRepository.GetRecentAsync(ValidationConstants.DefaultHistoryLoadCount).ConfigureAwait(false);
                config.History = history.Select(h => new CommandHistoryDto
                {
                    ActionId = h.ActionId,
                    GeneratedCommand = h.GeneratedCommand,
                    Parameters = h.Parameters,
                    Platform = h.Platform.ToString(),
                    CreatedAt = h.CreatedAt,
                    Category = h.Category,
                    ActionTitle = h.ActionTitle
                }).ToList();
            }

            // Add default preferences (can be extended later)
            config.Preferences = new Dictionary<string, string>
            {
                { "HistoryRetentionDays", "90" },
                { "MaxFavorites", "50" }
            };

            // Write to file
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export operation failed for path: {FilePath}", filePath);
            return (false, "Export operation failed");
        }
    }

    public async Task<(bool Success, string? ErrorMessage, int FavoritesImported, int HistoryImported)> ImportFromJsonAsync(
        string filePath,
        string? userId = null,
        bool mergeMode = true)
    {
        try
        {
            // SECURITY: Validate file path
            if (!PathValidator.IsImportPathValid(filePath))
            {
                return (false, "Invalid file path", 0, 0);
            }

            // Validate file exists
            if (!File.Exists(filePath))
            {
                return (false, "File not found", 0, 0);
            }

            // SECURITY: Validate file size (max 10MB)
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > ValidationConstants.MaxFileSizeBytes)
            {
                return (false, "File too large: maximum size is 10MB", 0, 0);
            }

            // Read and parse JSON
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

            // SECURITY: Validate JSON schema before deserialization
            if (!ValidateJsonSchema(json))
            {
                return (false, "Invalid JSON structure", 0, 0);
            }

            var config = JsonSerializer.Deserialize<UserConfigurationDto>(json, JsonOptions);

            if (config == null)
            {
                return (false, "Invalid JSON format", 0, 0);
            }

            // Validate version
            if (!string.IsNullOrEmpty(config.Version) && config.Version != "1.0")
            {
                // Future versions might need migration logic
                return (false, $"Unsupported configuration version: {config.Version}. This application supports version 1.0.", 0, 0);
            }

            int favoritesImported = 0;
            int historyImported = 0;

            // PERFORMANCE: Load all valid action IDs once to avoid N+1 queries
            var allActions = await _actionRepository.GetAllAsync().ConfigureAwait(false);
            var validActionIds = allActions.Select(a => a.Id).ToHashSet();

            // Get existing favorites if in merge mode
            HashSet<string> existingFavoriteActionIds = new();
            if (mergeMode)
            {
                var existingFavorites = await _favoritesRepository.GetAllAsync(userId).ConfigureAwait(false);
                existingFavoriteActionIds = existingFavorites.Select(f => f.ActionId).ToHashSet();
            }

            // PERFORMANCE: Get initial count once instead of in loop
            var currentFavoritesCount = await _favoritesRepository.GetCountAsync(userId).ConfigureAwait(false);

            // Import favorites
            // PERFORMANCE FIX: Collect all favorites and batch insert instead of N+1 queries
            var favoritesToAdd = new List<UserFavorite>();

            foreach (var favoriteDto in config.Favorites)
            {
                // Skip if already exists in merge mode
                if (mergeMode && existingFavoriteActionIds.Contains(favoriteDto.ActionId))
                {
                    continue;
                }

                // PERFORMANCE: Check existence in memory instead of database query
                if (!validActionIds.Contains(favoriteDto.ActionId))
                {
                    continue; // Skip invalid action IDs
                }

                // PERFORMANCE: Check limit with local counter
                if (currentFavoritesCount >= 50)
                {
                    break; // Stop if limit reached
                }

                var favorite = new UserFavorite
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = userId,
                    ActionId = favoriteDto.ActionId,
                    CreatedAt = favoriteDto.CreatedAt,
                    DisplayOrder = favoriteDto.DisplayOrder
                };

                favoritesToAdd.Add(favorite);
                favoritesImported++;
                currentFavoritesCount++; // Increment local counter
            }

            // PERFORMANCE FIX: Batch insert all favorites at once
            if (favoritesToAdd.Any())
            {
                await _favoritesRepository.AddRangeAsync(favoritesToAdd).ConfigureAwait(false);
            }

            // Import history
            // PERFORMANCE FIX: Collect all history items and batch insert instead of N+1 queries
            var historyToAdd = new List<CommandHistory>();

            foreach (var historyDto in config.History)
            {
                // PERFORMANCE: Check existence in memory instead of database query
                if (!validActionIds.Contains(historyDto.ActionId))
                {
                    continue; // Skip invalid action IDs
                }

                // Parse platform
                if (!Enum.TryParse<Platform>(historyDto.Platform, out var platform))
                {
                    platform = Platform.Both;
                }

                var history = new CommandHistory
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = userId,
                    ActionId = historyDto.ActionId,
                    GeneratedCommand = historyDto.GeneratedCommand,
                    Parameters = historyDto.Parameters,
                    Platform = platform,
                    CreatedAt = historyDto.CreatedAt,
                    Category = historyDto.Category,
                    ActionTitle = historyDto.ActionTitle
                };

                historyToAdd.Add(history);
                historyImported++;
            }

            // PERFORMANCE FIX: Batch insert all history at once
            if (historyToAdd.Any())
            {
                await _historyRepository.AddRangeAsync(historyToAdd).ConfigureAwait(false);
            }

            return (true, null, favoritesImported, historyImported);
        }
        catch (JsonException ex)
        {
            // SECURITY: Don't expose exception details to users
            _logger.LogError(ex, "JSON parsing error during import from: {FilePath}", filePath);
            return (false, "Invalid JSON format", 0, 0);
        }
        catch (Exception ex)
        {
            // SECURITY: Don't expose exception details to users
            _logger.LogError(ex, "Import failed from: {FilePath}", filePath);
            return (false, "Import failed", 0, 0);
        }
    }

    public async Task<(bool IsValid, string? ErrorMessage, string? Version)> ValidateConfigurationFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return (false, "File not found", null);
            }

            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var config = JsonSerializer.Deserialize<UserConfigurationDto>(json, JsonOptions);

            if (config == null)
            {
                return (false, "Invalid JSON format", null);
            }

            // Basic validation
            if (string.IsNullOrEmpty(config.Version))
            {
                return (false, "Missing version information", null);
            }

            if (config.Favorites == null)
            {
                return (false, "Missing favorites data", config.Version);
            }

            if (config.History == null)
            {
                return (false, "Missing history data", config.Version);
            }

            return (true, null, config.Version);
        }
        catch (JsonException ex)
        {
            // SECURITY FIX: Don't expose exception details that could leak path information
            _logger.LogWarning(ex, "JSON parsing error during validation of: {FilePath}", filePath);
            return (false, "JSON parsing error", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for: {FilePath}", filePath);
            return (false, "Validation error", null);
        }
    }

    /// <summary>
    /// Validates JSON structure before deserialization (prevents malicious JSON)
    /// </summary>
    private bool ValidateJsonSchema(string json)
    {
        try
        {
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;

                // Verify root is an object
                if (root.ValueKind != JsonValueKind.Object)
                    return false;

                // Verify required properties exist
                if (!root.TryGetProperty("Version", out _))
                    return false;

                if (!root.TryGetProperty("Favorites", out var favorites))
                    return false;

                if (favorites.ValueKind != JsonValueKind.Array)
                    return false;

                if (!root.TryGetProperty("History", out var history))
                    return false;

                if (history.ValueKind != JsonValueKind.Array)
                    return false;

                // Validate array lengths (prevent DoS via huge arrays)
                if (favorites.GetArrayLength() > ValidationConstants.MaxFavoritesInImport)
                    return false;

                if (history.GetArrayLength() > ValidationConstants.MaxHistoryInImport)
                    return false;

                return true;
            }
        }
        catch (JsonException)
        {
            // Invalid JSON structure
            return false;
        }
    }
}
