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

namespace Heimdall.Ssh;

/// <summary>
/// Shared local loopback binding constants and validation helpers.
/// </summary>
public static class LoopbackBinding
{
    public const string DefaultHost = "127.0.0.1";
    public const int FirstAliasOctet = 2;
    public const int LastAliasOctet = 254;

    private const string AliasPrefix = "127.0.0.";

    public static string FormatAlias(int hostOctet)
    {
        if (hostOctet is < FirstAliasOctet or > LastAliasOctet)
        {
            throw new ArgumentOutOfRangeException(nameof(hostOctet));
        }

        return $"{AliasPrefix}{hostOctet}";
    }

    public static bool IsDefaultHost(string? host)
        => TryNormalizeHost(host, out string normalized)
           && string.Equals(normalized, DefaultHost, StringComparison.Ordinal);

    public static string NormalizeHost(string? host)
    {
        if (!TryNormalizeHost(host, out string normalized))
        {
            throw new ArgumentException(
                $"Local bind host must be an IPv4 loopback address in the 127.0.0.1-127.0.0.254 range: {host}",
                nameof(host));
        }

        return normalized;
    }

    public static bool TryNormalizeHost(string? host, out string normalized)
    {
        normalized = DefaultHost;
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        if (!IPAddress.TryParse(host.Trim(), out IPAddress? ipAddress)
            || ipAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] bytes = ipAddress.GetAddressBytes();
        if (bytes[0] != 127 || bytes[1] != 0 || bytes[2] != 0)
        {
            return false;
        }

        if (bytes[3] is < 1 or > LastAliasOctet)
        {
            return false;
        }

        normalized = $"{AliasPrefix}{bytes[3]}";
        return true;
    }
}
