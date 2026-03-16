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
using Heimdall.Core.Security;
using Heimdall.Core.StateMachine;
using Heimdall.Sftp;
using Heimdall.Ssh;

namespace Heimdall.App.Services;

/// <summary>
/// Orchestrates the full connection lifecycle for RDP, SSH, and SFTP sessions.
/// Resolves gateway chains, opens tunnels, performs preflight checks, and
/// delegates to the appropriate session engine.
/// </summary>
public class ConnectionService
{
    private readonly ConfigManager _configManager;
    private readonly TunnelManager _tunnelManager;
    private readonly HostKeyStore _hostKeyStore;
    private readonly ConnectionStateMachine _connectionSm;
    private readonly LocalizationManager _localizer;

    public ConnectionService(
        ConfigManager configManager,
        TunnelManager tunnelManager,
        HostKeyStore hostKeyStore,
        ConnectionStateMachine connectionSm,
        LocalizationManager localizer)
    {
        _configManager = configManager;
        _tunnelManager = tunnelManager;
        _hostKeyStore = hostKeyStore;
        _connectionSm = connectionSm;
        _localizer = localizer;
    }

    /// <summary>
    /// Establishes an RDP connection, optionally through an SSH tunnel.
    /// Returns a result containing the tunnel local port (for embedded RDP)
    /// or null on failure.
    /// </summary>
    public async Task<ConnectionResult> ConnectRdpAsync(
        RdpServerDto server,
        AppSettings settings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.ValidatingConfig);

        // Resolve tunnel if gateway is configured and not a direct connection
        if (!server.UseDirectConnection && !string.IsNullOrEmpty(server.SshGatewayId))
        {
            var tunnelResult = await EstablishTunnelAsync(
                server.Id, server.SshGatewayId, server.RemoteServer,
                server.RemotePort, server.LocalPort, settings, ct)
                .ConfigureAwait(false);

            if (!tunnelResult.Success)
            {
                return new ConnectionResult(false, tunnelResult.ErrorMessage, null);
            }
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.LaunchingRdp);

        // The actual RDP host creation is handled by the View layer (ActiveX/mstsc).
        // Return success so the ViewModel can create the session tab.
        return new ConnectionResult(true, null, server);
    }

    /// <summary>
    /// Establishes an SSH shell connection, optionally through a tunnel.
    /// Returns a connected <see cref="SshShellSession"/> on success.
    /// </summary>
    public async Task<ConnectionResult> ConnectSshAsync(
        RdpServerDto server,
        AppSettings settings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.ValidatingConfig);

        string targetHost = server.RemoteServer;
        int targetPort = server.SshPort;

        // Tunnel through gateway if configured
        if (!server.UseDirectConnection && !string.IsNullOrEmpty(server.SshGatewayId))
        {
            var tunnelResult = await EstablishTunnelAsync(
                server.Id, server.SshGatewayId, server.RemoteServer,
                server.SshPort, server.LocalPort, settings, ct)
                .ConfigureAwait(false);

            if (!tunnelResult.Success)
            {
                return new ConnectionResult(false, tunnelResult.ErrorMessage, null);
            }

            // Connect through the tunnel
            targetHost = "127.0.0.1";
            targetPort = server.LocalPort;
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.LaunchingSsh);

        var sshParams = new SshConnectionParams
        {
            Host = targetHost,
            Port = targetPort,
            Username = server.SshUsername ?? "",
            KeyPath = string.IsNullOrWhiteSpace(server.SshKeyPath) ? null : server.SshKeyPath,
            AgentForwarding = server.SshAgentForwarding,
            Compression = server.SshCompression
        };

        var session = new SshShellSession();

        try
        {
            await session.ConnectAsync(sshParams, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            session.Dispose();
            var failure = FailureClassifier.Classify(ex, sshParams);
            _connectionSm.SetError(server.Id, failure.Message);
            return new ConnectionResult(false, failure.Message, null);
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.Connected);
        return new ConnectionResult(true, null, session);
    }

    /// <summary>
    /// Establishes an SFTP browser session, optionally through a tunnel.
    /// Returns a connected <see cref="SftpBrowser"/> on success.
    /// </summary>
    public async Task<ConnectionResult> ConnectSftpAsync(
        RdpServerDto server,
        AppSettings settings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.ValidatingConfig);

        string targetHost = server.RemoteServer;
        int targetPort = server.SshPort;

        // Tunnel through gateway if configured
        if (!server.UseDirectConnection && !string.IsNullOrEmpty(server.SshGatewayId))
        {
            var tunnelResult = await EstablishTunnelAsync(
                server.Id, server.SshGatewayId, server.RemoteServer,
                server.SshPort, server.LocalPort, settings, ct)
                .ConfigureAwait(false);

            if (!tunnelResult.Success)
            {
                return new ConnectionResult(false, tunnelResult.ErrorMessage, null);
            }

            targetHost = "127.0.0.1";
            targetPort = server.LocalPort;
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.LaunchingSftp);

        var sshParams = new SshConnectionParams
        {
            Host = targetHost,
            Port = targetPort,
            Username = server.SshUsername ?? "",
            KeyPath = string.IsNullOrWhiteSpace(server.SshKeyPath) ? null : server.SshKeyPath,
            AgentForwarding = server.SshAgentForwarding,
            Compression = server.SshCompression
        };

        var browser = new SftpBrowser();

        try
        {
            await browser.ConnectAsync(sshParams, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            browser.Dispose();
            var failure = FailureClassifier.Classify(ex, sshParams);
            _connectionSm.SetError(server.Id, failure.Message);
            return new ConnectionResult(false, failure.Message, null);
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.Connected);
        return new ConnectionResult(true, null, browser);
    }

    /// <summary>
    /// Runs authentication preflight checks for a server's gateway.
    /// </summary>
    /// <param name="server">The server DTO to check.</param>
    /// <param name="settings">Current application settings containing gateway definitions.</param>
    /// <returns>A preflight result indicating pass or fail.</returns>
    public PreflightResult RunPreflight(RdpServerDto server, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrEmpty(server.SshGatewayId) || server.UseDirectConnection)
        {
            return PreflightResult.Ok();
        }

        var gateway = settings.SshGateways.FirstOrDefault(
            g => string.Equals(g.Id, server.SshGatewayId, StringComparison.OrdinalIgnoreCase));

        if (gateway is null)
        {
            return PreflightResult.Fail(
                SshFailureCode.Unknown,
                _localizer.Format("ErrorGatewayNotFound", server.SshGatewayId));
        }

        var connParams = BuildGatewayParams(gateway);
        bool isTunnel = server.ConnectionType?.Equals("RDP", StringComparison.OrdinalIgnoreCase) == true;
        return AuthPreflightChecker.Check(connParams, isTunnelMode: isTunnel);
    }

    /// <summary>
    /// Resolves the gateway chain, performs preflight, and opens a tunnel.
    /// </summary>
    private async Task<TunnelResult> EstablishTunnelAsync(
        string serverId,
        string gatewayId,
        string remoteHost,
        int remotePort,
        int localPort,
        AppSettings settings,
        CancellationToken ct)
    {
        _connectionSm.TryTransition(serverId, Core.Models.ConnectionState.EstablishingTunnel);

        List<SshConnectionParams> chain;

        try
        {
            chain = GatewayChainResolver.ResolveChain(
                gatewayId, settings.SshGateways, DecryptPassword);
        }
        catch (Exception ex)
        {
            _connectionSm.SetError(serverId, ex.Message);
            return new TunnelResult(false, null, ex.Message, SshFailureCode.Unknown);
        }

        // Preflight check on the root gateway
        var preflight = AuthPreflightChecker.Check(chain[0], isTunnelMode: true);
        if (!preflight.Success)
        {
            var msg = preflight.Message ?? _localizer["ErrorPreflightFailed"];
            _connectionSm.SetError(serverId, msg);
            return new TunnelResult(false, null, msg, preflight.FailureCode);
        }

        // Open tunnel (single-hop or chained)
        TunnelResult result;
        if (chain.Count == 1)
        {
            result = await _tunnelManager.OpenTunnelAsync(
                chain[0], remoteHost, remotePort, localPort, ct)
                .ConfigureAwait(false);
        }
        else
        {
            result = await _tunnelManager.OpenChainedTunnelAsync(
                chain, remoteHost, remotePort, localPort, ct)
                .ConfigureAwait(false);
        }

        if (result.Success)
        {
            _connectionSm.TryTransition(serverId, Core.Models.ConnectionState.TunnelEstablished);
        }
        else
        {
            _connectionSm.SetError(serverId, result.ErrorMessage ?? _localizer["ErrorTunnelFailed"]);
        }

        return result;
    }

    /// <summary>
    /// Converts a gateway DTO to SSH connection parameters, decrypting the password if present.
    /// </summary>
    private static SshConnectionParams BuildGatewayParams(SshGatewayDto gateway)
    {
        string? password = null;
        if (!string.IsNullOrEmpty(gateway.SshPasswordEncrypted))
        {
            password = DecryptPassword(gateway.SshPasswordEncrypted);
        }

        return new SshConnectionParams
        {
            Host = gateway.Host,
            Port = gateway.Port,
            Username = gateway.User,
            KeyPath = string.IsNullOrWhiteSpace(gateway.KeyPath) ? null : gateway.KeyPath,
            Password = password
        };
    }

    /// <summary>
    /// Decrypts a DPAPI-encrypted password string. Returns null on failure.
    /// </summary>
    private static string? DecryptPassword(string encryptedBase64)
    {
        if (string.IsNullOrWhiteSpace(encryptedBase64))
        {
            return null;
        }

        try
        {
            return DpapiProvider.Unprotect(encryptedBase64);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Immutable result of a connection attempt.
/// </summary>
/// <param name="Success">Whether the connection was established.</param>
/// <param name="ErrorMessage">Error description on failure; null on success.</param>
/// <param name="Session">
/// The session object on success: <see cref="SshShellSession"/> for SSH,
/// <see cref="SftpBrowser"/> for SFTP, or the <see cref="RdpServerDto"/> for RDP
/// (the View layer creates the ActiveX host). Null on failure.
/// </param>
public record ConnectionResult(bool Success, string? ErrorMessage, object? Session);
