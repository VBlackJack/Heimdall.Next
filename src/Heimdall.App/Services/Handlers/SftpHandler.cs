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
using Heimdall.Core.StateMachine;
using Heimdall.Sftp;
using Heimdall.Ssh;

namespace Heimdall.App.Services.Handlers;

/// <summary>
/// Handles SFTP connection logic.
/// </summary>
internal sealed class SftpHandler : IProtocolHandler
{
    private readonly ITunnelService _tunnelService;
    private readonly ConnectionStateMachine _connectionSm;
    private readonly HostKeyStore _hostKeyStore;

    public SftpHandler(
        ITunnelService tunnelService,
        ConnectionStateMachine connectionSm,
        HostKeyStore hostKeyStore)
    {
        _tunnelService = tunnelService;
        _connectionSm = connectionSm;
        _hostKeyStore = hostKeyStore;
    }

    public string Protocol => "SFTP";

    /// <summary>
    /// Establishes an SFTP browser session, optionally through a tunnel.
    /// Returns a connected <see cref="SftpBrowser"/> on success.
    /// </summary>
    public async Task<ConnectionResult> ConnectAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        _connectionSm.TryTransition(server.Id, ConnectionState.ValidatingConfig);

        var (tunnelOk, _, targetHost, targetPort, tunnelError) =
            await _tunnelService.SetupTunnelIfNeededAsync(server, server.SshPort, settings, ct)
                .ConfigureAwait(false);

        if (!tunnelOk)
        {
            return new ConnectionResult(false, tunnelError, null);
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.LaunchingSftp);

        var sshParams = new SshConnectionParams
        {
            Host = targetHost,
            Port = targetPort,
            Username = server.SshUsername ?? string.Empty,
            Password = ConnectionHelpers.DecryptPassword(server.SshPasswordEncrypted),
            KeyPath = string.IsNullOrWhiteSpace(server.SshKeyPath) ? null : server.SshKeyPath,
            AgentForwarding = server.SshAgentForwarding,
            Compression = server.SshCompression
        };

        var browser = new SftpBrowser();

        try
        {
            await browser.ConnectAsync(sshParams, _hostKeyStore, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            browser.Dispose();
            var failure = FailureClassifier.Classify(ex, sshParams);
            Core.Logging.FileLogger.Warn($"SFTP connect failed: {failure.Code} - {ex.Message}");
            _connectionSm.SetError(server.Id, failure.Message);
            return new ConnectionResult(false, failure.Message, null);
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.Connected);
        return new ConnectionResult(true, null, new SftpSessionBundle(browser, sshParams));
    }
}
