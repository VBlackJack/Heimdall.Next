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
/// Fail-closed host key verifier used when interactive verification is unavailable.
/// </summary>
public sealed class RejectingHostKeyVerifier : IHostKeyVerifier
{
    public static RejectingHostKeyVerifier Instance { get; } = new();

    private RejectingHostKeyVerifier()
    {
    }

    public Task<HostKeyDecision> VerifyAsync(
        string host,
        int port,
        string algorithm,
        string presentedFingerprint,
        string? storedFingerprint,
        CancellationToken ct = default)
    {
        return Task.FromResult(HostKeyDecision.Reject);
    }
}
