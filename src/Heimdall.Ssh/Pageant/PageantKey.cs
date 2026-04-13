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

namespace Heimdall.Ssh.Pageant;

/// <summary>
/// Represents an SSH identity key loaded in the Pageant SSH agent.
/// </summary>
/// <param name="Blob">Raw public key blob as returned by the agent.</param>
/// <param name="Comment">Human-readable comment associated with the key (typically the file path).</param>
/// <param name="KeyType">SSH key type identifier (e.g., "ssh-rsa", "ssh-ed25519").</param>
public sealed record PageantKey(byte[] Blob, string Comment, string KeyType);
