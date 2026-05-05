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

namespace Heimdall.Ssh;

/// <summary>
/// Carries information about a security-relevant SSH session failure that
/// callers must distinguish from a benign network disconnect.
/// </summary>
/// <param name="Code">Structured failure code.</param>
/// <param name="Message">Human-readable failure description.</param>
/// <param name="Host">Remote host the session was using.</param>
/// <param name="Port">Remote port the session was using.</param>
/// <param name="Algorithm">Presented host-key algorithm, when available.</param>
/// <param name="PresentedFingerprint">Presented host-key fingerprint, when available.</param>
/// <param name="StoredFingerprint">Stored host-key fingerprint, when available.</param>
public sealed record SshSessionSecurityEvent(
    SshFailureCode Code,
    string Message,
    string Host,
    int Port,
    string? Algorithm = null,
    string? PresentedFingerprint = null,
    string? StoredFingerprint = null);
