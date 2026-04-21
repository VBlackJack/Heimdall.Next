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
/// Represents a single importable OpenSSH host alias resolved from ssh_config.
/// </summary>
public sealed record OpenSshImportCandidate
{
    /// <summary>
    /// Gets the literal alias declared in the Host directive.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Gets the resolved host name or address.
    /// </summary>
    public required string HostName { get; init; }

    /// <summary>
    /// Gets the SSH port. Defaults to 22.
    /// </summary>
    public int Port { get; init; } = 22;

    /// <summary>
    /// Gets the optional SSH username.
    /// </summary>
    public string? User { get; init; }

    /// <summary>
    /// Gets the optional identity file path after tilde expansion.
    /// </summary>
    public string? IdentityFile { get; init; }

    /// <summary>
    /// Gets the 1-based source line number of the Host directive.
    /// </summary>
    public int SourceLineNumber { get; init; }
}
