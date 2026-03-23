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

using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Heimdall.Core.Discovery;

/// <summary>
/// Computes the HASSH fingerprint of an SSH server by parsing the KEX_INIT
/// message to extract supported algorithm lists.
/// HASSH = MD5(kex_algorithms;encryption_server;mac_server;compression_server).
/// </summary>
public static class SshFingerprinter
{
    private const byte SshMsgKexInit = 20;

    /// <summary>
    /// Connects to an SSH server, reads the banner and KEX_INIT message,
    /// and returns the HASSH fingerprint string.
    /// </summary>
    public static async Task<string?> ComputeHashAsync(
        string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

            await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            stream.ReadTimeout = timeoutMs;

            // Read server banner (e.g., "SSH-2.0-OpenSSH_8.9p1\r\n")
            var bannerBuf = new byte[512];
            var bannerLen = 0;
            while (bannerLen < bannerBuf.Length)
            {
                var b = new byte[1];
                var n = await stream.ReadAsync(b, linked.Token).ConfigureAwait(false);
                if (n == 0) return null;
                bannerBuf[bannerLen++] = b[0];
                if (b[0] == 0x0A) break; // LF = end of banner
            }

            // Send our own banner to trigger KEX_INIT from server
            var clientBanner = "SSH-2.0-Heimdall_Scanner\r\n"u8.ToArray();
            await stream.WriteAsync(clientBanner, linked.Token).ConfigureAwait(false);
            await stream.FlushAsync(linked.Token).ConfigureAwait(false);

            // Read SSH binary packet containing KEX_INIT
            var kexInit = await ReadSshPacketAsync(stream, linked.Token).ConfigureAwait(false);
            if (kexInit is null || kexInit.Length < 17) return null;
            if (kexInit[0] != SshMsgKexInit) return null;

            // Parse KEX_INIT: skip cookie (16 bytes), then read name-lists
            var offset = 17; // 1 (msg type) + 16 (cookie)
            var kexAlgorithms = ReadNameList(kexInit, ref offset);
            var hostKeyAlgorithms = ReadNameList(kexInit, ref offset); // skip
            var encClientToServer = ReadNameList(kexInit, ref offset); // skip
            var encServerToClient = ReadNameList(kexInit, ref offset);
            var macClientToServer = ReadNameList(kexInit, ref offset); // skip
            var macServerToClient = ReadNameList(kexInit, ref offset);
            var compClientToServer = ReadNameList(kexInit, ref offset); // skip
            var compServerToClient = ReadNameList(kexInit, ref offset);

            if (kexAlgorithms is null || encServerToClient is null ||
                macServerToClient is null || compServerToClient is null)
                return null;

            // HASSH = MD5(kex;enc_s2c;mac_s2c;comp_s2c)
            var hasshInput = $"{kexAlgorithms};{encServerToClient};{macServerToClient};{compServerToClient}";
            var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(hasshInput));
            return Convert.ToHexStringLower(hashBytes);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]?> ReadSshPacketAsync(
        NetworkStream stream, CancellationToken ct)
    {
        // SSH binary packet: uint32 packet_length + byte padding_length + payload + padding
        var header = new byte[4];
        var read = 0;
        while (read < 4)
        {
            var n = await stream.ReadAsync(header.AsMemory(read, 4 - read), ct)
                .ConfigureAwait(false);
            if (n == 0) return null;
            read += n;
        }

        var packetLen = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        if (packetLen is <= 0 or > 65536) return null;

        var packet = new byte[packetLen];
        read = 0;
        while (read < packetLen)
        {
            var n = await stream.ReadAsync(packet.AsMemory(read, packetLen - read), ct)
                .ConfigureAwait(false);
            if (n == 0) return null;
            read += n;
        }

        // Skip padding_length byte, return payload
        var paddingLen = packet[0];
        if (paddingLen >= packetLen) return null;
        var payloadLen = packetLen - 1 - paddingLen;
        if (payloadLen <= 0) return null;
        return packet.AsSpan(1, payloadLen).ToArray();
    }

    /// <summary>
    /// Reads an SSH name-list (uint32 length + comma-separated ASCII string).
    /// </summary>
    private static string? ReadNameList(byte[] data, ref int offset)
    {
        if (offset + 4 > data.Length) return null;
        var len = (data[offset] << 24) | (data[offset + 1] << 16) |
                  (data[offset + 2] << 8) | data[offset + 3];
        offset += 4;
        if (len < 0 || offset + len > data.Length) return null;
        var result = len > 0 ? Encoding.ASCII.GetString(data, offset, len) : "";
        offset += len;
        return result;
    }
}
