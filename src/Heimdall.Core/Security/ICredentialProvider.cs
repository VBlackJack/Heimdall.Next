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

namespace Heimdall.Core.Security;

/// <summary>
/// Abstraction for external credential providers (KeePassXC, Bitwarden CLI,
/// 1Password CLI, pass, or any command-line password manager).
/// </summary>
public interface ICredentialProvider
{
    /// <summary>Display name of this credential provider (e.g., "Command").</summary>
    string Name { get; }

    /// <summary>Whether the provider is configured and ready to use.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Retrieves credentials for the given server from the external store.
    /// </summary>
    /// <param name="serverHost">Target server hostname or IP.</param>
    /// <param name="port">Target port number.</param>
    /// <param name="username">Optional username hint for lookup.</param>
    /// <param name="title">Optional display name / entry title for lookup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The retrieved credential, or null if not found.</returns>
    Task<CredentialResult?> GetCredentialAsync(
        string serverHost,
        int port,
        string? username,
        string? title,
        CancellationToken ct = default);
}

/// <summary>
/// Credential retrieved from an external provider.
/// </summary>
/// <param name="Username">The resolved username (may differ from the hint).</param>
/// <param name="Password">The plaintext password.</param>
public sealed record CredentialResult(string Username, string Password);
