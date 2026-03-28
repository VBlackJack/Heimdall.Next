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

using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace Heimdall.Core.Discovery;

/// <summary>
/// Downloads a favicon from HTTP(S) services and computes a MurmurHash3
/// fingerprint compatible with Shodan's favicon hash format.
/// Known hashes map to specific devices (routers, NAS, IoT, management panels).
/// </summary>
public static class FaviconHasher
{
    /// <summary>
    /// Downloads /favicon.ico and returns its MurmurHash3 (32-bit) hash.
    /// Returns null if the favicon cannot be retrieved or is empty.
    /// </summary>
    public static async Task<int?> HashAsync(
        string host, int port, bool useTls, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

            await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
            Stream stream = client.GetStream();
            SslStream? ssl = null;

            try
            {
                if (useTls)
                {
                    ssl = new SslStream(stream, leaveInnerStreamOpen: true, (_, _, _, _) => true);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = host
                    }, linked.Token).ConfigureAwait(false);
                    stream = ssl;
                }

                var request = $"GET /favicon.ico HTTP/1.0\r\nHost: {host}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(request), linked.Token)
                    .ConfigureAwait(false);
                await stream.FlushAsync(linked.Token).ConfigureAwait(false);

                // Read entire response
                using var ms = new MemoryStream();
                var buf = new byte[8192];
                int n;
                while ((n = await stream.ReadAsync(buf, linked.Token).ConfigureAwait(false)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > 1_048_576) break; // 1MB limit
                }

                var response = ms.ToArray();

                // Find body after \r\n\r\n
                var headerEnd = FindHeaderEnd(response);
                if (headerEnd < 0) return null;

                // Check for HTTP 200 OK (first line only)
                var firstLineEnd = Array.IndexOf(response, (byte)'\n', 0, Math.Min(headerEnd, 128));
                if (firstLineEnd < 0) firstLineEnd = Math.Min(headerEnd, 128);
                var statusLine = Encoding.ASCII.GetString(response, 0, firstLineEnd);
                if (!statusLine.StartsWith("HTTP", StringComparison.OrdinalIgnoreCase) ||
                    !statusLine.Contains(" 200 ")) return null;

                var body = response.AsSpan(headerEnd);
                if (body.Length < 4) return null; // Too small to be a valid icon

                // Shodan-compatible: base64 encode with MIME line breaks, then MurmurHash3
                var b64 = Convert.ToBase64String(body.ToArray());
                var mimeB64 = InsertMimeLineBreaks(b64);
                return MurmurHash3(mimeB64);
            }
            finally
            {
                if (ssl is not null) await ssl.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Inserts newlines every 76 characters (MIME-style base64) to match
    /// Shodan's favicon hashing behavior.
    /// </summary>
    private static string InsertMimeLineBreaks(string b64)
    {
        var sb = new StringBuilder(b64.Length + b64.Length / 76 + 2);
        for (var i = 0; i < b64.Length; i += 76)
        {
            var len = Math.Min(76, b64.Length - i);
            sb.Append(b64, i, len);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static int FindHeaderEnd(byte[] data)
    {
        for (var i = 0; i <= data.Length - 4; i++)
        {
            if (data[i] == 0x0D && data[i + 1] == 0x0A &&
                data[i + 2] == 0x0D && data[i + 3] == 0x0A)
                return i + 4;
        }
        return -1;
    }

    /// <summary>
    /// MurmurHash3 32-bit implementation compatible with Shodan favicon hashing.
    /// </summary>
    internal static int MurmurHash3(string input)
    {
        var data = Encoding.UTF8.GetBytes(input);
        const uint seed = 0;
        const uint c1 = 0xCC9E2D51;
        const uint c2 = 0x1B873593;
        var length = data.Length;
        var h1 = seed;
        var nblocks = length / 4;

        for (var i = 0; i < nblocks; i++)
        {
            var k1 = BitConverter.ToUInt32(data, i * 4);
            k1 *= c1;
            k1 = RotateLeft(k1, 15);
            k1 *= c2;
            h1 ^= k1;
            h1 = RotateLeft(h1, 13);
            h1 = h1 * 5 + 0xE6546B64;
        }

        var tail = nblocks * 4;
        uint k = 0;
        switch (length & 3)
        {
            case 3: k ^= (uint)data[tail + 2] << 16; goto case 2;
            case 2: k ^= (uint)data[tail + 1] << 8; goto case 1;
            case 1:
                k ^= data[tail];
                k *= c1;
                k = RotateLeft(k, 15);
                k *= c2;
                h1 ^= k;
                break;
        }

        h1 ^= (uint)length;
        h1 = FMix32(h1);
        return unchecked((int)h1);
    }

    private static uint RotateLeft(uint x, int r) => (x << r) | (x >> (32 - r));

    private static uint FMix32(uint h)
    {
        h ^= h >> 16;
        h *= 0x85EBCA6B;
        h ^= h >> 13;
        h *= 0xC2B2AE35;
        h ^= h >> 16;
        return h;
    }

    /// <summary>
    /// Known favicon hashes for common devices and management interfaces.
    /// Values are Shodan-compatible MurmurHash3 of MIME-encoded base64 favicon.
    /// </summary>
    public static readonly Dictionary<int, string> KnownHashes = new()
    {
        // Network equipment
        [-305179312] = "FortiGate (Fortinet)",
        [116323821] = "pfSense Firewall",
        [-1616143106] = "OPNsense Firewall",
        [442749392] = "MikroTik RouterOS",
        [-1588427199] = "Ubiquiti UniFi",
        [81586312] = "Ubiquiti EdgeOS",

        // Hypervisors / Management
        [-1615998030] = "VMware ESXi",
        [-357937467] = "VMware vCenter",
        [-1293290044] = "Proxmox VE",
        [-1293290043] = "Proxmox Backup Server",
        [727726689] = "Dell iDRAC",
        [-586010030] = "HP iLO",

        // NAS
        [-2057830448] = "Synology DSM",
        [999357577] = "IP Camera (Dahua/Hikvision)",
        [1820867002] = "TrueNAS / FreeNAS",

        // Monitoring / DevOps
        [1169437688] = "Grafana",
        [-1331059960] = "Jenkins",
        [1354051610] = "Zabbix",
        [-428790008] = "GitLab",
        [-1950415971] = "Portainer",

        // Smart Home / IoT
        [1410071322] = "Home Assistant",
        [-533826161] = "Pi-hole",

        // Web servers (default pages)
        [1061919793] = "Apache Default",
        [-1137723795] = "Nginx Default",
        [-1273108549] = "IIS Default (Windows)",

        // ISP Routers
        [1838417872] = "Router/Gateway (Freebox)",

        // Consumer Network Equipment
        [-1028703177] = "TP-Link Device",
    };
}
