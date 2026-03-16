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
    /// </summary>
    /// <param name="key">Pageant key identity (blob, comment, type).</param>
    public PageantKeyWrapper(PageantKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        _client = new PageantClient();

        var algorithm = new PageantHostAlgorithm(key.KeyType, key.Blob, _client);
        _algorithms = new[] { (HostAlgorithm)algorithm };
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
