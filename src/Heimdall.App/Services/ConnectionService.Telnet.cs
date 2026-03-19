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
using Heimdall.Core.Models;

namespace Heimdall.App.Services;

public partial class ConnectionService
{
    /// <summary>
    /// Opens a raw TCP (Telnet) connection to the specified host and port.
    /// Returns a <see cref="TerminalSessionResult"/> wrapping the
    /// <see cref="Terminal.TelnetSession"/> so it renders in the standard
    /// WebView2 + xterm.js terminal view.
    /// </summary>
    public async Task<ConnectionResult> ConnectTelnetAsync(
        ServerProfileDto server, AppSettings settings, CancellationToken ct = default)
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

        Core.Logging.FileLogger.Info(
            $"Launching Telnet session: {server.RemoteServer}:{port}");

        var session = new Terminal.TelnetSession(server.RemoteServer, port);

        try
        {
            // Host and port are baked into the TelnetSession constructor;
            // StartAsync signature matches ITerminalSession but ignores
            // executable/arguments.
            await session.StartAsync(string.Empty, string.Empty);
        }
        catch (Exception ex)
        {
            session.Dispose();
            Core.Logging.FileLogger.Error("Telnet connection failed", ex);
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(false, ex.Message, null);
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
        return new ConnectionResult(true, null, new TerminalSessionResult(session));
    }
}
