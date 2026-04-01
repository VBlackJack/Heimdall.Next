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

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for managing application backups with automatic scheduling and retention.
/// Implements RPO (Recovery Point Objective) of 15 minutes for critical data.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a full backup of the application data.
    /// </summary>
    /// <param name="backupPath">Optional custom backup path. Uses default if null.</param>
    /// <returns>Result containing backup file path and metadata.</returns>
    Task<BackupResult> CreateBackupAsync(string? backupPath = null);

    /// <summary>
    /// Restores application data from a backup file.
    /// </summary>
    /// <param name="backupFilePath">Path to the backup file to restore.</param>
    /// <returns>Result of the restore operation.</returns>
    Task<BackupResult> RestoreBackupAsync(string backupFilePath);

    /// <summary>
    /// Lists all available backups in the backup directory.
    /// </summary>
    /// <returns>List of backup metadata.</returns>
    Task<IReadOnlyList<BackupMetadata>> ListBackupsAsync();

    /// <summary>
    /// Deletes old backups based on retention policy.
    /// Default retention: 7 daily backups, 4 weekly backups, 3 monthly backups.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain backups.</param>
    /// <returns>Number of backups deleted.</returns>
    Task<int> CleanupOldBackupsAsync(int retentionDays = 30);

    /// <summary>
    /// Validates a backup file integrity without restoring.
    /// </summary>
    /// <param name="backupFilePath">Path to the backup file.</param>
    /// <returns>Validation result with any issues found.</returns>
    Task<BackupValidationResult> ValidateBackupAsync(string backupFilePath);

    /// <summary>
    /// Schedules automatic backups at specified interval.
    /// </summary>
    /// <param name="intervalHours">Hours between automatic backups.</param>
    void ScheduleAutomaticBackups(int intervalHours = 24);

    /// <summary>
    /// Stops automatic backup scheduling.
    /// </summary>
    void StopAutomaticBackups();

    /// <summary>
    /// Gets the default backup directory path.
    /// </summary>
    string DefaultBackupPath { get; }

    /// <summary>
    /// Gets the last backup time, or null if no backup exists.
    /// </summary>
    DateTime? LastBackupTime { get; }

    /// <summary>
    /// Event raised when a backup operation completes.
    /// </summary>
    event EventHandler<BackupCompletedEventArgs>? BackupCompleted;
}

/// <summary>
/// Result of a backup or restore operation.
/// </summary>
public class BackupResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? BackupFilePath { get; set; }
    public DateTime Timestamp { get; set; }
    public long FileSizeBytes { get; set; }
    public BackupContents Contents { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static BackupResult Ok(string message, string backupFilePath, BackupContents contents)
        => new()
        {
            Success = true,
            Message = message,
            BackupFilePath = backupFilePath,
            Timestamp = DateTime.UtcNow,
            Contents = contents
        };

    public static BackupResult Fail(string message, params string[] errors)
        => new()
        {
            Success = false,
            Message = message,
            Timestamp = DateTime.UtcNow,
            Errors = errors.ToList()
        };
}

/// <summary>
/// Contents included in a backup.
/// </summary>
public class BackupContents
{
    public int ActionsCount { get; set; }
    public int CategoriesCount { get; set; }
    public int TemplatesCount { get; set; }
    public int BatchesCount { get; set; }
    public int HistoryEntriesCount { get; set; }
    public int FavoritesCount { get; set; }
    public bool IncludesSettings { get; set; }
    public bool IncludesAuditLog { get; set; }
}

/// <summary>
/// Metadata about an existing backup file.
/// </summary>
public class BackupMetadata
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long FileSizeBytes { get; set; }
    public string Version { get; set; } = string.Empty;
    public BackupContents? Contents { get; set; }
    public bool IsValid { get; set; }
}

/// <summary>
/// Result of backup validation.
/// </summary>
public class BackupValidationResult
{
    public bool IsValid { get; set; }
    public string Version { get; set; } = string.Empty;
    public BackupContents? Contents { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Event arguments for backup completed event.
/// </summary>
public class BackupCompletedEventArgs : EventArgs
{
    public BackupResult Result { get; }
    public bool WasScheduled { get; }

    public BackupCompletedEventArgs(BackupResult result, bool wasScheduled = false)
    {
        Result = result;
        WasScheduled = wasScheduled;
    }
}
