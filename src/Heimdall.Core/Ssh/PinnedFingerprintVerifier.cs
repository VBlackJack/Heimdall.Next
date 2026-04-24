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

namespace Heimdall.Core.Ssh;

/// <summary>
/// Non-interactive host key verifier that accepts only one pre-resolved fingerprint.
/// </summary>
public sealed class PinnedFingerprintVerifier(
    string host,
    int port,
    string fingerprint) : IHostKeyVerifier
{
    public string Host { get; } = host ?? throw new ArgumentNullException(nameof(host));

    public int Port { get; } = port;

    public string Fingerprint { get; } = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));

    public bool Matches(string host, int port, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(fingerprint);

        return Port == port
            && string.Equals(Host, host, StringComparison.Ordinal)
            && string.Equals(Fingerprint, fingerprint, StringComparison.Ordinal);
    }

    public Task<HostKeyDecision> VerifyAsync(
        string host,
        int port,
        string algorithm,
        string presentedFingerprint,
        string? storedFingerprint,
        CancellationToken ct = default)
    {
        return Task.FromResult(Matches(host, port, presentedFingerprint)
            ? HostKeyDecision.Accept
            : HostKeyDecision.Reject);
    }
}
