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

namespace Heimdall.Core.SessionDiagnostics;

/// <summary>
/// Identifies the stage where a session connection failed.
/// </summary>
public enum SessionFailureStage
{
    Unknown = 0,
    SshGateway,
    SshPreflight,
    SshAuth,
    SshHostKey,
    SshPlinkFallback,
    SshPipeMode,
    RdpTunnel,
    RdpCredentialWrite,
    RdpFileWrite,
    RdpLaunch,
    RdpActiveXConnect,
    RdpActiveXDisconnect,
    GenericFailure,
}

/// <summary>
/// Structured diagnostics for a failed connection attempt.
/// </summary>
/// <param name="Stage">Stage where the failure occurred.</param>
/// <param name="MessageKey">Localization key describing the failure.</param>
/// <param name="Code">Optional numeric protocol-specific code.</param>
/// <param name="Detail">Optional raw diagnostic detail, typically an exception message.</param>
public sealed record SessionDiagnostic(
    SessionFailureStage Stage,
    string MessageKey,
    int? Code = null,
    string? Detail = null);
