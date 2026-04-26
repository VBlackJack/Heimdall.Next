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

using System.Buffers.Binary;
using System.Text;
using Heimdall.Core.Ssh;
using Heimdall.Ssh.Agents;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Heimdall.Ssh;

/// <summary>
/// Builds SSH.NET <see cref="ConnectionInfo"/> instances from Heimdall connection
/// parameters. Supports password authentication, private key authentication
/// (with optional passphrase), and SSH agent key authentication.
/// </summary>
public static class SshConnectionFactory
{
    /// <summary>
    /// Presented SSH host key captured during a pre-authentication probe.
    /// </summary>
    /// <param name="Host">Host used for trust lookup.</param>
    /// <param name="Port">Port used for trust lookup.</param>
    /// <param name="Algorithm">Presented host key algorithm.</param>
    /// <param name="Fingerprint">Presented SHA256 fingerprint.</param>
    /// <param name="HostKey">Raw host key bytes.</param>
    public sealed record HostKeyPresentation(
        string Host,
        int Port,
        string Algorithm,
        string Fingerprint,
        byte[] HostKey);

    /// <summary>
    /// Creates a <see cref="ConnectionInfo"/> suitable for interactive sessions
    /// (shell, SFTP) from the supplied connection parameters.
    /// </summary>
    /// <param name="connectionParams">SSH connection parameters.</param>
    /// <returns>A fully configured <see cref="ConnectionInfo"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="connectionParams"/> is null.</exception>
    /// <exception cref="ArgumentException">No authentication method could be determined.</exception>
    public static ConnectionInfo Create(SshConnectionParams connectionParams)
    {
        ArgumentNullException.ThrowIfNull(connectionParams);

        return Create(
            connectionParams,
            SshAgentRegistry.CreateDefault(connectionParams.SshAgentPreference));
    }

    internal static ConnectionInfo Create(
        SshConnectionParams connectionParams,
        SshAgentRegistry agentRegistry)
    {
        ArgumentNullException.ThrowIfNull(connectionParams);
        ArgumentNullException.ThrowIfNull(agentRegistry);

        var authMethods = BuildAuthMethods(connectionParams, agentRegistry);

        var info = new ConnectionInfo(
            connectionParams.Host,
            connectionParams.Port,
            connectionParams.Username,
            [.. authMethods])
        {
            Timeout = connectionParams.ConnectTimeout
        };

        return info;
    }

    /// <summary>
    /// Attaches TOFU (Trust On First Use) host key verification to an SSH.NET client.
    /// Call this on <see cref="Renci.SshNet.SshClient"/> or <see cref="Renci.SshNet.SftpClient"/>
    /// BEFORE calling <c>Connect()</c>.
    /// </summary>
    [Obsolete("Use ResolveHostKeyAsync and AttachPinnedHostKeyVerification.", true)]
    public static void AttachHostKeyVerification(
        Renci.SshNet.BaseClient client,
        string host,
        int port,
        HostKeyStore hostKeyStore)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(hostKeyStore);

        throw new InvalidOperationException(
            "Host key verification requires a pre-resolved PinnedFingerprintVerifier.");
    }

    /// <summary>
    /// Attaches pre-resolved host key verification to an SSH.NET client.
    /// Call this on <see cref="Renci.SshNet.SshClient"/> or <see cref="Renci.SshNet.SftpClient"/>
    /// BEFORE calling <c>Connect()</c>.
    /// </summary>
    public static void AttachHostKeyVerification(
        Renci.SshNet.BaseClient client,
        string host,
        int port,
        HostKeyStore hostKeyStore,
        IHostKeyVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(hostKeyStore);
        ArgumentNullException.ThrowIfNull(verifier);

        if (verifier is not PinnedFingerprintVerifier pinnedVerifier)
        {
            throw new InvalidOperationException(
                "HostKeyReceived cannot invoke interactive verifiers. Resolve host key trust before Connect().");
        }

        AttachPinnedHostKeyVerification(client, host, port, pinnedVerifier);
    }

    /// <summary>
    /// Attaches a strict, synchronous host key pin to an SSH.NET client.
    /// </summary>
    public static void AttachPinnedHostKeyVerification(
        Renci.SshNet.BaseClient client,
        string host,
        int port,
        PinnedFingerprintVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(verifier);

        client.HostKeyReceived += (sender, e) =>
        {
            var outcome = EvaluatePinnedHostKey(host, port, e.HostKeyName, e.HostKey, verifier);
            e.CanTrust = outcome.Trusted;
            if (!outcome.Trusted)
            {
                throw new HostKeyRejectedException(
                    host,
                    port,
                    outcome.Algorithm,
                    outcome.Fingerprint,
                    verifier.Fingerprint);
            }
        };
    }

    /// <summary>
    /// Resolves a pinned verifier by probing the server host key before authentication.
    /// </summary>
    public static async Task<PinnedFingerprintVerifier> ResolveHostKeyAsync(
        SshConnectionParams connectionParams,
        HostKeyStore hostKeyStore,
        IHostKeyVerifier verifier,
        CancellationToken cancellationToken = default)
    {
        return await ResolveHostKeyAsync(
                connectionParams,
                connectionParams.Host,
                connectionParams.Port,
                hostKeyStore,
                verifier,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a pinned verifier by probing a transport endpoint while storing
    /// trust against the logical host and port.
    /// </summary>
    public static async Task<PinnedFingerprintVerifier> ResolveHostKeyAsync(
        SshConnectionParams connectionParams,
        string verificationHost,
        int verificationPort,
        HostKeyStore hostKeyStore,
        IHostKeyVerifier verifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionParams);
        ArgumentNullException.ThrowIfNull(verificationHost);
        ArgumentNullException.ThrowIfNull(hostKeyStore);
        ArgumentNullException.ThrowIfNull(verifier);

        var presentation = await ProbeHostKeyAsync(
                connectionParams,
                verificationHost,
                verificationPort,
                cancellationToken)
            .ConfigureAwait(false);

        return await ResolvePresentedHostKeyAsync(
                verificationHost,
                verificationPort,
                presentation,
                hostKeyStore,
                verifier,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Captures the host key from a throwaway none-auth SSH.NET connection.
    /// </summary>
    public static async Task<HostKeyPresentation> ProbeHostKeyAsync(
        SshConnectionParams connectionParams,
        string verificationHost,
        int verificationPort,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionParams);
        ArgumentNullException.ThrowIfNull(verificationHost);

        var username = string.IsNullOrWhiteSpace(connectionParams.Username)
            ? "heimdall-probe"
            : connectionParams.Username;
        var connectionInfo = new ConnectionInfo(
            connectionParams.Host,
            connectionParams.Port,
            username,
            new NoneAuthenticationMethod(username))
        {
            Timeout = connectionParams.ConnectTimeout
        };

        HostKeyPresentation? presentation = null;
        Exception? connectFailure = null;

        using var client = new SshClient(connectionInfo);
        client.HostKeyReceived += (_, e) =>
        {
            var hostKey = e.HostKey.ToArray();
            presentation = new HostKeyPresentation(
                verificationHost,
                verificationPort,
                ExtractKeyAlgorithm(e.HostKeyName, hostKey),
                HostKeyStore.ComputeFingerprint(hostKey),
                hostKey);
            e.CanTrust = false;
        };

        await using var connectReg = cancellationToken.Register(
            () =>
            {
                try { client.Disconnect(); }
                catch (Exception ex) { Core.Logging.FileLogger.Debug("SSH host key probe disconnect suppressed", ex); }
            });

        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                client.Connect();
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            connectFailure = ex;
        }
        finally
        {
            if (client.IsConnected)
            {
                try { client.Disconnect(); }
                catch (Exception ex) { Core.Logging.FileLogger.Debug("SSH host key probe cleanup suppressed", ex); }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (presentation is not null)
        {
            return presentation;
        }

        if (connectFailure is not null)
        {
            throw connectFailure;
        }

        throw new InvalidOperationException(
            $"SSH host key probe completed without receiving a host key for {connectionParams.Host}:{connectionParams.Port}.");
    }

    internal static async Task<PinnedFingerprintVerifier> ResolvePresentedHostKeyAsync(
        string verificationHost,
        int verificationPort,
        HostKeyPresentation presentation,
        HostKeyStore hostKeyStore,
        IHostKeyVerifier verifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verificationHost);
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(hostKeyStore);
        ArgumentNullException.ThrowIfNull(verifier);

        var trustService = new HostKeyTrustService(hostKeyStore);
        var result = trustService.Verify(
            verificationHost,
            verificationPort,
            presentation.Fingerprint,
            presentation.Algorithm);
        if (!result.FirstUse && result.Trusted)
        {
            return new PinnedFingerprintVerifier(verificationHost, verificationPort, result.Fingerprint);
        }

        var decision = await verifier.VerifyAsync(
                verificationHost,
                verificationPort,
                presentation.Algorithm,
                result.Fingerprint,
                result.StoredFingerprint,
                cancellationToken)
            .ConfigureAwait(false);

        if (decision == HostKeyDecision.Accept)
        {
            trustService.Trust(
                verificationHost,
                verificationPort,
                result.Fingerprint,
                presentation.Algorithm,
                HostKeySource.UserConfirmed,
                Convert.ToBase64String(presentation.HostKey));
            return new PinnedFingerprintVerifier(verificationHost, verificationPort, result.Fingerprint);
        }

        if (decision == HostKeyDecision.TrustOnce)
        {
            trustService.TrustForSession(
                verificationHost,
                verificationPort,
                result.Fingerprint,
                presentation.Algorithm,
                Convert.ToBase64String(presentation.HostKey));
            return new PinnedFingerprintVerifier(verificationHost, verificationPort, result.Fingerprint);
        }

        throw new HostKeyRejectedException(
            verificationHost,
            verificationPort,
            presentation.Algorithm,
            result.Fingerprint,
            result.StoredFingerprint);
    }

    internal static HostKeyPinEvaluation EvaluatePinnedHostKey(
        string host,
        int port,
        string? hostKeyName,
        byte[] hostKey,
        PinnedFingerprintVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(hostKey);
        ArgumentNullException.ThrowIfNull(verifier);

        var fingerprint = HostKeyStore.ComputeFingerprint(hostKey);
        var trusted = verifier.Matches(host, port, fingerprint);
        return new HostKeyPinEvaluation(
            trusted,
            ExtractKeyAlgorithm(hostKeyName, hostKey),
            fingerprint);
    }

    internal sealed record HostKeyPinEvaluation(
        bool Trusted,
        string Algorithm,
        string Fingerprint);

    internal static string ExtractKeyAlgorithm(string? hostKeyName, byte[] hostKey)
    {
        if (!string.IsNullOrWhiteSpace(hostKeyName))
        {
            return hostKeyName;
        }

        if (hostKey.Length < sizeof(uint))
        {
            return "unknown";
        }

        var nameLength = BinaryPrimitives.ReadUInt32BigEndian(hostKey);
        if (nameLength == 0 || nameLength > hostKey.Length - sizeof(uint))
        {
            return "unknown";
        }

        try
        {
            return Encoding.ASCII.GetString(hostKey, sizeof(uint), (int)nameLength);
        }
        catch (DecoderFallbackException)
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Determines whether plink.exe is required for features SSH.NET cannot provide.
    /// </summary>
    public static bool RequiresPlinkFallback(SshConnectionParams connectionParams)
    {
        ArgumentNullException.ThrowIfNull(connectionParams);

        return connectionParams.AgentForwarding
            && SshAgentRegistry.CreateDefault(connectionParams.SshAgentPreference)
                .HasPlinkCompatibleAgent();
    }

    /// <summary>
    /// Assembles the list of authentication methods from connection parameters.
    /// Order: key file, password, agent. SSH.NET tries them in order.
    /// </summary>
    private static List<AuthenticationMethod> BuildAuthMethods(SshConnectionParams connectionParams)
    {
        ArgumentNullException.ThrowIfNull(connectionParams);

        return BuildAuthMethods(
            connectionParams,
            SshAgentRegistry.CreateDefault(connectionParams.SshAgentPreference));
    }

    private static List<AuthenticationMethod> BuildAuthMethods(
        SshConnectionParams connectionParams,
        SshAgentRegistry agentRegistry)
    {
        var methods = new List<AuthenticationMethod>();

        // Private key authentication (with optional passphrase)
        if (!string.IsNullOrWhiteSpace(connectionParams.KeyPath))
        {
            AddPrivateKeyMethod(methods, connectionParams);
        }

        // Password authentication.
        // Two methods are registered in sequence so SSH.NET tries them in order:
        //   1. "password" (RFC 4252 §8): supported by most servers.
        //   2. "keyboard-interactive" (RFC 4256): required when PasswordAuthentication is
        //      disabled server-side but KbdInteractiveAuthentication is enabled (common on
        //      hardened Linux). SSH.NET tries the next method automatically on rejection.
        if (!string.IsNullOrEmpty(connectionParams.Password))
        {
            AddPasswordMethods(methods, connectionParams.Username, connectionParams.Password);
        }

        // SSH agent key authentication. Always add agent keys as supplementary
        // auth methods when available, so SSH.NET can fall back to an agent if
        // key file or password auth is rejected.
        {
            var agentMethod = TryCreateAgentAuth(
                connectionParams.Username,
                agentRegistry);
            if (agentMethod is not null)
            {
                methods.Add(agentMethod);
            }
        }

        // Fallback: if still no auth method, add NoneAuthenticationMethod.
        if (methods.Count == 0)
        {
            methods.Add(new NoneAuthenticationMethod(connectionParams.Username));
        }

        return methods;
    }

    private static string? ResolveKeyPassphrase(SshConnectionParams connectionParams)
    {
        if (!string.IsNullOrEmpty(connectionParams.KeyPassphrase))
        {
            return connectionParams.KeyPassphrase;
        }

        if (connectionParams.UseLegacyPasswordAsKeyPassphrase
            && !string.IsNullOrEmpty(connectionParams.Password)
            && !string.IsNullOrWhiteSpace(connectionParams.KeyPath))
        {
            var name = string.IsNullOrWhiteSpace(connectionParams.LegacyCredentialName)
                ? $"{connectionParams.Username}@{connectionParams.Host}:{connectionParams.Port}"
                : connectionParams.LegacyCredentialName;
            Core.Logging.FileLogger.Info(
                $"Legacy credential mapping for server {name} - configure KeyPassphrase explicitly.");
            return connectionParams.Password;
        }

        return null;
    }

    private static void AddPrivateKeyMethod(
        ICollection<AuthenticationMethod> methods,
        SshConnectionParams connectionParams)
    {
        var keyPath = connectionParams.KeyPath
            ?? throw new InvalidOperationException("KeyPath is required for private key authentication.");

        try
        {
            var keyPassphrase = ResolveKeyPassphrase(connectionParams);
            var keyFile = string.IsNullOrEmpty(keyPassphrase)
                ? new PrivateKeyFile(keyPath)
                : new PrivateKeyFile(keyPath, keyPassphrase);

            methods.Add(new PrivateKeyAuthenticationMethod(connectionParams.Username, keyFile));
        }
        catch (Exception ex) when (CanFallBackToPasswordAfterKeyLoadFailure(ex, connectionParams))
        {
            Core.Logging.FileLogger.Warn(
                $"SSH key file could not be loaded for {connectionParams.Username}@{connectionParams.Host}:{connectionParams.Port}; trying password fallback. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool CanFallBackToPasswordAfterKeyLoadFailure(
        Exception ex,
        SshConnectionParams connectionParams)
    {
        if (string.IsNullOrEmpty(connectionParams.Password))
        {
            return false;
        }

        // SSH.NET raises typed passphrase/key parsing exceptions before it can
        // attempt later auth methods. When an explicit password fallback exists,
        // keep building the connection so password auth can still proceed.
        return ex is SshPassPhraseNullOrEmptyException
            or SshException
            or FormatException
            or ArgumentException;
    }

    private static void AddPasswordMethods(
        ICollection<AuthenticationMethod> methods,
        string username,
        string password)
    {
        methods.Add(new PasswordAuthenticationMethod(username, password));

        var capturedPassword = password; // Captured in closure - avoid re-reading mutable param.
        var kbdInteractive = new KeyboardInteractiveAuthenticationMethod(username);
        kbdInteractive.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
            {
                prompt.Response = capturedPassword;
            }
        };
        methods.Add(kbdInteractive);
    }

    /// <summary>
    /// Attempts to create an SSH-agent authentication method.
    /// Returns null if no configured agent is available or no keys are loaded.
    /// </summary>
    private static AuthenticationMethod? TryCreateAgentAuth(
        string username,
        SshAgentRegistry agentRegistry)
    {
        ArgumentNullException.ThrowIfNull(agentRegistry);

        foreach (var agent in agentRegistry.GetAvailableAgents())
        {
            try
            {
                var keys = agent.GetIdentities();

                if (keys.Count == 0)
                {
                    Heimdall.Core.Logging.FileLogger.Info($"SSH agent {agent.Name}: no keys loaded");
                    continue;
                }

                Heimdall.Core.Logging.FileLogger.Info(
                    $"SSH agent {agent.Name}: using {keys.Count} key(s): {string.Join(", ", keys.Select(k => $"{k.KeyType} ({k.Comment})"))}");

                var keyWrappers = keys
                    .Select(k => new SshAgentKeyWrapper(k))
                    .Cast<IPrivateKeySource>()
                    .ToArray();

                return new PrivateKeyAuthenticationMethod(username, keyWrappers);
            }
            catch (Exception ex)
            {
                Heimdall.Core.Logging.FileLogger.Warn(
                    $"SSH agent {agent.Name}: auth setup failed: {ex.Message}");
            }
        }

        return null;
    }
}
