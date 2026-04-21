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
/// Parser output: one expanded known_hosts entry with a concrete host and port.
/// Carries the decoded key blob for later fingerprint computation.
/// </summary>
public sealed record KnownHostsRawEntry
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    public required string KeyType { get; init; }

    public required byte[] Base64Key { get; init; }

    public required int SourceLineNumber { get; init; }
}
