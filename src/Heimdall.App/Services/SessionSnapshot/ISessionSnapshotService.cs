/*
 * Copyright 2026 Julien Bombled
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

namespace Heimdall.App.Services.SessionSnapshot;

/// <summary>
/// Persists and loads the top-level session snapshot used for restore-on-launch.
/// </summary>
public interface ISessionSnapshotService
{
    /// <summary>
    /// Absolute path to the snapshot JSON file.
    /// </summary>
    string SnapshotPath { get; }

    /// <summary>
    /// Saves a deterministic snapshot file for the supplied sessions.
    /// </summary>
    Task SaveAsync(IReadOnlyList<SessionSnapshotEntry> sessions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a snapshot file if present. Returns <c>null</c> on missing or unreadable files.
    /// Invalid entries are skipped.
    /// </summary>
    Task<SessionSnapshotFile?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the snapshot file if present. Idempotent.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
