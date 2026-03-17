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

using System.IO;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;
using Heimdall.Ssh;
using Heimdall.Ssh.Pageant;
using Heimdall.Ssh.Plink;

namespace Heimdall.App.Services;

public partial class ConnectionService
{
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
        Core.Logging.FileLogger.Info($"EstablishTunnelAsync: serverId={serverId} gatewayId={gatewayId} target={remoteHost}:{remotePort} localPort={localPort}");

        // Reuse existing tunnel if one is already active on the same local port
        var existingTunnels = _tunnelManager.GetActiveTunnels();
        var existing = existingTunnels.FirstOrDefault(t =>
            t.LocalPort == localPort &&
            t.RemoteHost == remoteHost &&
            t.RemotePort == remotePort &&
            t.IsAlive);

        if (existing is not null)
        {
            Core.Logging.FileLogger.Info(
                $"Reusing existing tunnel on port {localPort} for {serverId}");
            _connectionSm.SetTunnelInfo(serverId, localPort, 0);
            _connectionSm.TryTransition(serverId, Core.Models.ConnectionState.EstablishingTunnel);
            _connectionSm.TryTransition(serverId, Core.Models.ConnectionState.TunnelEstablished);
            return new TunnelResult(true, existing, null, null);
        }

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

        if (chain.Count == 1 && SshConnectionFactory.RequiresPageantFallback(chain[0]))
        {
            Core.Logging.FileLogger.Info(
                $"EstablishTunnelAsync: using plink fallback for {serverId} via {chain[0].Host}:{chain[0].Port}");

            return await EstablishPlinkTunnelAsync(
                    serverId,
                    chain[0],
                    remoteHost,
                    remotePort,
                    localPort,
                    settings,
                    ct)
                .ConfigureAwait(false);
        }

        // Open tunnel (single-hop or chained)
        TunnelResult result;
        if (chain.Count == 1)
        {
            result = await _tunnelManager.OpenTunnelAsync(
                chain[0], remoteHost, remotePort, localPort, ct, _hostKeyStore)
                .ConfigureAwait(false);
        }
        else
        {
            result = await _tunnelManager.OpenChainedTunnelAsync(
                chain, remoteHost, remotePort, localPort, ct, _hostKeyStore)
                .ConfigureAwait(false);
        }

        if (!result.Success
            && chain.Count == 1
            && result.FailureCode is SshFailureCode.AuthRejected or SshFailureCode.KeyRejected
            && PageantClient.IsAvailable())
        {
            Core.Logging.FileLogger.Info(
                $"SSH.NET auth failed, falling back to Plink: {result.ErrorMessage}");

            return await EstablishPlinkTunnelAsync(
                    serverId,
                    chain[0],
                    remoteHost,
                    remotePort,
                    localPort,
                    settings,
                    ct)
                .ConfigureAwait(false);
        }

        if (result.Success)
        {
            Core.Logging.FileLogger.Info($"Tunnel established for {serverId} on port {localPort}");
            _connectionSm.SetTunnelInfo(serverId, localPort, 0);
            _connectionSm.TryTransition(serverId, Core.Models.ConnectionState.TunnelEstablished);
        }
        else
        {
            Core.Logging.FileLogger.Error($"Tunnel failed for {serverId}: {result.ErrorMessage}");
            _connectionSm.SetError(serverId, result.ErrorMessage ?? _localizer["ErrorTunnelFailed"]);
        }

        return result;
    }

    private async Task<TunnelResult> EstablishPlinkTunnelAsync(
        string serverId,
        SshConnectionParams gatewayParams,
        string remoteHost,
        int remotePort,
        int localPort,
        AppSettings settings,
        CancellationToken ct)
    {
        var plinkPath = ResolvePlinkPath(settings.PlinkPath);
        if (string.IsNullOrWhiteSpace(plinkPath) || !File.Exists(plinkPath))
        {
            var message = "Plink not found. Set the path in Settings.";
            _connectionSm.SetError(serverId, message);
            return new TunnelResult(false, null, message, SshFailureCode.Unknown);
        }

        var runner = new PlinkTunnelRunner();
        var result = await runner.StartAsync(
                plinkPath,
                gatewayParams.Host,
                gatewayParams.Port,
                gatewayParams.Username,
                gatewayParams.KeyPath,
                gatewayParams.Password,
                remoteHost,
                remotePort,
                localPort,
                ct)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            _connectionSm.SetError(serverId, result.ErrorMessage ?? _localizer["ErrorTunnelFailed"]);
            runner.Dispose();
            return new TunnelResult(false, null, result.ErrorMessage, result.FailureCode);
        }

        var tunnelInfo = new TunnelInfo(
            gatewayParams.Host,
            localPort,
            remoteHost,
            remotePort,
            DateTime.UtcNow,
            IsAlive: true);

        if (!_tunnelManager.TryRegisterExternalTunnel(tunnelInfo, runner, () => runner.IsRunning))
        {
            runner.Dispose();
            const string duplicateMessage = "The tunnel local port was claimed concurrently.";
            _connectionSm.SetError(serverId, duplicateMessage);
            return new TunnelResult(false, null, duplicateMessage, SshFailureCode.PortInUse);
        }

        _connectionSm.SetTunnelInfo(serverId, localPort, runner.ProcessId ?? 0);
        _connectionSm.TryTransition(serverId, Core.Models.ConnectionState.TunnelEstablished);
        Core.Logging.FileLogger.Info(
            $"Plink tunnel established for {serverId} on port {localPort} (pid={runner.ProcessId?.ToString() ?? "unknown"})");

        return new TunnelResult(true, tunnelInfo, null, null);
    }
}
