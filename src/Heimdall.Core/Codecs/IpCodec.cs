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
using System.Net;
using System.Net.Sockets;

namespace Heimdall.Core.Codecs;

public readonly record struct IpConversionResult(
    string Dotted,
    string Decimal,
    string Hex,
    string Binary,
    string MappedIpv6);

public static class IpCodec
{
    public static bool TryConvert(string input, out IpConversionResult result)
    {
        ArgumentNullException.ThrowIfNull(input);

        var trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || !TryParseToUint32(trimmed, out var value))
        {
            result = default;
            return false;
        }

        result = Format(value);
        return true;
    }

    private static bool TryParseToUint32(string input, out uint value)
    {
        value = 0;

        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(input.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        if (input.Contains('.') && input.Replace(".", "", StringComparison.Ordinal).All(c => c is '0' or '1') &&
            input.Split('.') is { Length: 4 } binaryParts)
        {
            try
            {
                var b0 = Convert.ToByte(binaryParts[0], 2);
                var b1 = Convert.ToByte(binaryParts[1], 2);
                var b2 = Convert.ToByte(binaryParts[2], 2);
                var b3 = Convert.ToByte(binaryParts[3], 2);
                value = ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
                return true;
            }
            catch
            {
                return false;
            }
        }

        if (IPAddress.TryParse(input, out var addr) && addr.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = addr.GetAddressBytes();
            value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            return true;
        }

        return uint.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static IpConversionResult Format(uint value)
    {
        var bytes = new byte[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value,
        };

        return new IpConversionResult(
            new IPAddress(bytes).ToString(),
            value.ToString(CultureInfo.InvariantCulture),
            $"0x{value:X8}",
            string.Join(".",
                Convert.ToString(bytes[0], 2).PadLeft(8, '0'),
                Convert.ToString(bytes[1], 2).PadLeft(8, '0'),
                Convert.ToString(bytes[2], 2).PadLeft(8, '0'),
                Convert.ToString(bytes[3], 2).PadLeft(8, '0')),
            $"::ffff:{bytes[0]:x02}{bytes[1]:x02}:{bytes[2]:x02}{bytes[3]:x02}");
    }
}
