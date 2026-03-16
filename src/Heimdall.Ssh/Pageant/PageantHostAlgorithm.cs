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

using Renci.SshNet.Security;

namespace Heimdall.Ssh.Pageant;

/// <summary>
/// Custom <see cref="HostAlgorithm"/> implementation that delegates signing
/// operations to the Pageant SSH agent. The private key material never leaves
/// Pageant; only the public key blob and signature requests are exchanged
/// via shared memory IPC.
/// </summary>
internal sealed class PageantHostAlgorithm : HostAlgorithm
{
    private readonly byte[] _publicKeyBlob;
    private readonly PageantClient _pageantClient;

    /// <summary>
    /// Initializes a new Pageant-backed host algorithm.
    /// </summary>
    /// <param name="algorithmName">SSH algorithm name (e.g., "ssh-rsa", "ssh-ed25519").</param>
    /// <param name="publicKeyBlob">Full public key blob as returned by the agent.</param>
    /// <param name="pageantClient">
    /// Pageant client instance used for sign requests.
    /// The caller retains ownership and must keep it alive for the duration of the auth.
    /// </param>
    public PageantHostAlgorithm(string algorithmName, byte[] publicKeyBlob, PageantClient pageantClient)
        : base(algorithmName)
    {
        _publicKeyBlob = publicKeyBlob ?? throw new ArgumentNullException(nameof(publicKeyBlob));
        _pageantClient = pageantClient ?? throw new ArgumentNullException(nameof(pageantClient));
    }

    /// <summary>
    /// Returns the raw public key blob. SSH.NET uses this to propose the key
    /// to the server during the authentication handshake.
    /// </summary>
    public override byte[] Data => _publicKeyBlob;

    /// <summary>
    /// Signs the given data by delegating to Pageant via shared memory IPC.
    /// Pageant performs the cryptographic operation using the private key
    /// it holds in memory; the private key never leaves the agent.
    /// </summary>
    /// <param name="data">Data to sign (session hash + auth request from SSH.NET).</param>
    /// <returns>Raw signature bytes from the agent.</returns>
    public override byte[] Sign(byte[] data)
    {
        return _pageantClient.SignData(_publicKeyBlob, data);
    }

    /// <summary>
    /// Verifies a signature against the given data and hash.
    /// Not needed for client-side authentication. Returns false.
    /// </summary>
    public override bool VerifySignature(byte[] data, byte[] signature)
    {
        // Signature verification is a server-side concern during key exchange.
        // For client-side Pageant auth, this is never called.
        return false;
    }
}
