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

using System.Collections.Concurrent;
using Heimdall.Core.Ssh;

namespace Heimdall.Ssh;

/// <summary>
/// Trust-On-First-Use host key verification store.
/// Stores host key entries per host:port, blocks on mismatch.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </summary>
public sealed class HostKeyStore
{
    private const string UnknownAlgorithm = "unknown";
    private readonly ConcurrentDictionary<string, HostKeyEntry> _trustedKeys = new();

    /// <summary>
    /// Raised whenever a host key is explicitly trusted.
    /// Parameters: host:port key, fingerprint, whether it was trusted.
    /// </summary>
    public event Action<string, string, bool>? HostKeyEvent;

    /// <summary>
    /// Verify a host key received from the SSH.NET HostKeyReceived event.
    /// Returns trusted on first use (TOFU) or fingerprint match.
    /// Returns untrusted on fingerprint mismatch.
    /// </summary>
    /// <param name="host">Remote hostname or IP.</param>
    /// <param name="port">Remote port.</param>
    /// <param name="hostKey">Raw host key bytes from the server.</param>
    /// <returns>Verification result indicating trust status.</returns>
    public HostKeyVerifyResult Verify(string host, int port, byte[] hostKey)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(hostKey);

        var key = HostKeyFormats.MakeKey(host, port);
        var fingerprint = ComputeFingerprint(hostKey);

        if (_trustedKeys.TryGetValue(key, out var stored))
        {
            var match = string.Equals(stored.Fingerprint, fingerprint, StringComparison.Ordinal);
            return new HostKeyVerifyResult(match, FirstUse: false, fingerprint, stored.Fingerprint);
        }

        // First use: trusted by TOFU policy
        return new HostKeyVerifyResult(Trusted: true, FirstUse: true, fingerprint, StoredFingerprint: null);
    }

    /// <summary>
    /// Populate the store from persisted gateway/server configurations.
    /// Called at startup before any connections are made.
    /// </summary>
    /// <param name="entries">Tuples of (host, port, fingerprint) from config.</param>
    public void LoadFromConfig(IEnumerable<(string host, int port, string? fingerprint)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var (host, port, fingerprint) in entries)
        {
            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                _trustedKeys[HostKeyFormats.MakeKey(host, port)] = CreateLegacyEntry(fingerprint);
            }
        }
    }

    /// <summary>
    /// Populate the store from persisted metadata-rich host key entries.
    /// Called at startup before any connections are made.
    /// </summary>
    public void LoadEntriesFromConfig(IEnumerable<(string host, int port, HostKeyEntry? entry)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var (host, port, entry) in entries)
        {
            if (entry is not null && !string.IsNullOrWhiteSpace(entry.Fingerprint))
            {
                _trustedKeys[HostKeyFormats.MakeKey(host, port)] = NormalizeEntry(entry);
            }
        }
    }

    /// <summary>
    /// Get the stored fingerprint for a host, or null if unknown.
    /// </summary>
    public string? GetFingerprint(string host, int port)
    {
        ArgumentNullException.ThrowIfNull(host);
        return _trustedKeys.TryGetValue(HostKeyFormats.MakeKey(host, port), out var entry) ? entry.Fingerprint : null;
    }

    /// <summary>
    /// Get the stored metadata entry for a host, or null if unknown.
    /// </summary>
    public HostKeyEntry? GetEntry(string host, int port)
    {
        ArgumentNullException.ThrowIfNull(host);
        return _trustedKeys.TryGetValue(HostKeyFormats.MakeKey(host, port), out var entry) ? entry : null;
    }

    /// <summary>
    /// Manually trust a host key (e.g., after user confirmation dialog).
    /// Trust uses upsert semantics and emits <see cref="HostKeyEvent"/> for persistence.
    /// </summary>
    public void Trust(string host, int port, string fingerprint)
        => Trust(host, port, fingerprint, UnknownAlgorithm, HostKeySource.UserConfirmed);

    /// <summary>
    /// Manually trust a host key with metadata.
    /// Trust uses upsert semantics and emits <see cref="HostKeyEvent"/> for persistence.
    /// </summary>
    public void Trust(
        string host,
        int port,
        string fingerprint,
        string algorithm,
        HostKeySource source)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(fingerprint);

        var key = HostKeyFormats.MakeKey(host, port);
        _trustedKeys[key] = new HostKeyEntry(
            fingerprint,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(algorithm) ? UnknownAlgorithm : algorithm,
            source);
        HostKeyEvent?.Invoke(key, fingerprint, true);
    }

    /// <summary>
    /// Store a pre-built host key entry.
    /// </summary>
    public void TrustEntry(string host, int port, HostKeyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(entry);

        var normalized = NormalizeEntry(entry);
        var key = HostKeyFormats.MakeKey(host, port);
        _trustedKeys[key] = normalized;
        HostKeyEvent?.Invoke(key, normalized.Fingerprint, true);
    }

    /// <summary>
    /// Remove a previously trusted host key.
    /// </summary>
    public bool Remove(string host, int port)
    {
        ArgumentNullException.ThrowIfNull(host);
        return _trustedKeys.TryRemove(HostKeyFormats.MakeKey(host, port), out _);
    }

    /// <summary>
    /// Get all trusted entries for persistence to config.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAllTrusted()
        => _trustedKeys.ToDictionary(
            static kvp => kvp.Key,
            static kvp => kvp.Value.Fingerprint,
            StringComparer.Ordinal);

    /// <summary>
    /// Get all trusted metadata entries.
    /// </summary>
    public IReadOnlyDictionary<string, HostKeyEntry> GetAllEntries()
        => _trustedKeys.ToDictionary(
            static kvp => kvp.Key,
            static kvp => kvp.Value,
            StringComparer.Ordinal);

    internal void SetEntry(string host, int port, HostKeyEntry entry, bool raiseEvent)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(entry);

        var normalized = NormalizeEntry(entry);
        var key = HostKeyFormats.MakeKey(host, port);
        _trustedKeys[key] = normalized;
        if (raiseEvent)
        {
            HostKeyEvent?.Invoke(key, normalized.Fingerprint, true);
        }
    }

    /// <summary>
    /// Compute a SHA256 fingerprint from raw host key bytes.
    /// Format: "SHA256:&lt;base64-no-padding&gt;" (matches OpenSSH convention).
    /// </summary>
    internal static string ComputeFingerprint(byte[] hostKey)
    {
        return HostKeyFormats.ComputeSha256Fingerprint(hostKey);
    }

    private static HostKeyEntry CreateLegacyEntry(string fingerprint)
    {
        return new HostKeyEntry(
            fingerprint,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            UnknownAlgorithm,
            HostKeySource.Unknown);
    }

    private static HostKeyEntry NormalizeEntry(HostKeyEntry entry)
    {
        return entry with
        {
            Algorithm = string.IsNullOrWhiteSpace(entry.Algorithm) ? UnknownAlgorithm : entry.Algorithm
        };
    }
}
