using System.IO;
using System.Text.Json;
using TwinShell.Core.Constants;
using TwinShell.Core.Enums;
using TwinShell.Core.Helpers;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;
using ActionModel = TwinShell.Core.Models.Action;

namespace TwinShell.Infrastructure.Services;

/// <summary>
/// Service for importing and exporting actions to/from JSON files
/// </summary>
public sealed class ImportExportService : IImportExportService
{
    private readonly IActionRepository _actionRepository;

    public ImportExportService(IActionRepository actionRepository)
    {
        _actionRepository = actionRepository;
    }

    public async Task<ExportResult> ExportActionsAsync(string filePath)
    {
        try
        {
            // Get all actions from database
            var actions = (await _actionRepository.GetAllAsync()).ToList();

            // Create export data structure compatible with initial-actions.json format
            var exportData = new
            {
                SchemaVersion = "1.0",
                ExportDate = DateTime.UtcNow,
                TotalActions = actions.Count,
                Actions = actions.Select(a => new
                {
                    a.Id,
                    a.Title,
                    a.Description,
                    a.Category,
                    Platform = (int)a.Platform,
                    Level = (int)a.Level,
                    a.Tags,
                    // BUGFIX: Use ID field directly instead of navigation property to prevent data loss
                    a.WindowsCommandTemplateId,
                    WindowsCommandTemplate = a.WindowsCommandTemplate != null ? new
                    {
                        a.WindowsCommandTemplate.Id,
                        Platform = (int)a.WindowsCommandTemplate.Platform,
                        a.WindowsCommandTemplate.Name,
                        a.WindowsCommandTemplate.CommandPattern,
                        Parameters = a.WindowsCommandTemplate.Parameters.Select(p => new
                        {
                            p.Name,
                            p.Label,
                            p.Type,
                            p.DefaultValue,
                            p.Required,
                            p.Description
                        })
                    } : null,
                    // BUGFIX: Use ID field directly instead of navigation property to prevent data loss
                    a.LinuxCommandTemplateId,
                    LinuxCommandTemplate = a.LinuxCommandTemplate != null ? new
                    {
                        a.LinuxCommandTemplate.Id,
                        Platform = (int)a.LinuxCommandTemplate.Platform,
                        a.LinuxCommandTemplate.Name,
                        a.LinuxCommandTemplate.CommandPattern,
                        Parameters = a.LinuxCommandTemplate.Parameters.Select(p => new
                        {
                            p.Name,
                            p.Label,
                            p.Type,
                            p.DefaultValue,
                            p.Required,
                            p.Description
                        })
                    } : null,
                    a.Examples,
                    a.Notes,
                    a.Links,
                    a.IsUserCreated,
                    a.CreatedAt,
                    a.UpdatedAt
                }).ToList()
            };

            // Serialize to JSON with formatting
            var json = JsonSerializer.Serialize(exportData, JsonOptionsHelper.CamelCaseForExport);

            // Write to file
            await File.WriteAllTextAsync(filePath, json);

            return new ExportResult
            {
                Success = true,
                ActionCount = actions.Count
            };
        }
        catch (Exception ex)
        {
            return new ExportResult
            {
                Success = false,
                ActionCount = 0,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<ExportResult> ExportSingleActionAsync(string filePath, ActionModel action)
    {
        try
        {
            // Create export data structure for a single action
            var exportData = new
            {
                id = action.Id,
                title = action.Title,
                description = action.Description,
                category = action.Category,
                platform = action.Platform.ToString().ToLowerInvariant(),
                level = action.Level.ToString().ToLowerInvariant(),
                tags = action.Tags ?? new List<string>(),
                windowsCommandTemplateId = action.WindowsCommandTemplateId,
                windowsCommandTemplate = action.WindowsCommandTemplate != null ? new
                {
                    id = action.WindowsCommandTemplate.Id,
                    platform = action.WindowsCommandTemplate.Platform.ToString().ToLowerInvariant(),
                    name = action.WindowsCommandTemplate.Name,
                    commandPattern = action.WindowsCommandTemplate.CommandPattern,
                    parameters = action.WindowsCommandTemplate.Parameters.Select(p => new
                    {
                        name = p.Name,
                        label = p.Label,
                        type = p.Type,
                        defaultValue = p.DefaultValue,
                        required = p.Required,
                        description = p.Description
                    })
                } : null,
                linuxCommandTemplateId = action.LinuxCommandTemplateId,
                linuxCommandTemplate = action.LinuxCommandTemplate != null ? new
                {
                    id = action.LinuxCommandTemplate.Id,
                    platform = action.LinuxCommandTemplate.Platform.ToString().ToLowerInvariant(),
                    name = action.LinuxCommandTemplate.Name,
                    commandPattern = action.LinuxCommandTemplate.CommandPattern,
                    parameters = action.LinuxCommandTemplate.Parameters.Select(p => new
                    {
                        name = p.Name,
                        label = p.Label,
                        type = p.Type,
                        defaultValue = p.DefaultValue,
                        required = p.Required,
                        description = p.Description
                    })
                } : null,
                windowsExamples = action.Examples?.Where(e => e.Platform == Platform.Windows)
                    .Select(e => new { command = e.Command, description = e.Description }) ?? Enumerable.Empty<object>(),
                linuxExamples = action.Examples?.Where(e => e.Platform == Platform.Linux)
                    .Select(e => new { command = e.Command, description = e.Description }) ?? Enumerable.Empty<object>(),
                notes = action.Notes,
                links = action.Links?.Select(l => new { url = l.Url, title = l.Title }) ?? Enumerable.Empty<object>(),
                isUserCreated = action.IsUserCreated
            };

            // Serialize to JSON with formatting
            var json = JsonSerializer.Serialize(exportData, JsonOptionsHelper.IndentedIgnoreNull);

            // Write to file
            await File.WriteAllTextAsync(filePath, json);

            return new ExportResult
            {
                Success = true,
                ActionCount = 1
            };
        }
        catch (Exception ex)
        {
            return new ExportResult
            {
                Success = false,
                ActionCount = 0,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<ImportResult> ImportSingleActionAsync(string filePath)
    {
        try
        {
            // SECURITY: Validate file size
            var fileInfo = new FileInfo(filePath);
            const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB for single action
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                return new ImportResult
                {
                    Success = false,
                    ErrorMessage = $"File too large. Maximum size is {MaxFileSizeBytes / 1024} KB."
                };
            }

            // Read and parse JSON with security limits
            var json = await File.ReadAllTextAsync(filePath);
            var actionData = JsonSerializer.Deserialize<SingleActionImportData>(json, JsonOptionsHelper.SecureImport);

            if (actionData == null || string.IsNullOrWhiteSpace(actionData.Id) || string.IsNullOrWhiteSpace(actionData.Title))
            {
                return new ImportResult
                {
                    Success = false,
                    ErrorMessage = "Invalid file format: missing required fields (id, title)."
                };
            }

            // Check if action already exists
            var existingAction = await _actionRepository.GetByIdAsync(actionData.Id);

            // Map to Action model
            var action = MapSingleActionImportToAction(actionData);

            if (existingAction != null)
            {
                // Update existing action
                action.CreatedAt = existingAction.CreatedAt;
                action.UpdatedAt = DateTime.UtcNow;
                await _actionRepository.UpdateAsync(action);

                return new ImportResult
                {
                    Success = true,
                    Updated = 1,
                    Message = $"Action '{action.Title}' updated successfully."
                };
            }
            else
            {
                // Add new action
                action.CreatedAt = DateTime.UtcNow;
                action.UpdatedAt = DateTime.UtcNow;
                action.IsUserCreated = true;
                await _actionRepository.AddAsync(action);

                return new ImportResult
                {
                    Success = true,
                    Imported = 1,
                    Message = $"Action '{action.Title}' imported successfully."
                };
            }
        }
        catch (JsonException ex)
        {
            return new ImportResult
            {
                Success = false,
                ErrorMessage = $"Invalid JSON format: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new ImportResult
            {
                Success = false,
                ErrorMessage = $"Import failed: {ex.Message}"
            };
        }
    }

    public async Task<ImportResult> ImportActionsAsync(string filePath, ImportMode mode)
    {
        try
        {
            // SECURITY: Validate file size to prevent DoS
            var fileInfo = new FileInfo(filePath);
            const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                return new ImportResult
                {
                    Success = false,
                    ErrorMessage = $"File too large. Maximum size is {MaxFileSizeBytes / 1024 / 1024} MB."
                };
            }

            // Read and parse JSON with security limits
            var json = await File.ReadAllTextAsync(filePath);
            var importData = JsonSerializer.Deserialize<ImportData>(json, JsonOptionsHelper.SecureImport);

            if (importData?.Actions == null || importData.Actions.Count == 0)
            {
                return new ImportResult
                {
                    Success = false,
                    ErrorMessage = "Invalid file format or no actions found."
                };
            }

            int imported = 0, updated = 0, skipped = 0;

            // Replace mode: delete all user-created actions first
            if (mode == ImportMode.Replace)
            {
                var existingActions = await _actionRepository.GetAllAsync();
                foreach (var action in existingActions.Where(a => a.IsUserCreated))
                {
                    await _actionRepository.DeleteAsync(action.Id);
                }
            }

            // Import actions
            foreach (var actionData in importData.Actions)
            {
                // SECURITY: Validate action data
                if (!ValidateActionData(actionData))
                {
                    skipped++;
                    continue;
                }

                var existingAction = await _actionRepository.GetByIdAsync(actionData.Id);

                if (existingAction != null)
                {
                    // Action exists
                    if (mode == ImportMode.Merge)
                    {
                        // Only update if it's a user-created action
                        if (existingAction.IsUserCreated)
                        {
                            MapToAction(actionData, existingAction);
                            existingAction.UpdatedAt = DateTime.UtcNow;
                            await _actionRepository.UpdateAsync(existingAction);
                            updated++;
                        }
                        else
                        {
                            // SECURITY: Skip system actions to prevent overwriting
                            skipped++;
                        }
                    }
                    else
                    {
                        // Replace mode: action should have been deleted above if user-created
                        // If it still exists, it's a system action - skip
                        skipped++;
                    }
                }
                else
                {
                    // New action - import it
                    var newAction = MapToAction(actionData);
                    // BUGFIX: Preserve IsUserCreated from import data instead of forcing to true
                    // This allows re-importing system actions correctly
                    newAction.IsUserCreated = actionData.IsUserCreated;
                    newAction.CreatedAt = DateTime.UtcNow;
                    newAction.UpdatedAt = DateTime.UtcNow;
                    await _actionRepository.AddAsync(newAction);
                    imported++;
                }
            }

            return new ImportResult
            {
                Success = true,
                Imported = imported,
                Updated = updated,
                Skipped = skipped,
                Message = $"{imported} imported, {updated} updated, {skipped} skipped."
            };
        }
        catch (JsonException ex)
        {
            return new ImportResult
            {
                Success = false,
                ErrorMessage = $"Invalid JSON format: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new ImportResult
            {
                Success = false,
                ErrorMessage = $"Import failed: {ex.Message}"
            };
        }
    }

    public async Task<ValidationResult> ValidateImportFileAsync(string filePath)
    {
        try
        {
            // Check file exists
            if (!File.Exists(filePath))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "File not found."
                };
            }

            // Check file size
            var fileInfo = new FileInfo(filePath);
            const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"File too large. Maximum size is {MaxFileSizeBytes / 1024 / 1024} MB."
                };
            }

            // Parse JSON with security limits
            var json = await File.ReadAllTextAsync(filePath);
            var importData = JsonSerializer.Deserialize<ImportData>(json, JsonOptionsHelper.SecureImport);

            if (importData?.Actions == null)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Invalid file format: missing 'actions' array."
                };
            }

            return new ValidationResult
            {
                IsValid = true,
                SchemaVersion = importData.SchemaVersion ?? "1.0",
                ActionCount = importData.Actions.Count
            };
        }
        catch (JsonException ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Invalid JSON: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Validation failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// SECURITY: Validates action data to prevent injection of malicious content
    /// Uses centralized validation constants for consistency
    /// </summary>
    private static bool ValidateActionData(ActionModel action)
    {
        // Check required fields
        if (string.IsNullOrWhiteSpace(action.Title) ||
            string.IsNullOrWhiteSpace(action.Category))
        {
            return false;
        }

        // SECURITY: Check field lengths to prevent oversized data
        // BUGFIX: Use centralized constants for validation consistency
        if (action.Title.Length > ValidationConstants.MaxActionTitleLength ||
            action.Category.Length > ValidationConstants.MaxActionCategoryLength ||
            (action.Description?.Length ?? 0) > ValidationConstants.MaxActionDescriptionLength ||
            (action.Notes?.Length ?? 0) > ValidationConstants.MaxActionNotesLength)
        {
            return false;
        }

        // SECURITY: Check collections sizes
        if ((action.Tags?.Count ?? 0) > ValidationConstants.MaxActionTagsCount ||
            (action.Examples?.Count ?? 0) > ValidationConstants.MaxActionExamplesCount ||
            (action.Links?.Count ?? 0) > ValidationConstants.MaxActionLinksCount)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Maps import data to an Action model (new instance)
    /// </summary>
    private static ActionModel MapToAction(ActionModel source)
    {
        var action = new ActionModel
        {
            Id = source.Id,
            Title = source.Title,
            Description = source.Description ?? string.Empty,
            Category = source.Category,
            Platform = source.Platform,
            Level = source.Level,
            Tags = source.Tags ?? new List<string>(),
            Notes = source.Notes,
            Examples = source.Examples ?? new List<CommandExample>(),
            Links = source.Links ?? new List<ExternalLink>()
        };

        // Map Windows command template
        if (source.WindowsCommandTemplate != null)
        {
            action.WindowsCommandTemplateId = source.WindowsCommandTemplate.Id;
            action.WindowsCommandTemplate = new CommandTemplate
            {
                Id = source.WindowsCommandTemplate.Id,
                Platform = source.WindowsCommandTemplate.Platform,
                Name = source.WindowsCommandTemplate.Name,
                CommandPattern = source.WindowsCommandTemplate.CommandPattern,
                Parameters = source.WindowsCommandTemplate.Parameters?.Select(p => new TemplateParameter
                {
                    Name = p.Name,
                    Label = p.Label,
                    Type = p.Type,
                    DefaultValue = p.DefaultValue,
                    Required = p.Required,
                    Description = p.Description
                }).ToList() ?? new List<TemplateParameter>()
            };
        }

        // Map Linux command template
        if (source.LinuxCommandTemplate != null)
        {
            action.LinuxCommandTemplateId = source.LinuxCommandTemplate.Id;
            action.LinuxCommandTemplate = new CommandTemplate
            {
                Id = source.LinuxCommandTemplate.Id,
                Platform = source.LinuxCommandTemplate.Platform,
                Name = source.LinuxCommandTemplate.Name,
                CommandPattern = source.LinuxCommandTemplate.CommandPattern,
                Parameters = source.LinuxCommandTemplate.Parameters?.Select(p => new TemplateParameter
                {
                    Name = p.Name,
                    Label = p.Label,
                    Type = p.Type,
                    DefaultValue = p.DefaultValue,
                    Required = p.Required,
                    Description = p.Description
                }).ToList() ?? new List<TemplateParameter>()
            };
        }

        return action;
    }

    /// <summary>
    /// Maps import data to an existing Action model (update in place)
    /// </summary>
    private static void MapToAction(ActionModel source, ActionModel destination)
    {
        destination.Title = source.Title;
        destination.Description = source.Description ?? string.Empty;
        destination.Category = source.Category;
        destination.Platform = source.Platform;
        destination.Level = source.Level;
        destination.Tags = source.Tags ?? new List<string>();
        destination.Notes = source.Notes;
        destination.Examples = source.Examples ?? new List<CommandExample>();
        destination.Links = source.Links ?? new List<ExternalLink>();

        // Update Windows command template
        if (source.WindowsCommandTemplate != null)
        {
            destination.WindowsCommandTemplateId = source.WindowsCommandTemplate.Id;
            destination.WindowsCommandTemplate = new CommandTemplate
            {
                Id = source.WindowsCommandTemplate.Id,
                Platform = source.WindowsCommandTemplate.Platform,
                Name = source.WindowsCommandTemplate.Name,
                CommandPattern = source.WindowsCommandTemplate.CommandPattern,
                Parameters = source.WindowsCommandTemplate.Parameters?.Select(p => new TemplateParameter
                {
                    Name = p.Name,
                    Label = p.Label,
                    Type = p.Type,
                    DefaultValue = p.DefaultValue,
                    Required = p.Required,
                    Description = p.Description
                }).ToList() ?? new List<TemplateParameter>()
            };
        }
        else
        {
            destination.WindowsCommandTemplateId = null;
            destination.WindowsCommandTemplate = null;
        }

        // Update Linux command template
        if (source.LinuxCommandTemplate != null)
        {
            destination.LinuxCommandTemplateId = source.LinuxCommandTemplate.Id;
            destination.LinuxCommandTemplate = new CommandTemplate
            {
                Id = source.LinuxCommandTemplate.Id,
                Platform = source.LinuxCommandTemplate.Platform,
                Name = source.LinuxCommandTemplate.Name,
                CommandPattern = source.LinuxCommandTemplate.CommandPattern,
                Parameters = source.LinuxCommandTemplate.Parameters?.Select(p => new TemplateParameter
                {
                    Name = p.Name,
                    Label = p.Label,
                    Type = p.Type,
                    DefaultValue = p.DefaultValue,
                    Required = p.Required,
                    Description = p.Description
                }).ToList() ?? new List<TemplateParameter>()
            };
        }
        else
        {
            destination.LinuxCommandTemplateId = null;
            destination.LinuxCommandTemplate = null;
        }
    }

    /// <summary>
    /// Data structure for JSON import (compatible with initial-actions.json)
    /// </summary>
    private sealed class ImportData
    {
        public string? SchemaVersion { get; set; }
        public List<ActionModel> Actions { get; set; } = new();
    }

    /// <summary>
    /// Data structure for single action JSON import (compatible with data/seed/actions/*.json format)
    /// </summary>
    private sealed class SingleActionImportData
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Platform { get; set; } = "windows";
        public string Level { get; set; } = "standard";
        public List<string>? Tags { get; set; }
        public string? WindowsCommandTemplateId { get; set; }
        public CommandTemplateData? WindowsCommandTemplate { get; set; }
        public string? LinuxCommandTemplateId { get; set; }
        public CommandTemplateData? LinuxCommandTemplate { get; set; }
        public List<ExampleData>? WindowsExamples { get; set; }
        public List<ExampleData>? LinuxExamples { get; set; }
        public string? Notes { get; set; }
        public List<LinkData>? Links { get; set; }
        public bool IsUserCreated { get; set; }
    }

    private sealed class CommandTemplateData
    {
        public string Id { get; set; } = string.Empty;
        public string? Platform { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CommandPattern { get; set; } = string.Empty;
        public List<ParameterData>? Parameters { get; set; }
    }

    private sealed class ParameterData
    {
        public string Name { get; set; } = string.Empty;
        public string? Label { get; set; }
        public string? Type { get; set; }
        public string? DefaultValue { get; set; }
        public bool Required { get; set; }
        public string? Description { get; set; }
    }

    private sealed class ExampleData
    {
        public string Command { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    private sealed class LinkData
    {
        public string Url { get; set; } = string.Empty;
        public string? Title { get; set; }
    }

    /// <summary>
    /// Maps single action import data to an Action model
    /// </summary>
    private static ActionModel MapSingleActionImportToAction(SingleActionImportData source)
    {
        var action = new ActionModel
        {
            Id = source.Id,
            Title = source.Title,
            Description = source.Description ?? string.Empty,
            Category = source.Category,
            Platform = ParsePlatform(source.Platform),
            Level = ParseLevel(source.Level),
            Tags = source.Tags ?? new List<string>(),
            Notes = source.Notes,
            IsUserCreated = source.IsUserCreated
        };

        // Combine Windows and Linux examples
        var examples = new List<CommandExample>();
        if (source.WindowsExamples != null)
        {
            examples.AddRange(source.WindowsExamples.Select(e => new CommandExample
            {
                Command = e.Command,
                Description = e.Description ?? string.Empty,
                Platform = Platform.Windows
            }));
        }
        if (source.LinuxExamples != null)
        {
            examples.AddRange(source.LinuxExamples.Select(e => new CommandExample
            {
                Command = e.Command,
                Description = e.Description ?? string.Empty,
                Platform = Platform.Linux
            }));
        }
        action.Examples = examples;

        // Map links
        if (source.Links != null)
        {
            action.Links = source.Links.Select(l => new ExternalLink
            {
                Url = l.Url,
                Title = l.Title ?? string.Empty
            }).ToList();
        }
        else
        {
            action.Links = new List<ExternalLink>();
        }

        // Map Windows command template
        if (source.WindowsCommandTemplate != null)
        {
            action.WindowsCommandTemplateId = source.WindowsCommandTemplateId ?? source.WindowsCommandTemplate.Id;
            action.WindowsCommandTemplate = new CommandTemplate
            {
                Id = source.WindowsCommandTemplate.Id,
                Platform = Platform.Windows,
                Name = source.WindowsCommandTemplate.Name,
                CommandPattern = source.WindowsCommandTemplate.CommandPattern,
                Parameters = source.WindowsCommandTemplate.Parameters?.Select(p => new TemplateParameter
                {
                    Name = p.Name,
                    Label = p.Label ?? p.Name,
                    Type = p.Type ?? "string",
                    DefaultValue = p.DefaultValue,
                    Required = p.Required,
                    Description = p.Description
                }).ToList() ?? new List<TemplateParameter>()
            };
        }

        // Map Linux command template
        if (source.LinuxCommandTemplate != null)
        {
            action.LinuxCommandTemplateId = source.LinuxCommandTemplateId ?? source.LinuxCommandTemplate.Id;
            action.LinuxCommandTemplate = new CommandTemplate
            {
                Id = source.LinuxCommandTemplate.Id,
                Platform = Platform.Linux,
                Name = source.LinuxCommandTemplate.Name,
                CommandPattern = source.LinuxCommandTemplate.CommandPattern,
                Parameters = source.LinuxCommandTemplate.Parameters?.Select(p => new TemplateParameter
                {
                    Name = p.Name,
                    Label = p.Label ?? p.Name,
                    Type = p.Type ?? "string",
                    DefaultValue = p.DefaultValue,
                    Required = p.Required,
                    Description = p.Description
                }).ToList() ?? new List<TemplateParameter>()
            };
        }

        return action;
    }

    private static Platform ParsePlatform(string? platform)
    {
        return platform?.ToLowerInvariant() switch
        {
            "windows" => Platform.Windows,
            "linux" => Platform.Linux,
            "both" => Platform.Both,
            _ => Platform.Windows
        };
    }

    private static CriticalityLevel ParseLevel(string? level)
    {
        return level?.ToLowerInvariant() switch
        {
            "info" => CriticalityLevel.Info,
            "safe" => CriticalityLevel.Info,
            "run" => CriticalityLevel.Run,
            "standard" => CriticalityLevel.Run,
            "dangerous" => CriticalityLevel.Dangerous,
            "caution" => CriticalityLevel.Dangerous,
            "critical" => CriticalityLevel.Dangerous,
            _ => CriticalityLevel.Run
        };
    }
}
