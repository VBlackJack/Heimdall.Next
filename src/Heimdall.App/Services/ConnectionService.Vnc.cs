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
    /// Validates VNC connection parameters and returns a session result
    /// containing the target host, port, and optional password.
    /// The actual VNC connection is established by the WebSocket proxy
    /// and noVNC client in the embedded view.
    /// </summary>
    public Task<ConnectionResult> ConnectVncAsync(
        ServerProfileDto server, AppSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        _connectionSm.TryTransition(server.Id, ConnectionState.ValidatingConfig);

        if (string.IsNullOrWhiteSpace(server.RemoteServer))
        {
            var msg = _localizer["ErrorInvalidTargetHost"];
            _connectionSm.SetError(server.Id, msg);
            return Task.FromResult(new ConnectionResult(false, msg, null));
        }

        var vncPort = server.VncPort > 0 ? server.VncPort : DefaultPorts.Vnc;

        _connectionSm.TryTransition(server.Id, ConnectionState.LaunchingVnc);

        string? password = null;
        if (!string.IsNullOrEmpty(server.VncPassword))
        {
            password = DecryptPassword(server.VncPassword);
        }

        // Do NOT transition to Connected here — the actual VNC connection is established
        // asynchronously by the WebSocket proxy + noVNC in EmbeddedVncView.
        // The view will report connection success/failure via SessionConnected/SessionError events.

        var session = new VncSessionResult(server.Id, server.RemoteServer, vncPort, password, server.VncViewOnly);
        return Task.FromResult(new ConnectionResult(true, null, session));
    }
}

/// <summary>
/// Holds VNC connection parameters for the embedded noVNC view.
/// The proxy and WebView2 rendering are managed by <see cref="Views.EmbeddedVncView"/>.
/// </summary>
public record VncSessionResult(
    string ServerId,
    string Host,
    int Port,
    string? Password = null,
    bool ViewOnly = false) : ISessionResult;
