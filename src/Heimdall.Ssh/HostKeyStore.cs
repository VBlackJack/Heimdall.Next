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
using System.Security.Cryptography;

namespace Heimdall.Ssh;

/// <summary>
/// Verification result returned by <see cref="HostKeyStore.Verify"/>.
/// </summary>
/// <param name="Trusted">Whether the host key should be accepted.</param>
/// <param name="FirstUse">Whether this is the first time seeing this host.</param>
/// <param name="Fingerprint">SHA256 fingerprint of the presented host key.</param>
/// <param name="StoredFingerprint">Previously stored fingerprint, if any.</param>
public sealed record HostKeyVerifyResult(
    bool Trusted,
    bool FirstUse,
    string Fingerprint,
    string? StoredFingerprint);

/// <summary>
/// Trust-On-First-Use host key verification store.
/// Stores SHA256 fingerprints per host:port, blocks on mismatch.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </summary>
public sealed class HostKeyStore
{
    private readonly ConcurrentDictionary<string, string> _trustedKeys = new();

    /// <summary>
    /// Raised whenever a host key is verified.
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

        var key = MakeKey(host, port);
        var fingerprint = ComputeFingerprint(hostKey);

        if (_trustedKeys.TryGetValue(key, out var stored))
        {
            var match = string.Equals(stored, fingerprint, StringComparison.Ordinal);
            HostKeyEvent?.Invoke(key, fingerprint, match);
            return new HostKeyVerifyResult(match, FirstUse: false, fingerprint, stored);
        }

        // First use: trusted by TOFU policy
        HostKeyEvent?.Invoke(key, fingerprint, true);
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
                _trustedKeys[MakeKey(host, port)] = fingerprint;
            }
        }
    }

    /// <summary>
    /// Get the stored fingerprint for a host, or null if unknown.
    /// </summary>
    public string? GetFingerprint(string host, int port)
    {
        ArgumentNullException.ThrowIfNull(host);
        return _trustedKeys.TryGetValue(MakeKey(host, port), out var fp) ? fp : null;
    }

    /// <summary>
    /// Manually trust a host key (e.g., after user confirmation dialog).
    /// </summary>
    public void Trust(string host, int port, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(fingerprint);
        _trustedKeys[MakeKey(host, port)] = fingerprint;
    }

    /// <summary>
    /// Remove a previously trusted host key.
    /// </summary>
    public bool Remove(string host, int port)
    {
        ArgumentNullException.ThrowIfNull(host);
        return _trustedKeys.TryRemove(MakeKey(host, port), out _);
    }

    /// <summary>
    /// Get all trusted entries for persistence to config.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAllTrusted()
        => _trustedKeys;

    private static string MakeKey(string host, int port)
        => host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";

    /// <summary>
    /// Compute a SHA256 fingerprint from raw host key bytes.
    /// Format: "SHA256:&lt;base64-no-padding&gt;" (matches OpenSSH convention).
    /// </summary>
    internal static string ComputeFingerprint(byte[] hostKey)
    {
        var hash = SHA256.HashData(hostKey);
        var base64 = Convert.ToBase64String(hash).TrimEnd('=');
        return $"SHA256:{base64}";
    }
}
