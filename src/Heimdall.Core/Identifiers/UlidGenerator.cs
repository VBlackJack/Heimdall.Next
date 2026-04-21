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

public static class UlidGenerator
{
    public const int TextLength = 26;
    public const int RandomByteCount = 10;
    internal const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const long MaxTimestamp = 0xFFFFFFFFFFFFL;

    public static string Generate()
    {
        var (timestampMs, random) = GenerateParts();
        return Encode(timestampMs, random);
    }

    internal static string Encode(long timestampMs, ReadOnlySpan<byte> random10)
    {
        if (timestampMs < 0 || timestampMs > MaxTimestamp)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampMs));
        }

        if (random10.Length != RandomByteCount)
        {
            throw new ArgumentException("ULID random payload must be exactly 10 bytes.", nameof(random10));
        }

        Span<char> buffer = stackalloc char[TextLength];
        var timestamp = (ulong)timestampMs;
        for (var i = 0; i < 10; i++)
        {
            var shift = 45 - (i * 5);
            buffer[i] = Alphabet[(int)((timestamp >> shift) & 0x1FUL)];
        }

        ulong upper40 =
            ((ulong)random10[0] << 32) |
            ((ulong)random10[1] << 24) |
            ((ulong)random10[2] << 16) |
            ((ulong)random10[3] << 8) |
            random10[4];
        ulong lower40 =
            ((ulong)random10[5] << 32) |
            ((ulong)random10[6] << 24) |
            ((ulong)random10[7] << 16) |
            ((ulong)random10[8] << 8) |
            random10[9];

        for (var i = 0; i < 8; i++)
        {
            var shift = 35 - (i * 5);
            buffer[10 + i] = Alphabet[(int)((upper40 >> shift) & 0x1FUL)];
            buffer[18 + i] = Alphabet[(int)((lower40 >> shift) & 0x1FUL)];
        }

        return new string(buffer);
    }

    internal static (long TimestampMs, byte[] Random) GenerateParts()
    {
        var random = new byte[RandomByteCount];
        RandomNumberGenerator.Fill(random);
        return (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), random);
    }
}
