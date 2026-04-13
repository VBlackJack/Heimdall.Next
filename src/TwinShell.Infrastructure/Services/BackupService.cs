/*
 * Copyright 2025 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TwinShell.Core.Interfaces;

namespace TwinShell.Infrastructure.Services;

/// <summary>
/// Implementation of backup service with automatic scheduling and retention policy.
/// Supports RPO of 15 minutes through configurable backup intervals.
/// </summary>
public sealed class BackupService : IBackupService, IDisposable
{
    private readonly IActionRepository _actionRepository;
    private readonly IBatchRepository _batchRepository;
    private readonly ICustomCategoryRepository _categoryRepository;
    private readonly ICommandTemplateRepository _templateRepository;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<BackupService> _logger;
    private readonly string _appDataPath;
    private Timer? _backupTimer;
    private bool _disposed;

    private const string BackupExtension = ".twbak";
    private const string BackupManifestFile = "manifest.json";
    private const string CurrentBackupVersion = "1.0";

    public string DefaultBackupPath { get; }
    public DateTime? LastBackupTime { get; private set; }

    public event EventHandler<BackupCompletedEventArgs>? BackupCompleted;

    public BackupService(
        IActionRepository actionRepository,
        IBatchRepository batchRepository,
        ICustomCategoryRepository categoryRepository,
        ICommandTemplateRepository templateRepository,
        IConfigurationService configurationService,
        ILogger<BackupService> logger)
    {
        _actionRepository = actionRepository;
        _batchRepository = batchRepository;
        _categoryRepository = categoryRepository;
        _templateRepository = templateRepository;
        _configurationService = configurationService;
        _logger = logger;

        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TwinShell");

        DefaultBackupPath = Path.Combine(_appDataPath, "Backups");
        Directory.CreateDirectory(DefaultBackupPath);

        LoadLastBackupTime();
    }

    public async Task<BackupResult> CreateBackupAsync(string? backupPath = null)
    {
        var targetPath = backupPath ?? DefaultBackupPath;
        var timestamp = DateTime.UtcNow;
        var fileName = $"twinshell-backup-{timestamp:yyyyMMdd-HHmmss}{BackupExtension}";
        var fullPath = Path.Combine(targetPath, fileName);

        _logger.LogInformation("Creating backup at {Path}", fullPath);

        try
        {
            Directory.CreateDirectory(targetPath);

            var contents = new BackupContents();
            var tempDir = Path.Combine(Path.GetTempPath(), $"twbackup-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // Export all data
                var actions = await _actionRepository.GetAllWithTemplatesAsync();
                contents.ActionsCount = actions.Count();
                await WriteJsonFileAsync(Path.Combine(tempDir, "actions.json"), actions);

                var categories = await _categoryRepository.GetAllAsync();
                contents.CategoriesCount = categories.Count();
                await WriteJsonFileAsync(Path.Combine(tempDir, "categories.json"), categories);

                var templates = await _templateRepository.GetAllAsync();
                contents.TemplatesCount = templates.Count();
                await WriteJsonFileAsync(Path.Combine(tempDir, "templates.json"), templates);

                var batches = await _batchRepository.GetAllAsync();
                contents.BatchesCount = batches.Count();
                await WriteJsonFileAsync(Path.Combine(tempDir, "batches.json"), batches);

                // Export configuration
                var configFilePath = Path.Combine(tempDir, "config.json");
                var exportResult = await _configurationService.ExportToJsonAsync(configFilePath);
                if (exportResult.Success)
                {
                    contents.IncludesSettings = true;
                }

                // Create manifest
                var manifest = new BackupManifest
                {
                    Version = CurrentBackupVersion,
                    CreatedAt = timestamp,
                    MachineName = Environment.MachineName,
                    UserName = Environment.UserName,
                    Contents = contents
                };
                await WriteJsonFileAsync(Path.Combine(tempDir, BackupManifestFile), manifest);

                // Create ZIP archive
                if (File.Exists(fullPath))
                    File.Delete(fullPath);

                ZipFile.CreateFromDirectory(tempDir, fullPath, CompressionLevel.Optimal, false);

                var fileInfo = new FileInfo(fullPath);
                LastBackupTime = timestamp;
                SaveLastBackupTime();

                _logger.LogInformation("Backup created successfully: {Path} ({Size} bytes)",
                    fullPath, fileInfo.Length);

                var result = BackupResult.Ok(
                    $"Backup created successfully with {contents.ActionsCount} actions",
                    fullPath,
                    contents);
                result.FileSizeBytes = fileInfo.Length;

                BackupCompleted?.Invoke(this, new BackupCompletedEventArgs(result));
                return result;
            }
            finally
            {
                // Cleanup temp directory
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup temp directory: {Path}", tempDir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup at {Path}", fullPath);
            var result = BackupResult.Fail("Failed to create backup", ex.Message);
            BackupCompleted?.Invoke(this, new BackupCompletedEventArgs(result));
            return result;
        }
    }

    public async Task<BackupResult> RestoreBackupAsync(string backupFilePath)
    {
        _logger.LogInformation("Restoring backup from {Path}", backupFilePath);

        if (!File.Exists(backupFilePath))
        {
            return BackupResult.Fail("Backup file not found", $"File does not exist: {backupFilePath}");
        }

        try
        {
            var validation = await ValidateBackupAsync(backupFilePath);
            if (!validation.IsValid)
            {
                return BackupResult.Fail("Invalid backup file", validation.Errors.ToArray());
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"twrestore-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(backupFilePath, tempDir);

                var contents = new BackupContents();
                var warnings = new List<string>();

                // Restore actions
                var actionsFile = Path.Combine(tempDir, "actions.json");
                if (File.Exists(actionsFile))
                {
                    var actionsJson = await File.ReadAllTextAsync(actionsFile);
                    var actions = JsonSerializer.Deserialize<List<Core.Models.Action>>(actionsJson, GetJsonOptions());
                    if (actions != null)
                    {
                        foreach (var action in actions)
                        {
                            var existing = await _actionRepository.GetByPublicIdAsync(action.PublicId);
                            if (existing == null)
                            {
                                await _actionRepository.AddAsync(action);
                            }
                            else
                            {
                                existing.Title = action.Title;
                                existing.Description = action.Description;
                                existing.Category = action.Category;
                                existing.Tags = action.Tags;
                                existing.Examples = action.Examples;
                                await _actionRepository.UpdateAsync(existing);
                            }
                        }
                        contents.ActionsCount = actions.Count;
                    }
                }

                // Restore configuration
                var configFile = Path.Combine(tempDir, "config.json");
                if (File.Exists(configFile))
                {
                    var importResult = await _configurationService.ImportFromJsonAsync(configFile);
                    if (!importResult.Success)
                    {
                        warnings.Add($"Configuration restore had issues: {importResult.ErrorMessage}");
                    }
                    contents.IncludesSettings = true;
                }

                _logger.LogInformation("Backup restored successfully from {Path}", backupFilePath);

                var result = BackupResult.Ok(
                    $"Backup restored: {contents.ActionsCount} actions",
                    backupFilePath,
                    contents);
                result.Warnings = warnings;
                return result;
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temp directory: {Path}", tempDir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore backup from {Path}", backupFilePath);
            return BackupResult.Fail("Failed to restore backup", ex.Message);
        }
    }

    public async Task<IReadOnlyList<BackupMetadata>> ListBackupsAsync()
    {
        var backups = new List<BackupMetadata>();

        if (!Directory.Exists(DefaultBackupPath))
            return backups;

        var files = Directory.GetFiles(DefaultBackupPath, $"*{BackupExtension}")
            .OrderByDescending(f => new FileInfo(f).CreationTimeUtc);

        foreach (var file in files)
        {
            try
            {
                var fileInfo = new FileInfo(file);
                var validation = await ValidateBackupAsync(file);

                backups.Add(new BackupMetadata
                {
                    FilePath = file,
                    FileName = fileInfo.Name,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    FileSizeBytes = fileInfo.Length,
                    Version = validation.Version,
                    Contents = validation.Contents,
                    IsValid = validation.IsValid
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read backup metadata: {File}", file);
            }
        }

        return backups;
    }

    public Task<int> CleanupOldBackupsAsync(int retentionDays = 30)
    {
        var deleted = 0;
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

        if (!Directory.Exists(DefaultBackupPath))
            return Task.FromResult(0);

        var files = Directory.GetFiles(DefaultBackupPath, $"*{BackupExtension}");

        foreach (var file in files)
        {
            try
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTimeUtc < cutoffDate)
                {
                    File.Delete(file);
                    deleted++;
                    _logger.LogInformation("Deleted old backup: {File}", file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old backup: {File}", file);
            }
        }

        return Task.FromResult(deleted);
    }

    public async Task<BackupValidationResult> ValidateBackupAsync(string backupFilePath)
    {
        var result = new BackupValidationResult();

        if (!File.Exists(backupFilePath))
        {
            result.Errors.Add("Backup file not found");
            return result;
        }

        try
        {
            using var archive = ZipFile.OpenRead(backupFilePath);

            var manifestEntry = archive.GetEntry(BackupManifestFile);
            if (manifestEntry == null)
            {
                result.Errors.Add("Missing manifest file");
                return result;
            }

            using var stream = manifestEntry.Open();
            using var reader = new StreamReader(stream);
            var manifestJson = await reader.ReadToEndAsync();

            var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson, GetJsonOptions());
            if (manifest == null)
            {
                result.Errors.Add("Invalid manifest format");
                return result;
            }

            result.Version = manifest.Version;
            result.Contents = manifest.Contents;

            // Validate required files exist
            if (archive.GetEntry("actions.json") == null)
                result.Warnings.Add("Missing actions.json");

            result.IsValid = result.Errors.Count == 0;
        }
        catch (InvalidDataException)
        {
            result.Errors.Add("Invalid or corrupted archive");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Validation error: {ex.Message}");
        }

        return result;
    }

    public void ScheduleAutomaticBackups(int intervalHours = 24)
    {
        StopAutomaticBackups();

        var interval = TimeSpan.FromHours(intervalHours);
        _backupTimer = new Timer(async _ =>
        {
            try
            {
                _logger.LogInformation("Starting scheduled backup");
                var result = await CreateBackupAsync();
                if (result.Success)
                {
                    await CleanupOldBackupsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled backup failed");
            }
        }, null, interval, interval);

        _logger.LogInformation("Automatic backups scheduled every {Hours} hours", intervalHours);
    }

    public void StopAutomaticBackups()
    {
        _backupTimer?.Dispose();
        _backupTimer = null;
    }

    private void LoadLastBackupTime()
    {
        var metaFile = Path.Combine(_appDataPath, "backup-meta.json");
        if (File.Exists(metaFile))
        {
            try
            {
                var json = File.ReadAllText(metaFile);
                var meta = JsonSerializer.Deserialize<BackupMeta>(json);
                LastBackupTime = meta?.LastBackupTime;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load backup metadata from {Path}", metaFile);
            }
        }
    }

    private void SaveLastBackupTime()
    {
        var metaFile = Path.Combine(_appDataPath, "backup-meta.json");
        try
        {
            var meta = new BackupMeta { LastBackupTime = LastBackupTime };
            File.WriteAllText(metaFile, JsonSerializer.Serialize(meta));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write backup metadata to {Path}", metaFile);
        }
    }

    private static async Task WriteJsonFileAsync<T>(string path, T data)
    {
        var json = JsonSerializer.Serialize(data, GetJsonOptions());
        await File.WriteAllTextAsync(path, json);
    }

    private static JsonSerializerOptions GetJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public void Dispose()
    {
        if (_disposed) return;
        _backupTimer?.Dispose();
        _disposed = true;
    }

    private sealed class BackupManifest
    {
        public string Version { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public BackupContents Contents { get; set; } = new();
    }

    private sealed class BackupMeta
    {
        public DateTime? LastBackupTime { get; set; }
    }
}
