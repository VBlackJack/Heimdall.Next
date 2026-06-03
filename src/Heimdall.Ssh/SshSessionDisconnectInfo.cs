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
/// Describes why an interactive SSH session disconnected.
/// </summary>
/// <param name="Message">Optional user-visible disconnect detail.</param>
/// <param name="Failure">Structured failure information when the disconnect came from a classified SSH failure.</param>
/// <param name="IsClean">Whether the disconnect represents a deliberate or normal session end.</param>
public sealed record SshSessionDisconnectInfo(
    string? Message,
    SshFailureInfo? Failure,
    bool IsClean)
{
    /// <summary>Creates a normal session-end disconnect.</summary>
    public static SshSessionDisconnectInfo Clean(string? message = null)
        => new(message, Failure: null, IsClean: true);

    /// <summary>Creates a disconnect with no reliable SSH failure code.</summary>
    public static SshSessionDisconnectInfo Unclassified(string? message)
        => new(message, Failure: null, IsClean: false);

    /// <summary>Creates a disconnect from classified SSH failure information.</summary>
    public static SshSessionDisconnectInfo FromFailure(SshFailureInfo failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new SshSessionDisconnectInfo(failure.Message, failure, IsClean: false);
    }
}
