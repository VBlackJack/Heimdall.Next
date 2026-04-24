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
using Heimdall.Ssh.Pageant;
using Renci.SshNet;

namespace Heimdall.Ssh;

/// <summary>
/// Builds SSH.NET <see cref="ConnectionInfo"/> instances from Heimdall connection
/// parameters. Supports password authentication, private key authentication
/// (with optional passphrase), and Pageant SSH agent key authentication.
/// </summary>
public static class SshConnectionFactory
{
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

        var authMethods = BuildAuthMethods(connectionParams);

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
    [Obsolete("Use the overload that accepts IHostKeyVerifier", false)]
    public static void AttachHostKeyVerification(
        Renci.SshNet.BaseClient client,
        string host,
        int port,
        HostKeyStore hostKeyStore)
    {
        AttachHostKeyVerification(
            client,
            host,
            port,
            hostKeyStore,
            AutoAcceptHostKeyVerifier.Instance);
    }

    /// <summary>
    /// Attaches verifier-driven host key verification to an SSH.NET client.
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

        client.HostKeyReceived += (sender, e) =>
        {
            var result = hostKeyStore.Verify(host, port, e.HostKey);
            var algorithm = ExtractKeyAlgorithm(e.HostKeyName, e.HostKey);

            if (!result.FirstUse && result.Trusted)
            {
                e.CanTrust = true;
                return;
            }

            var decision = verifier
                .VerifyAsync(host, port, algorithm, result.Fingerprint, result.StoredFingerprint)
                .GetAwaiter()
                .GetResult();

            if (decision == HostKeyDecision.Accept)
            {
                hostKeyStore.Trust(host, port, result.Fingerprint);
                e.CanTrust = true;
                return;
            }

            e.CanTrust = false;
            Heimdall.Core.Logging.FileLogger.Warn(
                $"User rejected host key for {host}:{port} algorithm={algorithm} stored={result.StoredFingerprint ?? "<none>"} presented={result.Fingerprint}");
            throw new HostKeyRejectedException(
                host,
                port,
                algorithm,
                result.Fingerprint,
                result.StoredFingerprint);
        };
    }

    private static string ExtractKeyAlgorithm(string? hostKeyName, byte[] hostKey)
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
    /// Determines whether the given connection parameters require Pageant agent
    /// authentication, meaning the tunnel cannot be handled by SSH.NET alone and
    /// must fall back to plink.exe via <see cref="Plink.PlinkTunnelRunner"/>.
    /// </summary>
    /// <param name="connectionParams">SSH connection parameters to evaluate.</param>
    /// <returns>
    /// True if Pageant is running and either agent forwarding is enabled,
    /// or no key file and no password are configured (agent is the sole auth source).
    /// </returns>
    public static bool RequiresPageantFallback(SshConnectionParams connectionParams)
    {
        ArgumentNullException.ThrowIfNull(connectionParams);

        // Explicit agent forwarding requested
        if (connectionParams.AgentForwarding && PageantClient.IsAvailable())
        {
            return true;
        }

        // No key and no password: Pageant is the only viable auth source
        if (string.IsNullOrEmpty(connectionParams.KeyPath)
            && string.IsNullOrEmpty(connectionParams.Password)
            && PageantClient.IsAvailable())
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Assembles the list of authentication methods from connection parameters.
    /// Order: key file, password, agent. SSH.NET tries them in order.
    /// </summary>
    private static List<AuthenticationMethod> BuildAuthMethods(SshConnectionParams connectionParams)
    {
        var methods = new List<AuthenticationMethod>();

        // Private key authentication (with optional passphrase)
        if (!string.IsNullOrWhiteSpace(connectionParams.KeyPath))
        {
            var keyFile = string.IsNullOrEmpty(connectionParams.Password)
                ? new PrivateKeyFile(connectionParams.KeyPath)
                : new PrivateKeyFile(connectionParams.KeyPath, connectionParams.Password);

            methods.Add(new PrivateKeyAuthenticationMethod(connectionParams.Username, keyFile));
        }

        // Password authentication.
        // Two methods are registered in sequence so SSH.NET tries them in order:
        //   1. "password" (RFC 4252 §8): supported by most servers.
        //   2. "keyboard-interactive" (RFC 4256): required when PasswordAuthentication is
        //      disabled server-side but KbdInteractiveAuthentication is enabled (common on
        //      hardened Linux). SSH.NET tries the next method automatically on rejection.
        if (!string.IsNullOrEmpty(connectionParams.Password) &&
            string.IsNullOrWhiteSpace(connectionParams.KeyPath))
        {
            methods.Add(new PasswordAuthenticationMethod(connectionParams.Username, connectionParams.Password));

            var password = connectionParams.Password; // Captured in closure — avoid re-reading mutable param
            var kbdInteractive = new KeyboardInteractiveAuthenticationMethod(connectionParams.Username);
            kbdInteractive.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    prompt.Response = password;
                }
            };
            methods.Add(kbdInteractive);
        }

        // Pageant agent key authentication (Windows SSH agent).
        // Always add Pageant as a supplementary auth method when available,
        // so SSH.NET can fall back to the agent if key file or password auth
        // is rejected (e.g., passphrase-protected key loaded only in Pageant).
        {
            var agentMethod = TryCreateAgentAuth(connectionParams.Username);
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

    /// <summary>
    /// Attempts to create a Pageant-based agent authentication method.
    /// Queries Pageant for loaded keys and wraps the first available key
    /// into a <see cref="PrivateKeyAuthenticationMethod"/> via a
    /// <see cref="PageantKeyWrapper"/> that delegates signing to the agent.
    /// Returns null if Pageant is not running or no keys are loaded.
    /// </summary>
    private static AuthenticationMethod? TryCreateAgentAuth(string username)
    {
        try
        {
            if (!PageantClient.IsAvailable())
            {
                Heimdall.Core.Logging.FileLogger.Info("Pageant: not available");
                return null;
            }

            using var client = new PageantClient();
            var keys = client.GetIdentities();

            if (keys.Count == 0)
            {
                Heimdall.Core.Logging.FileLogger.Info("Pageant: no keys loaded");
                return null;
            }

            Heimdall.Core.Logging.FileLogger.Info(
                $"Pageant: {keys.Count} key(s) loaded: {string.Join(", ", keys.Select(k => $"{k.KeyType} ({k.Comment})"))}");

            // Wrap each Pageant key as an IPrivateKeySource for SSH.NET
            var keyWrappers = keys
                .Select(k => new PageantKeyWrapper(k))
                .Cast<IPrivateKeySource>()
                .ToArray();

            return new PrivateKeyAuthenticationMethod(username, keyWrappers);
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn($"Pageant auth setup failed: {ex.Message}");
            return null;
        }
    }
}
