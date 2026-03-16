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
using Heimdall.Core.Security;
using Heimdall.Core.StateMachine;
using Heimdall.Sftp;
using Heimdall.Ssh;
using Heimdall.Ssh.Pageant;
using Heimdall.Ssh.Plink;

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

        Core.Logging.FileLogger.Info($"ConnectRdpAsync: {server.DisplayName} ({server.RemoteServer}:{server.RemotePort}) Gateway={server.SshGatewayId ?? "none"} Direct={server.UseDirectConnection}");
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

        var rdpMode = server.RdpMode ?? "External";
        Core.Logging.FileLogger.Info($"RDP mode: {rdpMode}");

        if (string.Equals(rdpMode, "Embedded", StringComparison.OrdinalIgnoreCase))
        {
            // Embedded RDP will be handled by the View layer (ActiveX in WindowsFormsHost).
            // Return the server DTO so the ViewModel can create an embedded session tab.
            return new ConnectionResult(true, null, server);
        }

        // External mode: launch mstsc.exe
        try
        {
            var rdpHost = server.UseDirectConnection ? server.RemoteServer : "127.0.0.1";
            var rdpPort = server.UseDirectConnection ? server.RemotePort : server.LocalPort;
            string? rdpPassword = null;

            // Store credentials in Windows Credential Manager for mstsc auto-login
            if (!string.IsNullOrEmpty(server.RdpUsername) && !string.IsNullOrEmpty(server.RdpPasswordEncrypted))
            {
                try
                {
                    rdpPassword = DpapiProvider.Unprotect(server.RdpPasswordEncrypted);
                    var credTarget = $"TERMSRV/{rdpHost}";
                    Heimdall.Rdp.CredentialManagerHelper.WriteDomainCredential(credTarget, server.RdpUsername, rdpPassword, out _);
                    Core.Logging.FileLogger.Info($"RDP credentials stored for {credTarget}");
                }
                catch (Exception credEx)
                {
                    Core.Logging.FileLogger.Warn($"Failed to store RDP credentials: {credEx.Message}");
                }
            }

            // Generate and launch .rdp file
            var rdpFile = Path.Combine(Path.GetTempPath(), $"heimdall_{server.Id}_{Guid.NewGuid():N}.rdp");
            var rdpContent = Heimdall.Rdp.RdpFileGenerator.Generate(new Heimdall.Rdp.RdpFileOptions
            {
                Host = rdpHost,
                Port = rdpPort,
                Username = server.RdpUsername,
                FullScreen = false,
                AdminMode = false,
                Redirections = new Heimdall.Rdp.RdpRedirectionOptions
                {
                    Clipboard = server.RdpRedirectClipboard,
                    Drives = server.RdpRedirectDrives,
                    Printers = server.RdpRedirectPrinters,
                    Nla = server.RdpNla
                }
            });
            await File.WriteAllTextAsync(rdpFile, rdpContent, ct).ConfigureAwait(false);

            var mstscProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"\"{rdpFile}\"",
                UseShellExecute = false
            });

            var mstscPid = mstscProcess?.Id ?? 0;
            Core.Logging.FileLogger.Info($"Launched mstsc.exe PID={mstscPid} for {server.DisplayName} ({rdpHost}:{rdpPort})");
            _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.Connected);

            // Start CredUI autofill watcher for the external mstsc process
            if (!string.IsNullOrEmpty(rdpPassword) && mstscPid > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var filled = await Heimdall.Rdp.CredentialAutofill.WaitAndFillAsync(
                            mstscPid, rdpHost, rdpPassword,
                            TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                        if (!filled)
                            Core.Logging.FileLogger.Warn($"External RDP CredUI autofill timed out for {server.DisplayName}");
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Core.Logging.FileLogger.Warn($"External RDP CredUI autofill failed: {ex.Message}");
                    }
                }, ct);
            }

            // Clean up .rdp file after delay
            _ = Task.Delay(10000, ct).ContinueWith(_ =>
            {
                try { File.Delete(rdpFile); } catch { }
            }, TaskScheduler.Default);

            return new ConnectionResult(true, null, null);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error("RDP launch failed", ex);
            return new ConnectionResult(false, ex.Message, null);
        }
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

        // Check if we need Plink fallback (Pageant-only auth)
        if (SshConnectionFactory.RequiresPageantFallback(sshParams))
        {
            Core.Logging.FileLogger.Info($"SSH using Plink fallback (Pageant) for {server.DisplayName}");
            return await ConnectSshViaPlinkAsync(server, settings, targetHost, targetPort, ct);
        }

        // Try SSH.NET first
        var session = new SshShellSession();
        try
        {
            await session.ConnectAsync(sshParams, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            session.Dispose();
            var failure = FailureClassifier.Classify(ex, sshParams);

            // Fallback to Plink if auth failed and Pageant is available
            if (failure.Code is SshFailureCode.AuthRejected or SshFailureCode.KeyRejected
                && Heimdall.Ssh.Pageant.PageantClient.IsAvailable())
            {
                Core.Logging.FileLogger.Info($"SSH.NET auth failed, falling back to Plink: {failure.Message}");
                return await ConnectSshViaPlinkAsync(server, settings, targetHost, targetPort, ct);
            }

            _connectionSm.SetError(server.Id, failure.Message);
            return new ConnectionResult(false, failure.Message, null);
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.Connected);
        return new ConnectionResult(true, null, session);
    }

    /// <summary>
    /// Launches plink.exe as an interactive SSH session via ConPTY.
    /// Used when SSH.NET cannot authenticate (Pageant-only auth).
    /// Returns an ITerminalSession that the embedded SSH view can use.
    /// </summary>
    private async Task<ConnectionResult> ConnectSshViaPlinkAsync(
        RdpServerDto server,
        AppSettings settings,
        string targetHost,
        int targetPort,
        CancellationToken ct)
    {
        var plinkPath = settings.PlinkPath;
        if (string.IsNullOrWhiteSpace(plinkPath) || !File.Exists(plinkPath))
        {
            var msg = "Plink not found. Set the path in Settings.";
            _connectionSm.SetError(server.Id, msg);
            return new ConnectionResult(false, msg, null);
        }

        // Build plink arguments for interactive SSH
        var args = new System.Text.StringBuilder();
        args.Append("-ssh ");
        if (!string.IsNullOrEmpty(server.SshKeyPath))
            args.Append($"-i \"{server.SshKeyPath}\" ");
        if (server.SshCompression)
            args.Append("-C ");
        if (server.SshAgentForwarding)
            args.Append("-A ");
        args.Append($"-P {targetPort} ");
        if (!string.IsNullOrEmpty(server.SshUsername))
            args.Append($"{server.SshUsername}@");
        args.Append(targetHost);

        Core.Logging.FileLogger.Info($"SSH via Plink: {plinkPath} {args}");

        var terminal = new Heimdall.Terminal.ConPty.ConPtySession();
        try
        {
            await terminal.StartAsync(plinkPath, args.ToString(), 120, 40);
            Core.Logging.FileLogger.Info($"Plink SSH session started: PID={terminal.ProcessId}");
        }
        catch (Exception ex)
        {
            terminal.Dispose();
            Core.Logging.FileLogger.Error("Plink SSH launch failed", ex);
            _connectionSm.SetError(server.Id, ex.Message);
            return new ConnectionResult(false, ex.Message, null);
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.Connected);
        return new ConnectionResult(true, null, terminal);
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
        Core.Logging.FileLogger.Info($"EstablishTunnelAsync: serverId={serverId} gatewayId={gatewayId} target={remoteHost}:{remotePort} localPort={localPort}");
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
                chain[0], remoteHost, remotePort, localPort, ct)
                .ConfigureAwait(false);
        }
        else
        {
            result = await _tunnelManager.OpenChainedTunnelAsync(
                chain, remoteHost, remotePort, localPort, ct)
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
        var plinkPath = settings.PlinkPath;
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
