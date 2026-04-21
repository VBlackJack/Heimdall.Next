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

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Heimdall.Core.Network;

/// <summary>
/// Current status of a Wake-on-LAN send operation.
/// </summary>
public enum WakeOnLanStatusKind
{
    None,
    Sending,
    Sent,
    Error,
}

/// <summary>
/// Raw input for a Wake-on-LAN send attempt.
/// </summary>
public sealed record WakeOnLanRequest(string MacAddress, string BroadcastAddress, int Port);

/// <summary>
/// Outcome of a Wake-on-LAN send attempt.
/// </summary>
public sealed record WakeOnLanResult(
    string MacAddress,
    string BroadcastAddress,
    int Port,
    WakeOnLanStatusKind StatusKind,
    string? ErrorKey,
    string? ErrorArg)
{
    public bool Success => StatusKind == WakeOnLanStatusKind.Sent;

    public static WakeOnLanResult Sent(string macAddress, string broadcastAddress, int port)
        => new(macAddress, broadcastAddress, port, WakeOnLanStatusKind.Sent, null, null);

    public static WakeOnLanResult Error(
        string macAddress,
        string broadcastAddress,
        int port,
        string errorKey,
        string? errorArg = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorKey);
        return new(
            macAddress ?? string.Empty,
            broadcastAddress ?? string.Empty,
            port,
            WakeOnLanStatusKind.Error,
            errorKey,
            errorArg ?? string.Empty);
    }
}

/// <summary>
/// Parses user-supplied MAC addresses from common formats.
/// </summary>
public static partial class MacAddressParser
{
    public static bool TryParse(string? input, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var hex = SeparatorRegex().Replace(input.Trim(), string.Empty);
        if (hex.Length != 12 || !HexRegex().IsMatch(hex))
        {
            return false;
        }

        bytes = new byte[6];
        for (var i = 0; i < 6; i++)
        {
            bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return true;
    }

    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (!TryParse(input, out var bytes))
        {
            return false;
        }

        normalized = Format(bytes);
        return true;
    }

    public static string Format(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length != 6)
        {
            throw new ArgumentException("MAC address must contain exactly 6 bytes.", nameof(bytes));
        }

        var builder = new StringBuilder(17);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(':');
            }

            builder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    [GeneratedRegex("[:\\-.]")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex("^[0-9A-Fa-f]{12}$")]
    private static partial Regex HexRegex();
}

/// <summary>
/// Builds Wake-on-LAN magic packets from normalized MAC bytes.
/// </summary>
public static class MagicPacketBuilder
{
    public const int PacketLength = 102;

    public static byte[] Build(byte[] macAddressBytes)
    {
        ArgumentNullException.ThrowIfNull(macAddressBytes);
        if (macAddressBytes.Length != 6)
        {
            throw new ArgumentException("MAC address must contain exactly 6 bytes.", nameof(macAddressBytes));
        }

        var packet = new byte[PacketLength];
        for (var i = 0; i < 6; i++)
        {
            packet[i] = 0xFF;
        }

        for (var i = 0; i < 16; i++)
        {
            Buffer.BlockCopy(macAddressBytes, 0, packet, 6 + (i * 6), 6);
        }

        return packet;
    }
}
