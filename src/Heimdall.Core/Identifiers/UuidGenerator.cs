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

namespace Heimdall.Core.Identifiers;

public enum UuidVersion
{
    V4,
    V7,
}

public readonly record struct UuidFormat(bool Uppercase, bool WithHyphens)
{
    public static UuidFormat Default { get; } = new(false, true);
}

public static class UuidGenerator
{
    public static Guid Generate(UuidVersion version) => version switch
    {
        UuidVersion.V4 => Guid.NewGuid(),
        UuidVersion.V7 => GenerateV7(),
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
    };

    public static string Format(Guid guid, UuidFormat format)
    {
        var specifier = format.WithHyphens ? "D" : "N";
        var text = guid.ToString(specifier);
        return format.Uppercase ? text.ToUpperInvariant() : text;
    }

    private static Guid GenerateV7()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(unixMs >> 40);
        bytes[1] = (byte)(unixMs >> 32);
        bytes[2] = (byte)(unixMs >> 24);
        bytes[3] = (byte)(unixMs >> 16);
        bytes[4] = (byte)(unixMs >> 8);
        bytes[5] = (byte)unixMs;
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
