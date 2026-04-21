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

using System.IO;
using Heimdall.Core.Configuration;
using Heimdall.Core.Import;
using Heimdall.Core.Models;
using Heimdall.Core.Ssh;

namespace Heimdall.App.Services.Import;

/// <summary>
/// Computes import statuses and persists selected OpenSSH config candidates.
/// </summary>
public sealed class OpenSshConfigImporter(IConfigManager configManager)
{
    private readonly IConfigManager _configManager = configManager;

    /// <summary>
    /// Computes candidate import statuses against the current server inventory.
    /// </summary>
    public async Task<IReadOnlyList<OpenSshImportCandidateAssessment>> ComputeStatusesAsync(
        IReadOnlyList<OpenSshImportCandidate> candidates,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        ct.ThrowIfCancellationRequested();
        var existingProfiles = await _configManager.LoadServersAsync().ConfigureAwait(false);
        return ComputeStatuses(candidates, existingProfiles);
    }

    /// <summary>
    /// Computes candidate import statuses against the supplied server inventory.
    /// </summary>
    public IReadOnlyList<OpenSshImportCandidateAssessment> ComputeStatuses(
        IReadOnlyList<OpenSshImportCandidate> candidates,
        IEnumerable<ServerProfileDto> existingProfiles)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(existingProfiles);

        var existingAliases = existingProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.DisplayName))
            .Select(profile => profile.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidates
            .Select(candidate => new OpenSshImportCandidateAssessment(
                candidate,
                existingAliases.Contains(candidate.Alias)
                    ? ImportCandidateStatus.Duplicate
                    : ImportCandidateStatus.New))
            .ToList();
    }

    /// <summary>
    /// Imports the selected candidates without overwriting existing aliases.
    /// </summary>
    public async Task<ImportOutcome> ImportSelectedAsync(
        IReadOnlyList<OpenSshImportCandidate> selected,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selected);

        ct.ThrowIfCancellationRequested();
        var inventory = await _configManager.LoadServersAsync().ConfigureAwait(false);
        var existingAliases = inventory
            .Where(profile => !string.IsNullOrWhiteSpace(profile.DisplayName))
            .Select(profile => profile.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var importedCount = 0;
        var skippedDuplicates = 0;
        var warningCount = 0;

        foreach (var candidate in selected)
        {
            ct.ThrowIfCancellationRequested();

            if (!existingAliases.Add(candidate.Alias))
            {
                skippedDuplicates++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidate.IdentityFile) && !File.Exists(candidate.IdentityFile))
            {
                warningCount++;
                Heimdall.Core.Logging.FileLogger.Warn(
                    $"OpenSSH import preserved missing IdentityFile path for alias '{candidate.Alias}': {candidate.IdentityFile}");
            }

            inventory.Add(MapCandidate(candidate));
            importedCount++;
        }

        if (importedCount > 0)
        {
            await _configManager.SaveServersAsync(inventory).ConfigureAwait(false);
        }

        return new ImportOutcome(importedCount, skippedDuplicates, WarningCount: warningCount);
    }

    private static ServerProfileDto MapCandidate(OpenSshImportCandidate candidate)
    {
        return new ServerProfileDto
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = candidate.Alias,
            Origin = ProfileOrigin.ImportOpenSsh,
            RemoteServer = candidate.HostName,
            ConnectionType = "SSH",
            SshMode = "Embedded",
            SshPort = candidate.Port,
            SshUsername = string.IsNullOrWhiteSpace(candidate.User) ? null : candidate.User,
            SshKeyPath = string.IsNullOrWhiteSpace(candidate.IdentityFile) ? null : candidate.IdentityFile
        };
    }
}

/// <summary>
/// Associates an import candidate with its computed import status.
/// </summary>
public sealed record OpenSshImportCandidateAssessment(
    OpenSshImportCandidate Candidate,
    ImportCandidateStatus Status);
