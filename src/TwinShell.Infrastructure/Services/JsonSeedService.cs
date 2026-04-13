using System.IO;
using System.Text.Json;
using TwinShell.Core.Helpers;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;
using ActionModel = TwinShell.Core.Models.Action;

namespace TwinShell.Infrastructure.Services;

/// <summary>
/// Service for seeding initial data from individual JSON action files.
/// Each action is stored in its own file in data/seed/actions/*.json
/// </summary>
public sealed class JsonSeedService : ISeedService
{
    private readonly IActionRepository _actionRepository;
    private readonly string _actionsDir;

    private static JsonSerializerOptions JsonOptions => JsonOptionsHelper.CaseInsensitive;

    public JsonSeedService(IActionRepository actionRepository, string seedBasePath)
    {
        _actionRepository = actionRepository;
        _actionsDir = Path.Combine(Path.GetDirectoryName(seedBasePath) ?? seedBasePath, "actions");
    }

    public async Task SeedAsync()
    {
        // Check if database is empty
        var existingCount = await _actionRepository.CountAsync().ConfigureAwait(false);
        if (existingCount > 0)
        {
            return;
        }

        if (!Directory.Exists(_actionsDir))
        {
            Console.WriteLine($"Error: Actions directory not found: {_actionsDir}");
            return;
        }

        var jsonFiles = Directory.GetFiles(_actionsDir, "*.json")
            .Where(f => !Path.GetFileName(f).StartsWith("_"))
            .ToArray();

        if (jsonFiles.Length == 0)
        {
            Console.WriteLine($"Warning: No action files found in {_actionsDir}");
            return;
        }

        // Load and validate all actions
        var validActionsCount = 0;
        var skippedActionsCount = 0;
        var errorFiles = new List<string>();

        foreach (var filePath in jsonFiles.OrderBy(f => f))
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

                // Security: Limit file size (100KB per action file)
                const int MaxFileSizeBytes = 100 * 1024;
                if (json.Length > MaxFileSizeBytes)
                {
                    Console.WriteLine($"Warning: File {Path.GetFileName(filePath)} too large, skipping.");
                    errorFiles.Add(Path.GetFileName(filePath));
                    continue;
                }

                var action = JsonSerializer.Deserialize<ActionModel>(json, JsonOptions);
                if (action == null)
                {
                    errorFiles.Add(Path.GetFileName(filePath));
                    continue;
                }

                if (!ValidateAction(action))
                {
                    Console.WriteLine($"Warning: Invalid action in {Path.GetFileName(filePath)}: {action.Id ?? "unknown"}");
                    skippedActionsCount++;
                    continue;
                }

                action.IsUserCreated = false;
                action.CreatedAt = DateTime.UtcNow;
                action.UpdatedAt = DateTime.UtcNow;

                await _actionRepository.AddAsync(action).ConfigureAwait(false);
                validActionsCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading {Path.GetFileName(filePath)}: {ex.Message}");
                errorFiles.Add(Path.GetFileName(filePath));
            }
        }

        Console.WriteLine($"Seeding completed: {validActionsCount} actions from {jsonFiles.Length} files.");
        if (skippedActionsCount > 0)
        {
            Console.WriteLine($"  Skipped: {skippedActionsCount} invalid actions");
        }
        if (errorFiles.Count > 0)
        {
            Console.WriteLine($"  Errors: {errorFiles.Count} files failed to load");
        }
    }

    /// <summary>
    /// Validates action data to prevent injection of malicious content
    /// </summary>
    private static bool ValidateAction(ActionModel action)
    {
        // Check required fields
        if (string.IsNullOrWhiteSpace(action.Title) ||
            string.IsNullOrWhiteSpace(action.Category))
        {
            return false;
        }

        // Check field lengths to prevent oversized data
        const int MaxTitleLength = 200;
        const int MaxDescriptionLength = 2000;
        const int MaxCategoryLength = 100;
        const int MaxNotesLength = 5000;

        if (action.Title.Length > MaxTitleLength ||
            action.Category.Length > MaxCategoryLength ||
            (action.Description?.Length ?? 0) > MaxDescriptionLength ||
            (action.Notes?.Length ?? 0) > MaxNotesLength)
        {
            return false;
        }

        // Check collections sizes
        const int MaxTagsCount = 20;
        const int MaxExamplesCount = 50;
        const int MaxLinksCount = 10;

        if ((action.Tags?.Count ?? 0) > MaxTagsCount ||
            (action.Examples?.Count ?? 0) > MaxExamplesCount ||
            (action.WindowsExamples?.Count ?? 0) > MaxExamplesCount ||
            (action.LinuxExamples?.Count ?? 0) > MaxExamplesCount ||
            (action.Links?.Count ?? 0) > MaxLinksCount)
        {
            return false;
        }

        return true;
    }
}
