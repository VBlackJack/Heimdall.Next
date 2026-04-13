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

using System.Collections.Concurrent;
using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Heimdall.Ssh;

/// <summary>
/// Manages the lifecycle of SSH port-forwarding tunnels. Thread-safe.
/// Replaces the legacy plink-based tunnel management with in-process SSH.NET tunnels.
/// </summary>
public sealed class TunnelManager : IDisposable
{
    private readonly ConcurrentDictionary<int, TunnelSession> _activeTunnels = new();
    private readonly ConcurrentDictionary<int, ExternalTunnelSession> _externalTunnels = new();
    private readonly ConcurrentDictionary<int, int> _refCounts = new();
    private readonly object _registryLock = new();
    private bool _disposed;

    /// <summary>Raised when a tunnel is successfully opened.</summary>
    public event Action<TunnelInfo>? TunnelOpened;

    /// <summary>Raised when a tunnel is closed (localPort, optional error message).</summary>
    public event Action<int, string?>? TunnelClosed;

    /// <summary>
    /// Increments the reference count for a tunnel on the specified local port.
    /// Call this when a new session begins using an existing tunnel.
    /// </summary>
    /// <param name="localPort">Local port of the tunnel to reference.</param>
    public void AddReference(int localPort)
    {
        _refCounts.AddOrUpdate(localPort, 1, (_, current) => current + 1);
    }

    /// <summary>
    /// Decrements the reference count for a tunnel on the specified local port.
    /// Returns true if the count has reached zero (or no refs were tracked),
    /// meaning the caller should close the tunnel. Returns false if other
    /// sessions still reference the tunnel.
    /// </summary>
    /// <param name="localPort">Local port of the tunnel to release.</param>
    /// <returns>True if the tunnel should be closed; false if still in use.</returns>
    public bool ReleaseReference(int localPort)
    {
        var newCount = _refCounts.AddOrUpdate(localPort, 0, (_, current) => Math.Max(0, current - 1));

        if (newCount <= 0)
        {
            _refCounts.TryRemove(localPort, out _);
            CloseTunnel(localPort);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Opens a single-hop SSH port-forwarding tunnel through the specified gateway.
    /// Binds <paramref name="localPort"/> on localhost and forwards traffic to
    /// <paramref name="remoteHost"/>:<paramref name="remotePort"/> via the gateway.
    /// </summary>
    /// <param name="gatewayParams">SSH connection parameters for the gateway.</param>
    /// <param name="remoteHost">Target host on the remote network.</param>
    /// <param name="remotePort">Target port on the remote network.</param>
    /// <param name="localPort">Local port to bind for forwarding.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <param name="hostKeyStore">Optional TOFU host key store for server verification.</param>
    /// <returns>Result indicating success or structured failure.</returns>
    public async Task<TunnelResult> OpenTunnelAsync(
        SshConnectionParams gatewayParams,
        string remoteHost,
        int remotePort,
        int localPort,
        CancellationToken cancellationToken = default,
        HostKeyStore? hostKeyStore = null,
        int keepAliveIntervalSeconds = 30,
        int socksProxyPort = 0,
        int remoteBindPort = 0,
        int remoteLocalPort = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(gatewayParams);

        if (IsPortTracked(localPort))
        {
            return new TunnelResult(false, null, $"Local port {localPort} is already in use by an existing tunnel.", SshFailureCode.PortInUse);
        }

        SshClient? client = null;
        ForwardedPortLocal? forwardedPort = null;
        ForwardedPortDynamic? dynamicPort = null;
        ForwardedPortRemote? remotePortFwd = null;

        try
        {
            var connectionInfo = SshConnectionFactory.Create(gatewayParams);
            client = new SshClient(connectionInfo)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(keepAliveIntervalSeconds)
            };

            if (hostKeyStore is not null)
            {
                SshConnectionFactory.AttachHostKeyVerification(
                    client, gatewayParams.Host, gatewayParams.Port, hostKeyStore);
            }

            client.ErrorOccurred += (_, args) =>
                Core.Logging.FileLogger.Error($"SSH tunnel error on port {localPort}: {args.Exception.Message}");

            await using var connectReg = cancellationToken.Register(
                () => { try { client.Disconnect(); } catch (Exception ex) { Core.Logging.FileLogger.Debug("Client disconnect on cancel suppressed", ex); } });
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                client.Connect();
            }, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            forwardedPort = new ForwardedPortLocal("127.0.0.1", (uint)localPort, remoteHost, (uint)remotePort);

            forwardedPort.Exception += (_, args) =>
                Core.Logging.FileLogger.Error($"SSH forwarded port {localPort} exception: {args.Exception.Message}");

            client.AddForwardedPort(forwardedPort);
            forwardedPort.Start();

            if (socksProxyPort > 0)
            {
                dynamicPort = new ForwardedPortDynamic("127.0.0.1", (uint)socksProxyPort);
                client.AddForwardedPort(dynamicPort);
                dynamicPort.Start();
                Core.Logging.FileLogger.Info(
                    $"SOCKS5 proxy started on 127.0.0.1:{socksProxyPort}");
            }

            if (remoteBindPort > 0)
            {
                int localFwd = remoteLocalPort > 0 ? remoteLocalPort : remoteBindPort;
                remotePortFwd = new ForwardedPortRemote(
                    "127.0.0.1", (uint)remoteBindPort,
                    "127.0.0.1", (uint)localFwd);
                client.AddForwardedPort(remotePortFwd);
                remotePortFwd.Start();
                Core.Logging.FileLogger.Info(
                    $"Remote forward started: server:{remoteBindPort} \u2192 local:{localFwd}");
            }

            var info = new TunnelInfo(
                gatewayParams.Host,
                localPort,
                remoteHost,
                remotePort,
                DateTime.UtcNow,
                IsAlive: true)
            { SocksProxyPort = socksProxyPort, RemoteBindPort = remoteBindPort };

            var session = new TunnelSession(client, forwardedPort, info)
            { DynamicPort = dynamicPort, RemotePort = remotePortFwd };

            lock (_registryLock)
            {
                if (IsPortTracked(localPort) || !_activeTunnels.TryAdd(localPort, session))
                {
                    session.Dispose();
                    return new TunnelResult(false, null, $"Local port {localPort} was claimed concurrently.", SshFailureCode.PortInUse);
                }
            }

            AddReference(localPort);
            TunnelOpened?.Invoke(info);
            return new TunnelResult(true, info, null, null);
        }
        catch (OperationCanceledException)
        {
            try { dynamicPort?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Dynamic port dispose suppressed", cleanupEx); }
            try { remotePortFwd?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Remote port forward dispose suppressed", cleanupEx); }
            CleanupPartial(client, forwardedPort);
            return new TunnelResult(false, null, "Tunnel establishment was cancelled.", SshFailureCode.Cancelled);
        }
        catch (SshAuthenticationException ex)
        {
            try { dynamicPort?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Dynamic port dispose suppressed", cleanupEx); }
            try { remotePortFwd?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Remote port forward dispose suppressed", cleanupEx); }
            CleanupPartial(client, forwardedPort);
            return new TunnelResult(false, null, ex.Message, SshFailureCode.AuthRejected);
        }
        catch (SocketException ex)
        {
            try { dynamicPort?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Dynamic port dispose suppressed", cleanupEx); }
            try { remotePortFwd?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Remote port forward dispose suppressed", cleanupEx); }
            CleanupPartial(client, forwardedPort);
            var code = ClassifySocketException(ex);
            return new TunnelResult(false, null, ex.Message, code);
        }
        catch (SshConnectionException ex)
        {
            try { dynamicPort?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Dynamic port dispose suppressed", cleanupEx); }
            try { remotePortFwd?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Remote port forward dispose suppressed", cleanupEx); }
            CleanupPartial(client, forwardedPort);
            return new TunnelResult(false, null, ex.Message, SshFailureCode.NetworkRefused);
        }
        catch (SshException ex) when (ex.Message.Contains("port", StringComparison.OrdinalIgnoreCase))
        {
            try { dynamicPort?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Dynamic port dispose suppressed", cleanupEx); }
            try { remotePortFwd?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Remote port forward dispose suppressed", cleanupEx); }
            CleanupPartial(client, forwardedPort);
            return new TunnelResult(false, null, ex.Message, SshFailureCode.PortInUse);
        }
        catch (Exception ex)
        {
            try { dynamicPort?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Dynamic port dispose suppressed", cleanupEx); }
            try { remotePortFwd?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Remote port forward dispose suppressed", cleanupEx); }
            CleanupPartial(client, forwardedPort);
            return new TunnelResult(false, null, ex.Message, SshFailureCode.Unknown);
        }
    }

    /// <summary>
    /// Opens a multi-hop (chained) tunnel through a sequence of gateways.
    /// Each gateway in the chain forwards to the next, with the final gateway
    /// forwarding to <paramref name="remoteHost"/>:<paramref name="remotePort"/>.
    /// </summary>
    /// <param name="gatewayChain">Ordered list of gateways from root to target.</param>
    /// <param name="remoteHost">Final target host on the remote network.</param>
    /// <param name="remotePort">Final target port on the remote network.</param>
    /// <param name="localPort">Local port to bind for the outermost forwarding.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <param name="hostKeyStore">Optional TOFU host key store for server verification.</param>
    /// <returns>Result indicating success or structured failure.</returns>
    public async Task<TunnelResult> OpenChainedTunnelAsync(
        IReadOnlyList<SshConnectionParams> gatewayChain,
        string remoteHost,
        int remotePort,
        int localPort,
        CancellationToken cancellationToken = default,
        HostKeyStore? hostKeyStore = null,
        int socksProxyPort = 0,
        int remoteBindPort = 0,
        int remoteLocalPort = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(gatewayChain);

        if (gatewayChain.Count == 0)
        {
            return new TunnelResult(false, null, "Gateway chain must contain at least one gateway.", SshFailureCode.Unknown);
        }

        // Single gateway: delegate to simple tunnel
        if (gatewayChain.Count == 1)
        {
            return await OpenTunnelAsync(gatewayChain[0], remoteHost, remotePort, localPort, cancellationToken, hostKeyStore,
                    socksProxyPort: socksProxyPort, remoteBindPort: remoteBindPort, remoteLocalPort: remoteLocalPort)
                .ConfigureAwait(false);
        }

        if (IsPortTracked(localPort))
        {
            return new TunnelResult(false, null, $"Local port {localPort} is already in use by an existing tunnel.", SshFailureCode.PortInUse);
        }

        var intermediateClients = new List<SshClient>();
        var intermediatePorts = new List<ForwardedPortLocal>();
        SshClient? finalClient = null;
        ForwardedPortLocal? finalPort = null;
        ForwardedPortDynamic? dynamicPort = null;
        ForwardedPortRemote? remotePortFwd = null;

        try
        {
            // Build the chain: each hop connects to the next via a local port forward
            // Hop 0: connect to gateway[0] directly
            // Hop 1: forward through gateway[0] to gateway[1], connect to gateway[1] via local forward
            // ...
            // Final: forward through last intermediate to remoteHost:remotePort

            int nextLocalPort = GetEphemeralPort();

            // Connect to the first (root) gateway directly
            var rootInfo = SshConnectionFactory.Create(gatewayChain[0]);
            var rootClient = new SshClient(rootInfo);

            if (hostKeyStore is not null)
            {
                SshConnectionFactory.AttachHostKeyVerification(
                    rootClient, gatewayChain[0].Host, gatewayChain[0].Port, hostKeyStore);
            }

            await using var rootConnectReg = cancellationToken.Register(
                () => { try { rootClient.Disconnect(); } catch (Exception ex) { Core.Logging.FileLogger.Debug("Root client disconnect on cancel suppressed", ex); } });
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                rootClient.Connect();
            }, cancellationToken).ConfigureAwait(false);

            intermediateClients.Add(rootClient);
            SshClient currentClient = rootClient;

            // Set up intermediate hops
            for (int i = 1; i < gatewayChain.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nextGateway = gatewayChain[i];
                int intermediateLocalPort = nextLocalPort;
                nextLocalPort = (i < gatewayChain.Count - 1) ? GetEphemeralPort() : localPort;

                // Forward through current client to the next gateway's SSH port
                var intermediatePort = new ForwardedPortLocal(
                    "127.0.0.1",
                    (uint)intermediateLocalPort,
                    nextGateway.Host,
                    (uint)nextGateway.Port);
                currentClient.AddForwardedPort(intermediatePort);
                intermediatePort.Start();
                intermediatePorts.Add(intermediatePort);

                // Connect to the next gateway through the forwarded port
                var hopParams = new SshConnectionParams
                {
                    Host = "127.0.0.1",
                    Port = intermediateLocalPort,
                    Username = nextGateway.Username,
                    KeyPath = nextGateway.KeyPath,
                    Password = nextGateway.Password,
                    AgentForwarding = nextGateway.AgentForwarding,
                    Compression = nextGateway.Compression,
                    ConnectTimeout = nextGateway.ConnectTimeout
                };
                var hopInfo = SshConnectionFactory.Create(hopParams);
                var hopClient = new SshClient(hopInfo);

                if (hostKeyStore is not null)
                {
                    // Verify against the real gateway host, not 127.0.0.1
                    SshConnectionFactory.AttachHostKeyVerification(
                        hopClient, nextGateway.Host, nextGateway.Port, hostKeyStore);
                }

                await using var hopConnectReg = cancellationToken.Register(
                    () => { try { hopClient.Disconnect(); } catch (Exception ex) { Core.Logging.FileLogger.Debug("Hop client disconnect on cancel suppressed", ex); } });
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hopClient.Connect();
                }, cancellationToken).ConfigureAwait(false);

                if (i < gatewayChain.Count - 1)
                {
                    intermediateClients.Add(hopClient);
                    currentClient = hopClient;
                }
                else
                {
                    // This is the final gateway
                    finalClient = hopClient;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // The final forwarded port goes from localPort to remoteHost:remotePort
            finalPort = new ForwardedPortLocal("127.0.0.1", (uint)localPort, remoteHost, (uint)remotePort);
            finalClient!.AddForwardedPort(finalPort);
            finalPort.Start();

            if (socksProxyPort > 0)
            {
                dynamicPort = new ForwardedPortDynamic("127.0.0.1", (uint)socksProxyPort);
                finalClient.AddForwardedPort(dynamicPort);
                dynamicPort.Start();
                Core.Logging.FileLogger.Info(
                    $"SOCKS5 proxy started on 127.0.0.1:{socksProxyPort} (chained tunnel)");
            }

            if (remoteBindPort > 0)
            {
                int localFwd = remoteLocalPort > 0 ? remoteLocalPort : remoteBindPort;
                remotePortFwd = new ForwardedPortRemote(
                    "127.0.0.1", (uint)remoteBindPort,
                    "127.0.0.1", (uint)localFwd);
                finalClient.AddForwardedPort(remotePortFwd);
                remotePortFwd.Start();
                Core.Logging.FileLogger.Info(
                    $"Remote forward started: server:{remoteBindPort} \u2192 local:{localFwd} (chained tunnel)");
            }

            var tunnelInfo = new TunnelInfo(
                gatewayChain[^1].Host,
                localPort,
                remoteHost,
                remotePort,
                DateTime.UtcNow,
                IsAlive: true)
            { SocksProxyPort = socksProxyPort, RemoteBindPort = remoteBindPort };

            var session = new TunnelSession(
                finalClient,
                finalPort,
                tunnelInfo,
                intermediateClients,
                intermediatePorts)
            { DynamicPort = dynamicPort, RemotePort = remotePortFwd };

            lock (_registryLock)
            {
                if (IsPortTracked(localPort) || !_activeTunnels.TryAdd(localPort, session))
                {
                    session.Dispose();
                    return new TunnelResult(false, null, $"Local port {localPort} was claimed concurrently.", SshFailureCode.PortInUse);
                }
            }

            AddReference(localPort);
            TunnelOpened?.Invoke(tunnelInfo);
            return new TunnelResult(true, tunnelInfo, null, null);
        }
        catch (OperationCanceledException)
        {
            try { dynamicPort?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Dynamic port dispose suppressed", cleanupEx); }
            try { remotePortFwd?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Remote port forward dispose suppressed", cleanupEx); }
            CleanupChainPartial(finalClient, finalPort, intermediateClients, intermediatePorts);
            return new TunnelResult(false, null, "Chained tunnel establishment was cancelled.", SshFailureCode.Cancelled);
        }
        catch (SshAuthenticationException ex)
        {
            try { dynamicPort?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Dynamic port dispose suppressed", cleanupEx); }
            try { remotePortFwd?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Remote port forward dispose suppressed", cleanupEx); }
            CleanupChainPartial(finalClient, finalPort, intermediateClients, intermediatePorts);
            return new TunnelResult(false, null, ex.Message, SshFailureCode.AuthRejected);
        }
        catch (SocketException ex)
        {
            try { dynamicPort?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Dynamic port dispose suppressed", cleanupEx); }
            try { remotePortFwd?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Remote port forward dispose suppressed", cleanupEx); }
            CleanupChainPartial(finalClient, finalPort, intermediateClients, intermediatePorts);
            var code = ClassifySocketException(ex);
            return new TunnelResult(false, null, ex.Message, code);
        }
        catch (Exception ex)
        {
            try { dynamicPort?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Dynamic port dispose suppressed", cleanupEx); }
            try { remotePortFwd?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Remote port forward dispose suppressed", cleanupEx); }
            CleanupChainPartial(finalClient, finalPort, intermediateClients, intermediatePorts);
            return new TunnelResult(false, null, ex.Message, SshFailureCode.Unknown);
        }
    }

    /// <summary>
    /// Closes and removes the tunnel bound to the specified local port.
    /// If the tunnel has a ref count greater than zero, the tunnel is kept alive.
    /// Use <see cref="ReleaseReference"/> for ref-counted teardown.
    /// </summary>
    /// <param name="localPort">Local port of the tunnel to close.</param>
    public void CloseTunnel(int localPort)
    {
        // If there are still active references, do not tear down
        if (_refCounts.TryGetValue(localPort, out var count) && count > 0)
        {
            return;
        }

        // No refs remaining — clean up the ref count entry
        _refCounts.TryRemove(localPort, out _);

        if (_activeTunnels.TryRemove(localPort, out var session))
        {
            string? error = null;
            try
            {
                session.Dispose();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            TunnelClosed?.Invoke(localPort, error);
            return;
        }

        if (_externalTunnels.TryRemove(localPort, out var externalSession))
        {
            string? error = null;
            try
            {
                externalSession.Dispose();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            TunnelClosed?.Invoke(localPort, error);
        }
    }

    /// <summary>
    /// Forcefully closes a tunnel regardless of reference count.
    /// Use when the user explicitly requests closure from the UI.
    /// </summary>
    public void ForceCloseTunnel(int localPort)
    {
        _refCounts.TryRemove(localPort, out _);

        if (_activeTunnels.TryRemove(localPort, out var session))
        {
            string? error = null;
            try { session.Dispose(); }
            catch (Exception ex) { error = ex.Message; }
            TunnelClosed?.Invoke(localPort, error);
            return;
        }

        if (_externalTunnels.TryRemove(localPort, out var externalSession))
        {
            string? error = null;
            try { externalSession.Dispose(); }
            catch (Exception ex) { error = ex.Message; }
            TunnelClosed?.Invoke(localPort, error);
        }
    }

    /// <summary>Closes all active tunnels (force, ignores ref counts).</summary>
    public void CloseAllTunnels()
    {
        foreach (var localPort in _activeTunnels.Keys.Concat(_externalTunnels.Keys).Distinct().ToList())
        {
            ForceCloseTunnel(localPort);
        }
    }

    /// <summary>Returns true if a tunnel is active on the specified local port.</summary>
    public bool HasTunnel(int localPort) => IsPortTracked(localPort);

    /// <summary>Returns tunnel info for the specified local port, or null if not found.</summary>
    public TunnelInfo? GetTunnel(int localPort)
    {
        if (_activeTunnels.TryGetValue(localPort, out var session))
        {
            // Return a fresh snapshot with current alive status
            return session.Info with { IsAlive = session.Client.IsConnected };
        }

        if (_externalTunnels.TryGetValue(localPort, out var externalSession))
        {
            return externalSession.Info with { IsAlive = externalSession.IsAlive };
        }

        return null;
    }

    /// <summary>Returns snapshots of all active tunnels.</summary>
    public IReadOnlyList<TunnelInfo> GetActiveTunnels()
    {
        return _activeTunnels.Values
            .Select(s => s.Info with { IsAlive = s.Client.IsConnected })
            .Concat(_externalTunnels.Values.Select(
                s => s.Info with { IsAlive = s.IsAlive }))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Registers an externally managed tunnel, such as a plink.exe process,
    /// so it participates in normal tunnel listing and cleanup.
    /// </summary>
    public bool TryRegisterExternalTunnel(
        TunnelInfo info,
        IDisposable tunnelHandle,
        Func<bool> isAlive)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(tunnelHandle);
        ArgumentNullException.ThrowIfNull(isAlive);

        var session = new ExternalTunnelSession(info, tunnelHandle, isAlive);

        lock (_registryLock)
        {
            if (IsPortTracked(info.LocalPort) || !_externalTunnels.TryAdd(info.LocalPort, session))
            {
                session.Dispose();
                return false;
            }
        }

        AddReference(info.LocalPort);
        TunnelOpened?.Invoke(info);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CloseAllTunnels();
    }

    private bool IsPortTracked(int localPort)
    {
        return _activeTunnels.ContainsKey(localPort) || _externalTunnels.ContainsKey(localPort);
    }

    /// <summary>
    /// Allocates an available local port for tunnel forwarding.
    /// If the requested port is available and not tracked, returns it.
    /// Otherwise, finds a free ephemeral port via the OS.
    /// </summary>
    /// <param name="preferredPort">Preferred port from the server profile.</param>
    /// <returns>An available local port number.</returns>
    public int AllocatePort(int preferredPort = 0)
    {
        if (preferredPort > 0 && !IsPortTracked(preferredPort))
        {
            // Verify the preferred port is not in use by another process
            try
            {
                using var listener = new TcpListener(System.Net.IPAddress.Loopback, preferredPort);
                listener.Start();
                listener.Stop();
                return preferredPort;
            }
            catch (SocketException)
            {
                // Preferred port is in use by another process — fall through to ephemeral
            }
        }

        return GetEphemeralPort();
    }

    /// <summary>Finds an available ephemeral port by briefly binding to port 0.</summary>
    private static int GetEphemeralPort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Classifies a SocketException into a structured failure code.</summary>
    private static SshFailureCode ClassifySocketException(SocketException ex)
    {
        return ex.SocketErrorCode switch
        {
            SocketError.ConnectionRefused => SshFailureCode.NetworkRefused,
            SocketError.TimedOut => SshFailureCode.NetworkTimedOut,
            SocketError.HostNotFound or SocketError.HostUnreachable => SshFailureCode.NetworkUnreachable,
            SocketError.AddressAlreadyInUse => SshFailureCode.PortInUse,
            _ => SshFailureCode.Unknown
        };
    }

    /// <summary>Safely cleans up a partially constructed single-hop tunnel.</summary>
    private static void CleanupPartial(SshClient? client, ForwardedPortLocal? port)
    {
        try
        {
            if (port is { IsStarted: true })
            {
                port.Stop();
            }

            port?.Dispose();
        }
        catch (ObjectDisposedException ex) { Heimdall.Core.Logging.FileLogger.Warn($"[TunnelManager] CleanupPartial port: {ex.Message}"); }

        try
        {
            if (client is { IsConnected: true })
            {
                client.Disconnect();
            }

            client?.Dispose();
        }
        catch (ObjectDisposedException ex) { Heimdall.Core.Logging.FileLogger.Warn($"[TunnelManager] CleanupPartial client: {ex.Message}"); }
    }

    /// <summary>Safely cleans up a partially constructed chained tunnel.</summary>
    private static void CleanupChainPartial(
        SshClient? finalClient,
        ForwardedPortLocal? finalPort,
        List<SshClient> intermediateClients,
        List<ForwardedPortLocal> intermediatePorts)
    {
        CleanupPartial(finalClient, finalPort);

        for (int i = intermediatePorts.Count - 1; i >= 0; i--)
        {
            try
            {
                if (intermediatePorts[i].IsStarted)
                {
                    intermediatePorts[i].Stop();
                }

                intermediatePorts[i].Dispose();
            }
            catch (ObjectDisposedException ex) { Heimdall.Core.Logging.FileLogger.Warn($"[TunnelManager] CleanupChainPartial port[{i}]: {ex.Message}"); }
        }

        for (int i = intermediateClients.Count - 1; i >= 0; i--)
        {
            try
            {
                if (intermediateClients[i].IsConnected)
                {
                    intermediateClients[i].Disconnect();
                }

                intermediateClients[i].Dispose();
            }
            catch (ObjectDisposedException ex) { Heimdall.Core.Logging.FileLogger.Warn($"[TunnelManager] CleanupChainPartial client[{i}]: {ex.Message}"); }
        }
    }

    private sealed class ExternalTunnelSession : IDisposable
    {
        private readonly IDisposable _tunnelHandle;
        private readonly Func<bool> _isAlive;

        public ExternalTunnelSession(TunnelInfo info, IDisposable tunnelHandle, Func<bool> isAlive)
        {
            Info = info;
            _tunnelHandle = tunnelHandle;
            _isAlive = isAlive;
        }

        public TunnelInfo Info { get; }

        public bool IsAlive
        {
            get
            {
                try
                {
                    return _isAlive();
                }
                catch (Exception ex)
                {
                    Heimdall.Core.Logging.FileLogger.Warn($"[TunnelManager.ExternalTunnelSession] IsAlive: {ex.Message}");
                    return false;
                }
            }
        }

        public void Dispose()
        {
            _tunnelHandle.Dispose();
        }
    }
}
