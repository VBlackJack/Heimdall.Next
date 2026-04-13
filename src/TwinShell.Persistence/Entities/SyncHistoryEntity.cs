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

namespace TwinShell.Persistence.Entities;

/// <summary>
/// Database entity for tracking Git sync history
/// </summary>
public sealed class SyncHistoryEntity
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
    /// Duration of the operation in milliseconds
    /// </summary>
    public long DurationMs { get; set; }

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
}
