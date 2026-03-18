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
using Heimdall.Sftp;

namespace Heimdall.App.Services;

public partial class ConnectionService
{
    /// <summary>
    /// Establishes an FTP browser session using .NET's built-in FtpWebRequest.
    /// Returns a connected <see cref="FtpBrowser"/> wrapped in an
    /// <see cref="FtpSessionBundle"/> on success.
    /// </summary>
    public async Task<ConnectionResult> ConnectFtpAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        _connectionSm.TryTransition(server.Id, ConnectionState.ValidatingConfig);

        if (string.IsNullOrWhiteSpace(server.RemoteServer))
        {
            var msg = _localizer["ErrorInvalidTargetHost"];
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null);
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.LaunchingFtp);

        string host = server.RemoteServer;
        int port = server.FtpPort > 0 ? server.FtpPort : 21;
        string? username = server.FtpUsername;
        string? password = DecryptPassword(server.FtpPasswordEncrypted);

        var browser = new FtpBrowser();

        try
        {
            await browser.ConnectAsync(host, port, username, password, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            browser.Dispose();
            Core.Logging.FileLogger.Warn(
                $"FTP connect failed: {ex.Message}");
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(false, ex.Message, null);
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
        return new ConnectionResult(true, null, new FtpSessionBundle(browser));
    }
}

/// <summary>
/// Bundles an FTP browser session for use by the embedded SFTP/FTP view.
/// </summary>
public record FtpSessionBundle(FtpBrowser Browser) : ISessionResult;
