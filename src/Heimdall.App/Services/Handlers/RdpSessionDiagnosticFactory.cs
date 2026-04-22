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

using Heimdall.Core.SessionDiagnostics;

namespace Heimdall.App.Services.Handlers;

/// <summary>
/// Maps RDP pre-launch failures to the shared session diagnostic contract.
/// </summary>
internal static class RdpSessionDiagnosticFactory
{
    internal static SessionDiagnostic CreateTunnelFailure(string? detail)
    {
        return new SessionDiagnostic(
            SessionFailureStage.RdpTunnel,
            "SessionFailureStageRdpTunnel",
            null,
            detail);
    }

    internal static SessionDiagnostic FromCredentialWriteFailure(string? detail)
    {
        return new SessionDiagnostic(
            SessionFailureStage.RdpCredentialWrite,
            "SessionFailureStageRdpCredentialWrite",
            TryExtractWin32Error(detail),
            detail);
    }

    internal static SessionDiagnostic FromRdpFileWriteException(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return new SessionDiagnostic(
            SessionFailureStage.RdpFileWrite,
            "SessionFailureStageRdpFileWrite",
            null,
            ex.Message);
    }

    internal static SessionDiagnostic FromMstscLaunchException(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return new SessionDiagnostic(
            SessionFailureStage.RdpLaunch,
            "SessionFailureStageRdpLaunch",
            null,
            ex.Message);
    }

    internal static SessionDiagnostic FromGenericException(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return new SessionDiagnostic(
            SessionFailureStage.GenericFailure,
            "SessionFailureStageGeneric",
            null,
            ex.Message);
    }

    private static int? TryExtractWin32Error(string? detail)
    {
        const string prefix = "WIN32_ERROR_";

        if (string.IsNullOrWhiteSpace(detail) ||
            !detail.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(detail[prefix.Length..], out var value) ? value : null;
    }
}
