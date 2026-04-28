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

using System.Net.Sockets;
using Heimdall.Core.Ssh;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Heimdall.Ssh;

public sealed partial class TunnelManager
{
    private const int DefaultMaxStartAttempts = 3;
    private const int DefaultRetryDelayMs = 50;

    private static async Task<PinnedFingerprintVerifier?> ResolvePinnedVerifierAsync(
        SshConnectionParams connectionParams,
        string verificationHost,
        int verificationPort,
        HostKeyStore? hostKeyStore,
        IHostKeyVerifier? verifier,
        CancellationToken cancellationToken)
    {
        if (hostKeyStore is null)
        {
            return null;
        }

        if (verifier is null)
        {
            throw new InvalidOperationException("IHostKeyVerifier is required when HostKeyStore is provided.");
        }

        return await SshConnectionFactory.ResolveHostKeyAsync(
                connectionParams,
                verificationHost,
                verificationPort,
                hostKeyStore,
                verifier,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ConnectSshClientWithCancellationAsync(
        SshClient client,
        string verificationHost,
        int verificationPort,
        PinnedFingerprintVerifier? pinnedVerifier,
        CancellationToken cancellationToken,
        string cancelLogMessage)
    {
        if (pinnedVerifier is not null)
        {
            SshConnectionFactory.AttachPinnedHostKeyVerification(
                client,
                verificationHost,
                verificationPort,
                pinnedVerifier);
        }

        await using var connectReg = cancellationToken.Register(
            () =>
            {
                try { client.Disconnect(); }
                catch (Exception ex) { Core.Logging.FileLogger.Debug(cancelLogMessage, ex); }
            });

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            client.Connect();
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void WireFinalForwardedPorts(
        TunnelBuildContext context,
        string remoteHost,
        int remotePort,
        int localPort,
        int socksProxyPort,
        int remoteBindPort,
        int remoteLocalPort,
        bool isChained)
    {
        var finalClient = context.FinalClient
            ?? throw new InvalidOperationException("Final SSH client must be connected before wiring forwarded ports.");

        context.FinalPort = new ForwardedPortLocal("127.0.0.1", (uint)localPort, remoteHost, (uint)remotePort);
        if (!isChained)
        {
            context.FinalPort.Exception += (_, args) =>
                Core.Logging.FileLogger.Error($"SSH forwarded port {localPort} exception: {args.Exception.Message}");
        }

        finalClient.AddForwardedPort(context.FinalPort);
        StartForwardedPortWithRetry(context.FinalPort, $"local port {localPort}");

        var logSuffix = isChained ? " (chained tunnel)" : string.Empty;
        if (socksProxyPort > 0)
        {
            context.DynamicPort = new ForwardedPortDynamic("127.0.0.1", (uint)socksProxyPort);
            finalClient.AddForwardedPort(context.DynamicPort);
            StartForwardedPortWithRetry(context.DynamicPort, $"SOCKS5 port {socksProxyPort}");
            Core.Logging.FileLogger.Info(
                $"SOCKS5 proxy started on 127.0.0.1:{socksProxyPort}{logSuffix}");
        }

        if (remoteBindPort > 0)
        {
            int localFwd = remoteLocalPort > 0 ? remoteLocalPort : remoteBindPort;
            context.RemotePortForward = new ForwardedPortRemote(
                "127.0.0.1", (uint)remoteBindPort,
                "127.0.0.1", (uint)localFwd);
            finalClient.AddForwardedPort(context.RemotePortForward);
            context.RemotePortForward.Start();
            Core.Logging.FileLogger.Info(
                $"Remote forward started: server:{remoteBindPort} \u2192 local:{localFwd}{logSuffix}");
        }
    }

    private static TunnelInfo BuildTunnelInfo(
        string gatewayHost,
        int localPort,
        string remoteHost,
        int remotePort,
        int socksProxyPort,
        int remoteBindPort,
        string? label = null)
    {
        return new TunnelInfo(
            gatewayHost,
            localPort,
            remoteHost,
            remotePort,
            DateTime.UtcNow,
            IsAlive: true)
        {
            SocksProxyPort = socksProxyPort,
            RemoteBindPort = remoteBindPort,
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim()
        };
    }

    private TunnelResult RegisterTunnelSession(
        TunnelSession session,
        int localPort,
        TunnelInfo info)
    {
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

    private static TunnelResult ClassifyAndBuildFailureResult(
        Exception ex,
        Action cleanup,
        bool isChained)
    {
        cleanup();

        return ex switch
        {
            OperationCanceledException => new TunnelResult(
                false,
                null,
                isChained ? "Chained tunnel establishment was cancelled." : "Tunnel establishment was cancelled.",
                SshFailureCode.Cancelled),
            HostKeyRejectedException hostKeyEx => new TunnelResult(
                false,
                null,
                hostKeyEx.Message,
                hostKeyEx.IsMismatch ? SshFailureCode.HostKeyMismatch : SshFailureCode.Cancelled),
            SshAuthenticationException authEx => new TunnelResult(
                false,
                null,
                authEx.Message,
                SshFailureCode.AuthRejected),
            SocketException socketEx => new TunnelResult(
                false,
                null,
                socketEx.Message,
                ClassifySocketException(socketEx)),
            SshConnectionException connectionEx when !isChained => new TunnelResult(
                false,
                null,
                connectionEx.Message,
                SshFailureCode.NetworkRefused),
            SshException sshEx when !isChained && sshEx.Message.Contains("port", StringComparison.OrdinalIgnoreCase) => new TunnelResult(
                false,
                null,
                sshEx.Message,
                SshFailureCode.PortInUse),
            _ => new TunnelResult(false, null, ex.Message, SshFailureCode.Unknown)
        };
    }

    private static SshConnectionParams CreateLoopbackHopParams(
        SshConnectionParams nextGateway,
        int intermediateLocalPort)
    {
        return new SshConnectionParams
        {
            Host = "127.0.0.1",
            Port = intermediateLocalPort,
            Username = nextGateway.Username,
            KeyPath = nextGateway.KeyPath,
            Password = nextGateway.Password,
            KeyPassphrase = nextGateway.KeyPassphrase,
            SshAgentPreference = nextGateway.SshAgentPreference,
            UseLegacyPasswordAsKeyPassphrase = nextGateway.UseLegacyPasswordAsKeyPassphrase,
            LegacyCredentialName = nextGateway.LegacyCredentialName,
            AgentForwarding = nextGateway.AgentForwarding,
            Compression = nextGateway.Compression,
            ConnectTimeout = nextGateway.ConnectTimeout
        };
    }

    internal static void StartForwardedPortWithRetry(
        ForwardedPort port,
        string logContext,
        int maxAttempts = DefaultMaxStartAttempts,
        TimeSpan? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(port);

        ExecuteStartWithRetry(
            port.Start,
            logContext,
            maxAttempts,
            retryDelay ?? TimeSpan.FromMilliseconds(DefaultRetryDelayMs));
    }

    internal static void ExecuteStartWithRetry(
        Action startAction,
        string logContext,
        int maxAttempts,
        TimeSpan retryDelay,
        Action<TimeSpan>? sleep = null)
    {
        ArgumentNullException.ThrowIfNull(startAction);

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "Maximum attempts must be at least 1.");
        }

        sleep ??= Thread.Sleep;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                startAction();
                return;
            }
            catch (SocketException ex) when (IsLocalBindAlreadyInUse(ex) && attempt < maxAttempts)
            {
                Core.Logging.FileLogger.Info(
                    $"Forwarded port {logContext} bind failed, retrying (attempt {attempt}/{maxAttempts}): {ex.Message}");
                sleep(retryDelay);
            }
        }
    }

    private static bool IsLocalBindAlreadyInUse(SocketException ex)
    {
        return ex.SocketErrorCode == SocketError.AddressAlreadyInUse;
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

    private static void SafeDispose(IDisposable? resource, string logMessage)
    {
        if (resource is null) { return; }
        try { resource.Dispose(); }
        catch (Exception ex) { Core.Logging.FileLogger.Debug(logMessage, ex); }
    }

    private sealed class TunnelBuildContext
    {
        public List<SshClient> IntermediateClients { get; } = [];
        public List<ForwardedPortLocal> IntermediatePorts { get; } = [];
        public SshClient? FinalClient { get; set; }
        public ForwardedPortLocal? FinalPort { get; set; }
        public ForwardedPortDynamic? DynamicPort { get; set; }
        public ForwardedPortRemote? RemotePortForward { get; set; }

        public TunnelSession CreateSession(TunnelInfo info)
        {
            return new TunnelSession(
                FinalClient ?? throw new InvalidOperationException("Final SSH client was not created."),
                FinalPort ?? throw new InvalidOperationException("Final forwarded port was not created."),
                info,
                IntermediateClients,
                IntermediatePorts)
            { DynamicPort = DynamicPort, RemotePort = RemotePortForward };
        }

        public void Cleanup()
        {
            SafeDispose(DynamicPort, "Dynamic port dispose suppressed");
            SafeDispose(RemotePortForward, "Remote port forward dispose suppressed");
            CleanupPortAndClient(FinalPort, FinalClient, "final");

            for (int i = IntermediatePorts.Count - 1; i >= 0; i--)
            {
                CleanupPort(IntermediatePorts[i], $"intermediate port[{i}]");
            }

            for (int i = IntermediateClients.Count - 1; i >= 0; i--)
            {
                CleanupClient(IntermediateClients[i], $"intermediate client[{i}]");
            }
        }

        private static void CleanupPortAndClient(
            ForwardedPortLocal? port,
            SshClient? client,
            string label)
        {
            CleanupPort(port, $"{label} port");
            CleanupClient(client, $"{label} client");
        }

        private static void CleanupPort(ForwardedPortLocal? port, string label)
        {
            try
            {
                if (port is { IsStarted: true })
                {
                    port.Stop();
                }

                port?.Dispose();
            }
            catch (ObjectDisposedException ex)
            {
                Core.Logging.FileLogger.Warn($"[TunnelManager] Cleanup {label}: {ex.Message}");
            }
        }

        private static void CleanupClient(SshClient? client, string label)
        {
            try
            {
                if (client is { IsConnected: true })
                {
                    client.Disconnect();
                }

                client?.Dispose();
            }
            catch (ObjectDisposedException ex)
            {
                Core.Logging.FileLogger.Warn($"[TunnelManager] Cleanup {label}: {ex.Message}");
            }
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
