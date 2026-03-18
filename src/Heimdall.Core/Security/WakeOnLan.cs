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

using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Heimdall.Core.Security;

/// <summary>
/// Sends Wake-on-LAN magic packets to wake sleeping machines on the network.
/// </summary>
public static partial class WakeOnLan
{
    private static readonly int WolPort = 9;

    /// <summary>
    /// Sends a WOL magic packet to the specified MAC address via UDP broadcast.
    /// </summary>
    /// <param name="macAddress">MAC address in any common format (AA:BB:CC:DD:EE:FF, AA-BB-CC-DD-EE-FF, AABBCCDDEEFF).</param>
    /// <returns>True if the packet was sent successfully.</returns>
    public static async Task<bool> SendAsync(string macAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(macAddress);

        var bytes = ParseMacAddress(macAddress);
        if (bytes is null)
            return false;

        // Magic packet: 6 bytes of 0xFF followed by the MAC address repeated 16 times
        var packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++)
            packet[i] = 0xFF;
        for (int i = 0; i < 16; i++)
            Buffer.BlockCopy(bytes, 0, packet, 6 + i * 6, 6);

        try
        {
            using var client = new UdpClient();
            client.EnableBroadcast = true;
            await client.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, WolPort))
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates whether a string is a valid MAC address format.
    /// </summary>
    public static bool IsValidMac(string? macAddress)
    {
        return !string.IsNullOrWhiteSpace(macAddress) && ParseMacAddress(macAddress) is not null;
    }

    private static byte[]? ParseMacAddress(string mac)
    {
        var cleaned = MacSeparatorRegex().Replace(mac.Trim(), "");
        if (cleaned.Length != 12 || !HexOnlyRegex().IsMatch(cleaned))
            return null;

        var bytes = new byte[6];
        for (int i = 0; i < 6; i++)
            bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
        return bytes;
    }

    [GeneratedRegex("[:\\-\\.]")]
    private static partial Regex MacSeparatorRegex();

    [GeneratedRegex("^[0-9A-Fa-f]+$")]
    private static partial Regex HexOnlyRegex();
}
