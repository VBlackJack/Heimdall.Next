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
using Heimdall.Core.Security;
using Heimdall.Ssh;
using Renci.SshNet;

namespace Heimdall.App.Services;

/// <summary>
/// Shared helper for connecting to an SSH gateway from network tools.
/// Runs authentication preflight, attaches TOFU host key verification,
/// and provides clear error messages on failure.
/// </summary>
internal static class ToolGatewayConnector
{
    private static readonly HostKeyStore s_hostKeyStore = new();

    /// <summary>
    /// Creates and connects an SSH client to the specified gateway.
    /// Validates auth prerequisites, verifies host keys via TOFU,
    /// and returns a ready-to-use client for remote command execution.
    /// </summary>
    /// <param name="gateway">Gateway to connect to.</param>
    /// <returns>A connected <see cref="SshClient"/>. Caller must dispose.</returns>
    /// <exception cref="InvalidOperationException">Preflight check failed.</exception>
    /// <exception cref="SshException">SSH connection or authentication failed.</exception>
    public static SshClient Connect(SshGatewayDto gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        string? password = null;
        if (!string.IsNullOrEmpty(gateway.SshPasswordEncrypted))
        {
            password = CredentialProtector.Unprotect(gateway.SshPasswordEncrypted);
        }

        var connParams = new SshConnectionParams
        {
            Host = gateway.Host,
            Port = gateway.Port,
            Username = gateway.User,
            KeyPath = gateway.KeyPath,
            Password = password,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        // Validate authentication prerequisites before attempting connection
        var preflight = AuthPreflightChecker.Check(connParams, isTunnelMode: true);
        if (!preflight.Success)
        {
            throw new InvalidOperationException(
                preflight.Message ?? $"Authentication preflight failed for {gateway.Host}.");
        }

        var connInfo = SshConnectionFactory.Create(connParams);
        var client = new SshClient(connInfo);

        // TOFU host key verification
        SshConnectionFactory.AttachHostKeyVerification(
            client, gateway.Host, gateway.Port, s_hostKeyStore);

        client.Connect();

        Core.Logging.FileLogger.Info(
            $"Tool gateway connected: {gateway.Name} ({gateway.Host}:{gateway.Port})");

        return client;
    }
}
