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
/// Verifier that always accepts the presented host key.
/// This implementation is unsafe for production and must only be used in tests
/// or as a temporary compatibility fallback while dependency injection is being
/// migrated to a real verifier.
/// </summary>
public sealed class AutoAcceptHostKeyVerifier : IHostKeyVerifier
{
    public static AutoAcceptHostKeyVerifier Instance { get; } = new();

    private AutoAcceptHostKeyVerifier()
    {
    }

    /// <inheritdoc/>
    public Task<HostKeyDecision> VerifyAsync(
        string host,
        int port,
        string algorithm,
        string presentedFingerprint,
        string? storedFingerprint,
        CancellationToken ct = default)
    {
        return Task.FromResult(HostKeyDecision.Accept);
    }
}
