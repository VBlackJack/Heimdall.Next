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
using Heimdall.Core.Ssh;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Heimdall.Ssh;

/// <summary>
/// Manages the lifecycle of SSH port-forwarding tunnels. Thread-safe.
/// Replaces the legacy plink-based tunnel management with in-process SSH.NET tunnels.
/// </summary>
public sealed partial class TunnelManager : IDisposable
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
    /// <param name="hostKeyStore">TOFU host key store for server verification.</param>
    /// <param name="verifier">Verifier used when a host key is unknown or changed.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <returns>Result indicating success or structured failure.</returns>
    public async Task<TunnelResult> OpenTunnelAsync(
        SshConnectionParams gatewayParams,
        string remoteHost,
        int remotePort,
        int localPort,
        HostKeyStore hostKeyStore,
        IHostKeyVerifier verifier,
        CancellationToken cancellationToken = default,
        int keepAliveIntervalSeconds = 30,
        int socksProxyPort = 0,
        int remoteBindPort = 0,
        int remoteLocalPort = 0,
        string? label = null,
        string? gatewayChainKey = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(gatewayParams);
        ArgumentNullException.ThrowIfNull(hostKeyStore);
        ArgumentNullException.ThrowIfNull(verifier);

        if (IsPortTracked(localPort))
        {
            return new TunnelResult(false, null, $"Local port {localPort} is already in use by an existing tunnel.", SshFailureCode.PortInUse);
        }

        var context = new TunnelBuildContext();

        try
        {
            var pinnedVerifier = await ResolvePinnedVerifierAsync(
                    gatewayParams,
                    gatewayParams.Host,
                    gatewayParams.Port,
                    hostKeyStore,
                    verifier,
                    cancellationToken)
                .ConfigureAwait(false);

            var connectionInfo = SshConnectionFactory.Create(gatewayParams);
            context.FinalClient = new SshClient(connectionInfo)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(keepAliveIntervalSeconds)
            };

            context.FinalClient.ErrorOccurred += (_, args) =>
                Core.Logging.FileLogger.Error($"SSH tunnel error on port {localPort}: {args.Exception.Message}");

            await ConnectSshClientWithCancellationAsync(
                    context.FinalClient,
                    gatewayParams.Host,
                    gatewayParams.Port,
                    pinnedVerifier,
                    cancellationToken,
                    "Client disconnect on cancel suppressed")
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            WireFinalForwardedPorts(
                context,
                remoteHost,
                remotePort,
                localPort,
                socksProxyPort,
                remoteBindPort,
                remoteLocalPort,
                isChained: false);

            var info = BuildTunnelInfo(
                gatewayParams.Host,
                localPort,
                remoteHost,
                remotePort,
                socksProxyPort,
                remoteBindPort,
                label,
                gatewayChainKey);

            var session = context.CreateSession(info);

            return RegisterTunnelSession(session, localPort, info);
        }
        catch (Exception ex)
        {
            return ClassifyAndBuildFailureResult(ex, context.Cleanup, isChained: false);
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
    /// <param name="hostKeyStore">TOFU host key store for server verification.</param>
    /// <param name="verifier">Verifier used when a host key is unknown or changed.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <returns>Result indicating success or structured failure.</returns>
    public async Task<TunnelResult> OpenChainedTunnelAsync(
        IReadOnlyList<SshConnectionParams> gatewayChain,
        string remoteHost,
        int remotePort,
        int localPort,
        HostKeyStore hostKeyStore,
        IHostKeyVerifier verifier,
        CancellationToken cancellationToken = default,
        int socksProxyPort = 0,
        int remoteBindPort = 0,
        int remoteLocalPort = 0,
        string? label = null,
        string? gatewayChainKey = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(gatewayChain);
        ArgumentNullException.ThrowIfNull(hostKeyStore);
        ArgumentNullException.ThrowIfNull(verifier);

        if (gatewayChain.Count == 0)
        {
            return new TunnelResult(false, null, "Gateway chain must contain at least one gateway.", SshFailureCode.Unknown);
        }

        // Single gateway: delegate to simple tunnel
        if (gatewayChain.Count == 1)
        {
            return await OpenTunnelAsync(gatewayChain[0], remoteHost, remotePort, localPort, hostKeyStore, verifier,
                    cancellationToken,
                    socksProxyPort: socksProxyPort, remoteBindPort: remoteBindPort, remoteLocalPort: remoteLocalPort,
                    label: label,
                    gatewayChainKey: gatewayChainKey)
                .ConfigureAwait(false);
        }

        if (IsPortTracked(localPort))
        {
            return new TunnelResult(false, null, $"Local port {localPort} is already in use by an existing tunnel.", SshFailureCode.PortInUse);
        }

        var context = new TunnelBuildContext();

        try
        {
            // Build the chain: each hop connects to the next via a local port forward
            // Hop 0: connect to gateway[0] directly
            // Hop 1: forward through gateway[0] to gateway[1], connect to gateway[1] via local forward
            // ...
            // Final: forward through last intermediate to remoteHost:remotePort

            int nextLocalPort = GetEphemeralPort();

            var rootPinnedVerifier = await ResolvePinnedVerifierAsync(
                    gatewayChain[0],
                    gatewayChain[0].Host,
                    gatewayChain[0].Port,
                    hostKeyStore,
                    verifier,
                    cancellationToken)
                .ConfigureAwait(false);

            // Connect to the first (root) gateway directly
            var rootClient = new SshClient(SshConnectionFactory.Create(gatewayChain[0]));
            context.IntermediateClients.Add(rootClient);

            await ConnectSshClientWithCancellationAsync(
                    rootClient,
                    gatewayChain[0].Host,
                    gatewayChain[0].Port,
                    rootPinnedVerifier,
                    cancellationToken,
                    "Root client disconnect on cancel suppressed")
                .ConfigureAwait(false);

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
                context.IntermediatePorts.Add(intermediatePort);
                currentClient.AddForwardedPort(intermediatePort);
                StartForwardedPortWithRetry(intermediatePort, $"intermediate chain port {intermediateLocalPort}");

                // Connect to the next gateway through the forwarded port
                var hopParams = CreateLoopbackHopParams(nextGateway, intermediateLocalPort);

                var hopPinnedVerifier = await ResolvePinnedVerifierAsync(
                        hopParams,
                        nextGateway.Host,
                        nextGateway.Port,
                        hostKeyStore,
                        verifier,
                        cancellationToken)
                    .ConfigureAwait(false);

                var hopClient = new SshClient(SshConnectionFactory.Create(hopParams));

                if (i < gatewayChain.Count - 1)
                {
                    context.IntermediateClients.Add(hopClient);
                    currentClient = hopClient;
                }
                else
                {
                    // This is the final gateway
                    context.FinalClient = hopClient;
                }

                await ConnectSshClientWithCancellationAsync(
                        hopClient,
                        nextGateway.Host,
                        nextGateway.Port,
                        hopPinnedVerifier,
                        cancellationToken,
                        "Hop client disconnect on cancel suppressed")
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            WireFinalForwardedPorts(
                context,
                remoteHost,
                remotePort,
                localPort,
                socksProxyPort,
                remoteBindPort,
                remoteLocalPort,
                isChained: true);

            var tunnelInfo = BuildTunnelInfo(
                gatewayChain[^1].Host,
                localPort,
                remoteHost,
                remotePort,
                socksProxyPort,
                remoteBindPort,
                label,
                gatewayChainKey);

            return RegisterTunnelSession(context.CreateSession(tunnelInfo), localPort, tunnelInfo);
        }
        catch (Exception ex)
        {
            return ClassifyAndBuildFailureResult(ex, context.Cleanup, isChained: true);
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
        if (preferredPort > 0)
        {
            if (IsPortTracked(preferredPort))
            {
                Heimdall.Core.Logging.FileLogger.Info(
                    $"TunnelManager: preferred local port {preferredPort} is already tracked by another tunnel; using ephemeral.");
            }
            else
            {
                try
                {
                    using var listener = new TcpListener(System.Net.IPAddress.Loopback, preferredPort);
                    listener.Start();
                    listener.Stop();
                    return preferredPort;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    Heimdall.Core.Logging.FileLogger.Info(
                        $"TunnelManager: preferred local port {preferredPort} is held by another process; using ephemeral.");
                }
                catch (SocketException ex)
                {
                    Heimdall.Core.Logging.FileLogger.Warn(
                        $"TunnelManager: preferred local port {preferredPort} bind failed ({ex.SocketErrorCode}); using ephemeral.");
                }
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

}
