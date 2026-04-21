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
using System.Net.NetworkInformation;

namespace Heimdall.Core.Network;

/// <summary>
/// UI-friendly snapshot of a network interface row.
/// </summary>
public sealed record NicSnapshot(
    string Name,
    string InterfaceType,
    string Status,
    string Speed,
    string Mac,
    string Ipv4,
    string Subnet,
    string Gateway,
    string Dhcp);

/// <summary>
/// Pure formatting helpers shared by the network interfaces tool.
/// </summary>
public static class NicFormatter
{
    public static string FormatSpeed(long bitsPerSecond)
    {
        return bitsPerSecond switch
        {
            <= 0 => "-",
            < 1_000_000 => $"{bitsPerSecond / 1_000} Kbps",
            < 1_000_000_000 => $"{bitsPerSecond / 1_000_000} Mbps",
            _ => $"{bitsPerSecond / 1_000_000_000} Gbps",
        };
    }

    public static string FormatMac(PhysicalAddress? mac)
    {
        var bytes = mac?.GetAddressBytes() ?? [];
        return bytes.Length == 0
            ? string.Empty
            : string.Join(":", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    public static string FormatDhcp(bool enabled) => enabled ? "DHCP" : "Static";
}
