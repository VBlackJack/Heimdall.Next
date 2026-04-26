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

using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;

namespace Heimdall.App.Services.Handlers;

/// <summary>
/// Handles Telnet connection logic.
/// </summary>
internal sealed class TelnetHandler : IProtocolHandler
{
    private readonly ConnectionStateMachine _connectionSm;
    private readonly LocalizationManager _localizer;

    public TelnetHandler(
        ConnectionStateMachine connectionSm,
        LocalizationManager localizer)
    {
        _connectionSm = connectionSm;
        _localizer = localizer;
    }

    public string Protocol => "TELNET";

    /// <summary>
    /// Opens a raw TCP (Telnet) connection to the specified host and port.
    /// </summary>
    public async Task<ConnectionResult> ConnectAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        _connectionSm.TryTransition(server.Id, ConnectionState.ValidatingConfig);

        if (string.IsNullOrWhiteSpace(server.RemoteServer))
        {
            var msg = _localizer["ErrorInvalidTargetHost"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null);
        }

        var port = server.TelnetPort > 0 ? server.TelnetPort : DefaultPorts.Telnet;

        _connectionSm.TryTransition(server.Id, ConnectionState.LaunchingTelnet);

        Core.Logging.FileLogger.Info($"Launching Telnet session: {server.RemoteServer}:{port}");

        var session = new Terminal.TelnetSession(
            server.RemoteServer,
            port,
            settings.TelnetConnectTimeoutMs);

        try
        {
            await session.StartAsync(string.Empty, string.Empty).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            session.Dispose();
            Core.Logging.FileLogger.Error("Telnet connection failed", ex);
            var userMsg = _localizer.Format("ErrorTelnetConnectionFailed", ex.Message);
            _connectionSm.SetError(server.Id, userMsg);
            return new ConnectionResult(false, userMsg, null);
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
        return new ConnectionResult(
            true,
            null,
            new TerminalSessionResult(session, $"{server.RemoteServer}:{port}"));
    }
}
