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

using TwinShell.Core.Interfaces;

namespace TwinShell.Core.Models;

/// <summary>
/// Represents a Git sync operation history entry
/// </summary>
public sealed class SyncHistoryEntry
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Type of sync operation (Pull, Push, FullSync, Initialize, TestConnection)
    /// </summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>
    /// Whether the operation succeeded
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error code if the operation failed
    /// </summary>
    public int ErrorCode { get; set; }

    /// <summary>
    /// Result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error details if the operation failed
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// Number of items created during import
    /// </summary>
    public int ItemsCreated { get; set; }

    /// <summary>
    /// Number of items updated during import
    /// </summary>
    public int ItemsUpdated { get; set; }

    /// <summary>
    /// Number of items exported during push
    /// </summary>
    public int ItemsExported { get; set; }

    /// <summary>
    /// Number of items skipped (conflicts)
    /// </summary>
    public int ItemsSkipped { get; set; }

    /// <summary>
    /// Number of conflicts detected
    /// </summary>
    public int ConflictsDetected { get; set; }

    /// <summary>
    /// Number of commits merged during pull
    /// </summary>
    public int CommitsMerged { get; set; }

    /// <summary>
    /// Duration of the operation
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Remote repository URL
    /// </summary>
    public string? RemoteUrl { get; set; }

    /// <summary>
    /// Branch name
    /// </summary>
    public string? Branch { get; set; }

    /// <summary>
    /// Timestamp when the operation started
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the operation completed
    /// </summary>
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Display-friendly relative time (e.g., "2 hours ago")
    /// </summary>
    public string RelativeTime
    {
        get
        {
            var diff = DateTime.UtcNow - StartedAt;

            if (diff.TotalMinutes < 1)
                return "Just now";
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes} minutes ago";
            if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours} hours ago";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays} days ago";

            return StartedAt.ToString("g");
        }
    }

    /// <summary>
    /// Creates a sync history entry from a Git operation result
    /// </summary>
    public static SyncHistoryEntry FromResult(
        GitOperationResult result,
        string operationType,
        DateTime startedAt,
        string? remoteUrl = null,
        string? branch = null)
    {
        return new SyncHistoryEntry
        {
            OperationType = operationType,
            Success = result.Success,
            ErrorCode = (int)result.ErrorCode,
            Message = result.Message,
            ErrorDetails = result.ErrorDetails,
            ItemsCreated = result.ItemsImported - result.ItemsUpdated,
            ItemsUpdated = result.ItemsUpdated,
            ItemsExported = result.ItemsExported,
            ItemsSkipped = result.ItemsSkipped,
            ConflictsDetected = result.ConflictsDetected,
            CommitsMerged = result.CommitsMerged,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            Duration = DateTime.UtcNow - startedAt,
            RemoteUrl = remoteUrl,
            Branch = branch
        };
    }
}

/// <summary>
/// Constants for sync operation types
/// </summary>
public static class SyncOperationType
{
    public const string Initialize = "Initialize";
    public const string Pull = "Pull";
    public const string Push = "Push";
    public const string FullSync = "FullSync";
    public const string TestConnection = "TestConnection";
}
