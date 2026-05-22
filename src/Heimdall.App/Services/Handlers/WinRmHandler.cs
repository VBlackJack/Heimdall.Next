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

using Heimdall.App.Services.WinRm;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;
using Heimdall.Terminal;
using Heimdall.Terminal.ConPty;

namespace Heimdall.App.Services.Handlers;

/// <summary>
/// Handles WinRM sessions by launching a local PowerShell host inside an embedded terminal.
/// </summary>
internal sealed class WinRmHandler : IProtocolHandler
{
    private readonly ConnectionStateMachine _connectionSm;
    private readonly LocalizationManager _localizer;
    private readonly WinRmPreflight _preflight;

    public WinRmHandler(
        ConnectionStateMachine connectionSm,
        LocalizationManager localizer,
        WinRmPreflight? preflight = null)
    {
        _connectionSm = connectionSm;
        _localizer = localizer;
        _preflight = preflight ?? new WinRmPreflight();
    }

    public string Protocol => "WINRM";

    public async Task<ConnectionResult> ConnectAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct,
        RdpModeOverride rdpModeOverride = RdpModeOverride.UseProfile)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        _connectionSm.TryTransition(server.Id, ConnectionState.ValidatingConfig);
        _connectionSm.TryTransition(server.Id, ConnectionState.LaunchingLocal);

        ITerminalSession? session = null;
        WinRmCredentialBootstrap? bootstrap = null;
        string? bootstrapScriptPath = null;

        try
        {
            ct.ThrowIfCancellationRequested();
            await _preflight.EnsureReachableAsync(server, ct).ConfigureAwait(false);

            if (server.WinRmIdentityMode == WinRmIdentityMode.Credential)
            {
                bootstrap = new WinRmCredentialBootstrap();
                bootstrapScriptPath = bootstrap.Write(server).ScriptPath;
            }

            WinRmPowerShellLaunchSpec spec =
                new WinRmPowerShellLaunchBuilder().Build(server, bootstrapScriptPath);

            session = ConPtySession.IsAvailable
                ? new ConPtySession()
                : new PipeModeSession();

            Core.Logging.FileLogger.Info($"Launching WinRM session for host '{server.RemoteServer}'");

            await session.StartAsync(spec.Executable, spec.Arguments, workingDirectory: null)
                .ConfigureAwait(false);

            Core.Logging.FileLogger.Info(
                $"WinRM terminal started for host '{server.RemoteServer}'");

            _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
            return new ConnectionResult(
                true,
                null,
                new TerminalSessionResult(session, server.RemoteServer));
        }
        catch (OperationCanceledException)
        {
            DeleteBootstrap(bootstrap, bootstrapScriptPath);
            session?.Dispose();
            throw;
        }
        catch (WinRmPreflightException ex)
        {
            string message = _localizer.Format(ex.LocalizationKey, ex.LocalizationArguments);
            Core.Logging.FileLogger.Warn(
                $"WinRM preflight failed for host '{server.RemoteServer}' protocol=WINRM");
            _connectionSm.SetError(server.Id, message);
            return new ConnectionResult(false, message, null);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BuildFailureResult(
                server,
                session,
                bootstrap,
                bootstrapScriptPath,
                "ErrorWinRmInvalidConfiguration",
                ex);
        }
        catch (ArgumentException ex)
        {
            return BuildFailureResult(
                server,
                session,
                bootstrap,
                bootstrapScriptPath,
                "ErrorWinRmInvalidConfiguration",
                ex);
        }
        catch (InvalidOperationException ex)
        {
            return BuildFailureResult(
                server,
                session,
                bootstrap,
                bootstrapScriptPath,
                "ErrorWinRmCredentialUnavailable",
                ex,
                includeExceptionMessage: false);
        }
        catch (Exception ex)
        {
            return BuildFailureResult(
                server,
                session,
                bootstrap,
                bootstrapScriptPath,
                "ErrorWinRmLaunchFailed",
                ex);
        }
    }

    private ConnectionResult BuildFailureResult(
        ServerProfileDto server,
        ITerminalSession? session,
        WinRmCredentialBootstrap? bootstrap,
        string? bootstrapScriptPath,
        string localizationKey,
        Exception exception,
        bool includeExceptionMessage = true)
    {
        DeleteBootstrap(bootstrap, bootstrapScriptPath);
        session?.Dispose();

        string message = includeExceptionMessage
            ? _localizer.Format(localizationKey, exception.Message)
            : _localizer[localizationKey];

        Core.Logging.FileLogger.Warn($"WinRM connection failed for host '{server.RemoteServer}'");
        _connectionSm.SetError(server.Id, message);
        return new ConnectionResult(false, message, null);
    }

    private static void DeleteBootstrap(
        WinRmCredentialBootstrap? bootstrap,
        string? bootstrapScriptPath)
    {
        if (bootstrap is null || string.IsNullOrWhiteSpace(bootstrapScriptPath))
        {
            return;
        }

        bootstrap.Delete(bootstrapScriptPath);
    }
}
