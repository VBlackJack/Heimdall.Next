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

using Renci.SshNet;
using Renci.SshNet.Security;

namespace Heimdall.Ssh.Pageant;

/// <summary>
/// Wraps a Pageant-loaded SSH key as an <see cref="IPrivateKeySource"/> for SSH.NET.
/// The <see cref="HostKeyAlgorithms"/> collection contains a single
/// <see cref="PageantHostAlgorithm"/> that delegates signing to the Pageant agent.
/// </summary>
/// <remarks>
/// SSH.NET's <see cref="PrivateKeyAuthenticationMethod"/> accepts
/// <see cref="IPrivateKeySource"/> instances. By implementing this interface,
/// Pageant keys integrate seamlessly into SSH.NET's auth pipeline without
/// requiring access to the private key material.
/// </remarks>
internal sealed class PageantKeyWrapper : IPrivateKeySource, IDisposable
{
    private readonly PageantClient _client;
    private readonly IReadOnlyCollection<HostAlgorithm> _algorithms;
    private bool _disposed;

    /// <summary>
    /// Creates a wrapper for the specified Pageant key.
    /// A dedicated <see cref="PageantClient"/> instance is created for signing
    /// operations during the authentication handshake.
    /// For RSA keys, registers rsa-sha2-256 and rsa-sha2-512 algorithms
    /// (preferred by modern servers) in addition to the legacy ssh-rsa.
    /// </summary>
    /// <param name="key">Pageant key identity (blob, comment, type).</param>
    public PageantKeyWrapper(PageantKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        _client = new PageantClient();
        _algorithms = BuildAlgorithms(key, _client);
    }

    private static IReadOnlyCollection<HostAlgorithm> BuildAlgorithms(
        PageantKey key,
        PageantClient client)
    {
        // For RSA keys, modern servers require rsa-sha2-256 or rsa-sha2-512.
        // Register SHA-2 variants first (preferred), then legacy ssh-rsa as fallback.
        // The agent protocol flags (0x02, 0x04) tell Pageant which hash to use.
        if (string.Equals(key.KeyType, "ssh-rsa", StringComparison.Ordinal))
        {
            return
            [
                new PageantHostAlgorithm("rsa-sha2-256", key.Blob, client,
                    PageantHostAlgorithm.AgentRsaSha2_256),
                new PageantHostAlgorithm("rsa-sha2-512", key.Blob, client,
                    PageantHostAlgorithm.AgentRsaSha2_512),
                new PageantHostAlgorithm("ssh-rsa", key.Blob, client, 0),
            ];
        }

        return [new PageantHostAlgorithm(key.KeyType, key.Blob, client, 0)];
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<HostAlgorithm> HostKeyAlgorithms => _algorithms;

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _client.Dispose();
        }
    }
}
