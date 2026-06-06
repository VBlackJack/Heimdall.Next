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
    /// <summary>SSH agent protocol flag for RSA-SHA2-256 signatures.</summary>
    internal const uint AgentRsaSha2_256 = 0x02;

    /// <summary>SSH agent protocol flag for RSA-SHA2-512 signatures.</summary>
    internal const uint AgentRsaSha2_512 = 0x04;

    private readonly byte[] _publicKeyBlob;
    private readonly IPageantClient _pageantClient;
    private readonly uint _signFlags;

    /// <summary>
    /// Initializes a new Pageant-backed host algorithm.
    /// </summary>
    /// <param name="algorithmName">SSH algorithm name (e.g., "rsa-sha2-256", "ssh-ed25519").</param>
    /// <param name="publicKeyBlob">Full public key blob as returned by the agent.</param>
    /// <param name="pageantClient">
    /// Pageant client instance used for sign requests.
    /// The caller retains ownership and must keep it alive for the duration of the auth.
    /// </param>
    /// <param name="signFlags">
    /// Agent signature flags passed to Pageant during signing.
    /// Use <see cref="AgentRsaSha2_256"/> (0x02) for rsa-sha2-256,
    /// <see cref="AgentRsaSha2_512"/> (0x04) for rsa-sha2-512,
    /// or 0 for default (ssh-rsa / non-RSA keys).
    /// </param>
    public PageantHostAlgorithm(
        string algorithmName,
        byte[] publicKeyBlob,
        IPageantClient pageantClient,
        uint signFlags = 0)
        : base(algorithmName)
    {
        _publicKeyBlob = publicKeyBlob ?? throw new ArgumentNullException(nameof(publicKeyBlob));
        _pageantClient = pageantClient ?? throw new ArgumentNullException(nameof(pageantClient));
        _signFlags = signFlags;
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
    /// <remarks>
    /// Pageant returns the full SSH signature blob:
    /// <c>[algo_name_len:4][algo_name][raw_sig_len:4][raw_sig]</c>.
    /// SSH.NET's <see cref="Renci.SshNet.PrivateKeyAuthenticationMethod"/>
    /// expects <see cref="HostAlgorithm.Sign"/> to return this exact format
    /// (matching <c>KeyHostAlgorithm.Sign</c> which wraps via
    /// <c>SignatureData</c>). We pass the blob through unchanged; do
    /// <b>not</b> strip the algorithm name prefix.
    /// </remarks>
    /// <param name="data">Data to sign (session hash + auth request from SSH.NET).</param>
    /// <returns>Full SSH signature blob, including the algorithm name prefix.</returns>
    public override byte[] Sign(byte[] data)
    {
        // Pageant returns the full SSH signature blob:
        //   [algo_name_len:4][algo_name][raw_sig_len:4][raw_sig]
        // SSH.NET's PrivateKeyAuthenticationMethod expects Sign() to return
        // this exact format (matching KeyHostAlgorithm.Sign() which wraps
        // via SignatureData). The blob is sent directly to the server.
        return _pageantClient.SignData(_publicKeyBlob, data, _signFlags);
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
