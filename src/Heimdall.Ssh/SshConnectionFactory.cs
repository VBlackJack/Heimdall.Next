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
    /// Creates a <see cref="ConnectionInfo"/> optimized for port-forwarding tunnels.
    /// Functionally identical to <see cref="Create"/> but semantically separated
    /// for future tunnel-specific tuning (e.g., keep-alive, no shell).
    /// </summary>
    /// <param name="connectionParams">SSH connection parameters.</param>
    /// <returns>A fully configured <see cref="ConnectionInfo"/> for tunneling.</returns>
    public static ConnectionInfo CreateForTunnel(SshConnectionParams connectionParams)
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

        // Password authentication
        if (!string.IsNullOrEmpty(connectionParams.Password) &&
            string.IsNullOrWhiteSpace(connectionParams.KeyPath))
        {
            methods.Add(new PasswordAuthenticationMethod(connectionParams.Username, connectionParams.Password));
        }

        // Pageant agent key authentication (Windows SSH agent)
        if (connectionParams.AgentForwarding)
        {
            var agentMethod = TryCreateAgentAuth(connectionParams.Username);
            if (agentMethod is not null)
            {
                methods.Add(agentMethod);
            }
        }

        // When no key/password configured but Pageant is available,
        // try agent-based auth as primary method
        if (methods.Count == 0)
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
                return null;
            }

            using var client = new PageantClient();
            var keys = client.GetIdentities();

            if (keys.Count == 0)
            {
                return null;
            }

            // Wrap each Pageant key as an IPrivateKeySource for SSH.NET
            var keyWrappers = keys
                .Select(k => new PageantKeyWrapper(k))
                .Cast<IPrivateKeySource>()
                .ToArray();

            return new PrivateKeyAuthenticationMethod(username, keyWrappers);
        }
        catch
        {
            // Pageant communication failure is non-fatal
            return null;
        }
    }
}
