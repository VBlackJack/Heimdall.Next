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
using System.Text;
using Heimdall.Core.Configuration;
using Heimdall.Core.Ssh;
using Heimdall.Ssh;

namespace Heimdall.App.Services.Import;

/// <summary>
/// Reads, classifies, and imports trusted SSH host fingerprints from known_hosts.
/// </summary>
public sealed class KnownHostsImporter(IConfigManager config, HostKeyStore hostKeyStore)
{
    private readonly IConfigManager _config = config;
    private readonly HostKeyStore _hostKeyStore = hostKeyStore;

    public async Task<KnownHostsParseResult> ParseFileAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct).ConfigureAwait(false);
        return KnownHostsParser.Parse(content);
    }

    public async Task<KnownHostsImportPreview> BuildPreviewAsync(
        KnownHostsParseResult parseResult,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        ct.ThrowIfCancellationRequested();
        var settings = await _config.LoadSettingsAsync().ConfigureAwait(false);
        var diagnostics = parseResult.Diagnostics.ToList();
        var rows = new List<KnownHostsPreviewRow>();

        var indexedCandidates = parseResult.Entries
            .Select((entry, index) => new IndexedCandidate(
                index,
                new KnownHostsImportCandidate
                {
                    Host = entry.Host,
                    Port = entry.Port,
                    Fingerprint = HostKeyFormats.ComputeSha256Fingerprint(entry.Base64Key),
                    SourceLineNumber = entry.SourceLineNumber
                }))
            .ToList();

        foreach (var group in indexedCandidates.GroupBy(
                     candidate => HostKeyFormats.MakeKey(candidate.Candidate.Host, candidate.Candidate.Port),
                     StringComparer.Ordinal))
        {
            var orderedGroup = group.OrderBy(candidate => candidate.Index).ToList();
            var distinctFingerprints = orderedGroup
                .Select(candidate => candidate.Candidate.Fingerprint)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (distinctFingerprints.Count == 1)
            {
                var primary = orderedGroup[0].Candidate;
                foreach (var duplicate in orderedGroup.Skip(1))
                {
                    diagnostics.Add(new KnownHostsImportDiagnostic(
                        KnownHostsDiagnosticLevel.Info,
                        duplicate.Candidate.SourceLineNumber,
                        KnownHostsDiagnosticCode.DuplicateFingerprintInSourceMerged));
                }

                var hostPortKey = HostKeyFormats.MakeKey(primary.Host, primary.Port);
                if (settings.TrustedHostKeys.TryGetValue(hostPortKey, out var storedFingerprint))
                {
                    rows.Add(new KnownHostsPreviewRow(
                        primary,
                        string.Equals(storedFingerprint, primary.Fingerprint, StringComparison.Ordinal)
                            ? KnownHostsCandidateStatus.Existing
                            : KnownHostsCandidateStatus.Conflict,
                        storedFingerprint));
                }
                else
                {
                    rows.Add(new KnownHostsPreviewRow(primary, KnownHostsCandidateStatus.New, null));
                }

                continue;
            }

            diagnostics.Add(new KnownHostsImportDiagnostic(
                KnownHostsDiagnosticLevel.Warning,
                orderedGroup[0].Candidate.SourceLineNumber,
                KnownHostsDiagnosticCode.IntraFileFingerprintConflict,
                $"{distinctFingerprints.Count} distinct fingerprints for {group.Key}"));

            foreach (var item in orderedGroup)
            {
                rows.Add(new KnownHostsPreviewRow(
                    item.Candidate,
                    KnownHostsCandidateStatus.Conflict,
                    null));
            }
        }

        return new KnownHostsImportPreview(rows, diagnostics);
    }

    public async Task<KnownHostsImportOutcome> ImportSelectedAsync(
        IReadOnlyList<KnownHostsImportCandidate> selected,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selected);

        ct.ThrowIfCancellationRequested();
        var settings = await _config.LoadSettingsAsync().ConfigureAwait(false);
        var selectedGroups = selected
            .GroupBy(candidate => HostKeyFormats.MakeKey(candidate.Host, candidate.Port), StringComparer.Ordinal)
            .ToList();

        var toPersist = new List<(string Key, KnownHostsImportCandidate Candidate)>();
        var skippedExisting = 0;
        var skippedConflict = 0;

        foreach (var group in selectedGroups)
        {
            var candidates = group.ToList();
            var distinctFingerprints = candidates
                .Select(candidate => candidate.Fingerprint)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (distinctFingerprints.Count > 1)
            {
                skippedConflict += candidates.Count;
                continue;
            }

            var candidate = candidates[0];
            if (settings.TrustedHostKeys.TryGetValue(group.Key, out var storedFingerprint))
            {
                if (string.Equals(storedFingerprint, candidate.Fingerprint, StringComparison.Ordinal))
                {
                    skippedExisting++;
                }
                else
                {
                    skippedConflict++;
                }

                continue;
            }

            toPersist.Add((group.Key, candidate));
        }

        var imported = 0;
        if (toPersist.Count > 0)
        {
            imported = await _config.MergeTrustedHostKeysAsync(
                    toPersist.Select(item => new KeyValuePair<string, string>(item.Key, item.Candidate.Fingerprint)))
                .ConfigureAwait(false);

            var refreshedSettings = await _config.LoadSettingsAsync().ConfigureAwait(false);
            foreach (var item in toPersist)
            {
                if (refreshedSettings.TrustedHostKeys.TryGetValue(item.Key, out var storedFingerprint))
                {
                    if (string.Equals(storedFingerprint, item.Candidate.Fingerprint, StringComparison.Ordinal))
                    {
                        _hostKeyStore.Trust(item.Candidate.Host, item.Candidate.Port, item.Candidate.Fingerprint);
                    }
                    else
                    {
                        skippedConflict++;
                    }
                }
            }
        }

        return new KnownHostsImportOutcome(imported, skippedExisting, skippedConflict);
    }

    private sealed record IndexedCandidate(int Index, KnownHostsImportCandidate Candidate);
}

public enum KnownHostsCandidateStatus
{
    New,
    Existing,
    Conflict
}

public sealed record KnownHostsPreviewRow(
    KnownHostsImportCandidate Candidate,
    KnownHostsCandidateStatus Status,
    string? ExistingFingerprint);

public sealed record KnownHostsImportPreview(
    IReadOnlyList<KnownHostsPreviewRow> Rows,
    IReadOnlyList<KnownHostsImportDiagnostic> Diagnostics);

public sealed record KnownHostsImportOutcome(
    int Imported,
    int SkippedExisting,
    int SkippedConflict);
