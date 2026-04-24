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

using System.Security.Cryptography;

namespace Heimdall.Core.Ssh;

/// <summary>
/// Pure helpers for SSH host identity formatting and fingerprint computation.
/// Shared by the known_hosts importer and the runtime HostKeyStore.
/// </summary>
public static class HostKeyFormats
{
    /// <summary>
    /// Compute an OpenSSH-style SHA256 fingerprint from the raw host key blob.
    /// Format: "SHA256:&lt;base64-no-padding&gt;".
    /// </summary>
    public static string ComputeSha256Fingerprint(byte[] rawKeyBlob)
    {
        ArgumentNullException.ThrowIfNull(rawKeyBlob);

        var hash = SHA256.HashData(rawKeyBlob);
        var base64 = Convert.ToBase64String(hash).TrimEnd('=');
        return $"SHA256:{base64}";
    }

    /// <summary>
    /// Build the persisted trust-store key for a host/port pair.
    /// IPv6 hosts are wrapped in brackets.
    /// </summary>
    public static string MakeKey(string host, int port)
    {
        ArgumentNullException.ThrowIfNull(host);

        return host.Contains(':', StringComparison.Ordinal)
            ? $"[{host}]:{port}"
            : $"{host}:{port}";
    }

    public static bool TryParseKey(string hostPortKey, out string host, out int port)
    {
        ArgumentNullException.ThrowIfNull(hostPortKey);

        host = string.Empty;
        port = 0;

        if (hostPortKey.StartsWith("[", StringComparison.Ordinal))
        {
            var closing = hostPortKey.IndexOf(']');
            if (closing <= 0 || closing + 2 > hostPortKey.Length || hostPortKey[closing + 1] != ':')
            {
                return false;
            }

            host = hostPortKey[1..closing];
            return int.TryParse(hostPortKey[(closing + 2)..], out port) && port is >= 1 and <= 65535;
        }

        var separator = hostPortKey.LastIndexOf(':');
        if (separator <= 0 || separator == hostPortKey.Length - 1)
        {
            return false;
        }

        host = hostPortKey[..separator];
        return int.TryParse(hostPortKey[(separator + 1)..], out port) && port is >= 1 and <= 65535;
    }
}
