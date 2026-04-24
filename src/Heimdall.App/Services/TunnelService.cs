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
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Ssh;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;
using Heimdall.Ssh.Agents;
using Heimdall.Ssh.Plink;

namespace Heimdall.App.Services;

/// <summary>
/// Resolves SSH gateway chains and establishes reusable tunnels for protocol handlers.
/// </summary>
public sealed class TunnelService : ITunnelService
{
    private readonly TunnelManager _tunnelManager;
    private readonly HostKeyStore _hostKeyStore;
    private readonly IHostKeyTrustService _hostKeyTrustService;
    private readonly ConnectionStateMachine _connectionSm;
    private readonly LocalizationManager _localizer;
    private readonly IHostKeyVerifier _hostKeyVerifier;

    private AppSettings? _currentSettings;

    public TunnelService(
        TunnelManager tunnelManager,
        HostKeyStore hostKeyStore,
        IHostKeyTrustService hostKeyTrustService,
        ConnectionStateMachine connectionSm,
        LocalizationManager localizer,
        IHostKeyVerifier hostKeyVerifier)
    {
        _tunnelManager = tunnelManager;
        _hostKeyStore = hostKeyStore;
        _hostKeyTrustService = hostKeyTrustService;
        _connectionSm = connectionSm;
        _localizer = localizer;
        _hostKeyVerifier = hostKeyVerifier;
    }

    public void UpdateSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _currentSettings = settings;
    }

    /// <summary>
    /// Checks whether the server requires a tunnel and establishes it if needed.
    /// Returns the resolved host and port to connect to.
    /// </summary>
    public async Task<(bool Success, bool UsesTunnel, string Host, int Port, string? ErrorMessage)>
        SetupTunnelIfNeededAsync(
            ServerProfileDto server,
            int remotePort,
            AppSettings settings,
            CancellationToken ct)
    {
        if (server.UseDirectConnection || string.IsNullOrEmpty(server.SshGatewayId))
        {
            return (true, false, server.RemoteServer, remotePort, null);
        }

        var tunnelResult = await EstablishTunnelAsync(
                server.Id,
                server.SshGatewayId,
                server.RemoteServer,
                remotePort,
                server.LocalPort,
                settings,
                ct,
                server.SocksProxyPort,
                server.RemoteBindPort,
                server.RemoteLocalPort)
            .ConfigureAwait(false);

        if (!tunnelResult.Success)
        {
            return (false, false, string.Empty, 0, tunnelResult.ErrorMessage);
        }

        var localPort = tunnelResult.Tunnel?.LocalPort ?? server.LocalPort;
        return (true, true, "127.0.0.1", localPort, null);
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
        CancellationToken ct,
        int socksProxyPort = 0,
        int remoteBindPort = 0,
        int remoteLocalPort = 0)
    {
        Core.Logging.FileLogger.Info(
            $"Establish tunnel: serverId={serverId} gatewayId={gatewayId} target={remoteHost}:{remotePort} requestedPort={localPort}");

        var existingTunnels = _tunnelManager.GetActiveTunnels();
        var existing = existingTunnels.FirstOrDefault(t =>
            t.RemoteHost == remoteHost &&
            t.RemotePort == remotePort &&
            t.IsAlive);

        if (existing is not null)
        {
            Core.Logging.FileLogger.Info(
                $"Reusing existing tunnel on port {existing.LocalPort} for {serverId}");
            _tunnelManager.AddReference(existing.LocalPort);
            _connectionSm.SetTunnelInfo(serverId, existing.LocalPort, 0);
            _connectionSm.TryTransition(serverId, Core.Models.ConnectionState.EstablishingTunnel);
            _connectionSm.TryTransition(serverId, Core.Models.ConnectionState.TunnelEstablished);
            return new TunnelResult(true, existing, null, null);
        }

        _connectionSm.TryTransition(serverId, Core.Models.ConnectionState.EstablishingTunnel);

        localPort = _tunnelManager.AllocatePort(localPort);
        Core.Logging.FileLogger.Info($"Allocated tunnel port: {localPort}");

        List<SshConnectionParams> chain;

        try
        {
            chain = GatewayChainResolver.ResolveChain(
                gatewayId,
                settings.SshGateways,
                ConnectionHelpers.DecryptPassword,
                sshAgentPreference: settings.SshAgentPreference);
        }
        catch (Exception ex)
        {
            _connectionSm.SetError(serverId, ex.Message);
            return new TunnelResult(false, null, ex.Message, SshFailureCode.Unknown);
        }

        var agentRegistry = SshAgentRegistry.CreateDefault(settings.SshAgentPreference);
        if (chain.Any(hop => hop.AgentForwarding)
            && !agentRegistry.HasPlinkCompatibleAgent()
            && agentRegistry.HasAnyNonPlinkAgent())
        {
            var message = _localizer["ErrorPlinkOpenSshAgentUnsupported"];
            _connectionSm.SetError(serverId, message);
            return new TunnelResult(false, null, message, SshFailureCode.PageantKeyUnavailable);
        }

        var preflight = AuthPreflightChecker.Check(chain[0], isTunnelMode: true);
        if (!preflight.Success)
        {
            var msg = ResolvePreflightMessage(preflight.Message);
            _connectionSm.SetError(serverId, msg);
            return new TunnelResult(false, null, msg, preflight.FailureCode);
        }

        TunnelResult result;
        if (chain.Count == 1)
        {
            var keepAlive = _currentSettings?.SshKeepAliveIntervalSeconds ?? 30;
            result = await _tunnelManager.OpenTunnelAsync(
                    chain[0],
                    remoteHost,
                    remotePort,
                    localPort,
                    ct,
                    hostKeyStore: _hostKeyStore,
                    verifier: _hostKeyVerifier,
                    keepAliveIntervalSeconds: keepAlive,
                    socksProxyPort: socksProxyPort,
                    remoteBindPort: remoteBindPort,
                    remoteLocalPort: remoteLocalPort)
                .ConfigureAwait(false);
        }
        else
        {
            result = await _tunnelManager.OpenChainedTunnelAsync(
                    chain,
                    remoteHost,
                    remotePort,
                    localPort,
                    ct,
                    hostKeyStore: _hostKeyStore,
                    verifier: _hostKeyVerifier,
                    socksProxyPort: socksProxyPort,
                    remoteBindPort: remoteBindPort,
                    remoteLocalPort: remoteLocalPort)
                .ConfigureAwait(false);
        }

        if (!result.Success
            && chain.Count == 1
            && result.FailureCode is SshFailureCode.AuthRejected
                or SshFailureCode.KeyRejected
                or SshFailureCode.PassphraseRejected
            && SshAgentRegistry.CreateDefault(settings.SshAgentPreference).HasPlinkCompatibleAgent())
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

        if (!result.Success
            && chain.Count == 1
            && result.FailureCode is SshFailureCode.AuthRejected
                or SshFailureCode.KeyRejected
                or SshFailureCode.PassphraseRejected)
        {
            var registry = SshAgentRegistry.CreateDefault(settings.SshAgentPreference);
            if (!registry.HasPlinkCompatibleAgent() && registry.HasAnyNonPlinkAgent())
            {
                var message = _localizer["ErrorPlinkOpenSshAgentUnsupported"];
                _connectionSm.SetError(serverId, message);
                return new TunnelResult(false, null, message, result.FailureCode);
            }
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
        var plinkPath = ConnectionHelpers.ResolvePlinkPath(settings.PlinkPath);
        if (string.IsNullOrWhiteSpace(plinkPath) || !File.Exists(plinkPath))
        {
            var message = _localizer["ErrorPlinkNotConfigured"];
            _connectionSm.SetError(serverId, message);
            return new TunnelResult(false, null, message, SshFailureCode.Unknown);
        }

        var storedFingerprint = _hostKeyTrustService.GetEntry(gatewayParams.Host, gatewayParams.Port)?.Fingerprint;
        string? fingerprint = storedFingerprint;

        if (!string.IsNullOrWhiteSpace(storedFingerprint))
        {
            var verifyPresentation = await ProbeHostKeyPresentationAsync(
                    plinkPath,
                    gatewayParams.Host,
                    gatewayParams.Port,
                    gatewayParams.Username,
                    settings,
                    ct)
                .ConfigureAwait(false);

            if (verifyPresentation is not null
                && !string.Equals(
                    verifyPresentation.Fingerprint,
                    storedFingerprint,
                    StringComparison.Ordinal))
            {
                var decision = await _hostKeyVerifier.VerifyAsync(
                        gatewayParams.Host,
                        gatewayParams.Port,
                        verifyPresentation.Algorithm,
                        verifyPresentation.Fingerprint,
                        storedFingerprint,
                        ct)
                    .ConfigureAwait(false);

                if (decision == HostKeyDecision.Accept)
                {
                    _hostKeyTrustService.Trust(
                        gatewayParams.Host,
                        gatewayParams.Port,
                        verifyPresentation.Fingerprint,
                        verifyPresentation.Algorithm,
                        HostKeySource.UserConfirmed);
                    fingerprint = verifyPresentation.Fingerprint;
                    Core.Logging.FileLogger.Warn(
                        $"User accepted replacement tunnel host key for {gatewayParams.Host}:{gatewayParams.Port}: {verifyPresentation.Fingerprint}");
                }
                else
                {
                    var message = BuildHostKeyMismatchMessage(
                        storedFingerprint,
                        verifyPresentation.Fingerprint);
                    _connectionSm.SetError(serverId, message);
                    return new TunnelResult(false, null, message, SshFailureCode.HostKeyMismatch);
                }
            }
        }
        else
        {
            var probedPresentation = await ProbeHostKeyPresentationAsync(
                    plinkPath,
                    gatewayParams.Host,
                    gatewayParams.Port,
                    gatewayParams.Username,
                    settings,
                    ct)
                .ConfigureAwait(false);

            if (probedPresentation is not null)
            {
                var decision = await _hostKeyVerifier.VerifyAsync(
                        gatewayParams.Host,
                        gatewayParams.Port,
                        probedPresentation.Algorithm,
                        probedPresentation.Fingerprint,
                        storedFingerprint: null,
                        ct)
                    .ConfigureAwait(false);

                if (decision == HostKeyDecision.Accept)
                {
                    _hostKeyTrustService.Trust(
                        gatewayParams.Host,
                        gatewayParams.Port,
                        probedPresentation.Fingerprint,
                        probedPresentation.Algorithm,
                        HostKeySource.UserConfirmed);
                    fingerprint = probedPresentation.Fingerprint;
                    Core.Logging.FileLogger.Info(
                        $"User trusted tunnel host key for {gatewayParams.Host}:{gatewayParams.Port} fingerprint={probedPresentation.Fingerprint}");
                }
                else
                {
                    var message = BuildCancelledMessage();
                    _connectionSm.SetError(serverId, message);
                    return new TunnelResult(false, null, message, SshFailureCode.Cancelled);
                }
            }
        }

        var runner = new PlinkTunnelRunner(
            _currentSettings?.PlinkPortCheckIntervalMs ?? 2000,
            _currentSettings?.PlinkKillGracePeriodMs ?? 2000,
            _currentSettings?.PlinkStderrReadTimeoutMs ?? 10000);
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
                fingerprint,
                ct,
                gatewayParams.KeyPassphrase,
                _localizer["ErrorPlinkPassphraseUnsupported"])
            .ConfigureAwait(false);

        if (!result.Success)
        {
            var errorMsg = result.ErrorMessage ?? _localizer["ErrorTunnelFailed"];
            Core.Logging.FileLogger.Error(
                $"Plink tunnel failed for {serverId} via {gatewayParams.Host}:{gatewayParams.Port}: {errorMsg}");
            _connectionSm.SetError(serverId, errorMsg);
            runner.Dispose();
            return new TunnelResult(false, null, errorMsg, result.FailureCode);
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
            var duplicateMessage = _localizer["ErrorTunnelPortConcurrent"];
            _connectionSm.SetError(serverId, duplicateMessage);
            return new TunnelResult(false, null, duplicateMessage, SshFailureCode.PortInUse);
        }

        _connectionSm.SetTunnelInfo(serverId, localPort, runner.ProcessId ?? 0);
        _connectionSm.TryTransition(serverId, Core.Models.ConnectionState.TunnelEstablished);
        Core.Logging.FileLogger.Info(
            $"Plink tunnel established for {serverId} on port {localPort} (pid={runner.ProcessId?.ToString() ?? "unknown"})");

        return new TunnelResult(true, tunnelInfo, null, null);
    }

    private Task<PlinkHostKeyPresentation?> ProbeHostKeyPresentationAsync(
        string plinkPath,
        string host,
        int port,
        string? username,
        AppSettings settings,
        CancellationToken ct)
    {
        return PlinkHostKeyProbe.ProbeAsync(
            plinkPath,
            host,
            port,
            username,
            settings.HostKeyProbeTimeoutMs,
            ct);
    }

    private string BuildHostKeyMismatchMessage(
        string storedFingerprint,
        string presentedFingerprint)
    {
        var message = _localizer["ErrorHostKeyMismatch"];
        if (string.Equals(message, "ErrorHostKeyMismatch", StringComparison.Ordinal))
        {
            message = "SSH host key mismatch \u2014 possible MITM. Stored fingerprint differs from server-presented fingerprint.";
        }

        var detail = _localizer.Format(
            "ErrorHostKeyMismatchDetail",
            storedFingerprint,
            presentedFingerprint);
        if (string.Equals(detail, "ErrorHostKeyMismatchDetail", StringComparison.Ordinal))
        {
            detail = $"Stored: {storedFingerprint}. Presented: {presentedFingerprint}.";
        }

        return $"{message} {detail}";
    }

    private string BuildCancelledMessage()
    {
        var message = _localizer["ErrorSshCancelled"];
        return string.Equals(message, "ErrorSshCancelled", StringComparison.Ordinal)
            ? "Connection was cancelled."
            : message;
    }

    private string ResolvePreflightMessage(string? messageOrKey)
    {
        if (string.IsNullOrWhiteSpace(messageOrKey))
        {
            return _localizer["ErrorPreflightFailed"];
        }

        var localized = _localizer[messageOrKey];
        return string.Equals(localized, messageOrKey, StringComparison.Ordinal)
            ? messageOrKey
            : localized;
    }
}
