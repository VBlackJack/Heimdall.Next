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

using System.Text.RegularExpressions;
using Heimdall.Core.Identifiers;

namespace Heimdall.Core.Tests.Identifiers;

public sealed class UlidGeneratorTests
{
    [Fact]
    public void Encode_AllZero_ProducesAllZeroAlphabet()
    {
        Assert.Equal("00000000000000000000000000", UlidGenerator.Encode(0, new byte[UlidGenerator.RandomByteCount]));
    }

    [Fact]
    public void Encode_KnownTimestamp_ProducesExpectedPrefix()
    {
        Assert.Equal("0000000010", UlidGenerator.Encode(32, new byte[UlidGenerator.RandomByteCount])[..10]);
    }

    [Fact]
    public void Encode_MaxTimestamp_ProducesUpperBoundPrefix()
    {
        Assert.Equal("7ZZZZZZZZZ", UlidGenerator.Encode(0xFFFFFFFFFFFFL, new byte[UlidGenerator.RandomByteCount])[..10]);
    }

    [Fact]
    public void Encode_AllOnesRandom_ProducesExpectedSuffix()
    {
        Assert.Equal("ZZZZZZZZZZZZZZZZ", UlidGenerator.Encode(0, Enumerable.Repeat((byte)0xFF, UlidGenerator.RandomByteCount).ToArray())[10..]);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(11)]
    public void Encode_InvalidRandomLength_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => UlidGenerator.Encode(0, new byte[length]));
    }

    [Fact]
    public void Generate_ReturnsStringOfLength26()
    {
        Assert.Equal(UlidGenerator.TextLength, UlidGenerator.Generate().Length);
    }

    [Fact]
    public void Generate_UsesUppercaseCrockfordAlphabet()
    {
        Assert.Matches(new Regex("^[0-9A-HJKMNP-TV-Z]{26}$"), UlidGenerator.Generate());
    }

    [Fact]
    public void Generate_ProducesDistinctValuesAcrossHundredCalls()
    {
        var values = Enumerable.Range(0, 100).Select(_ => UlidGenerator.Generate()).ToArray();
        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Generate_TimestampPrefixMatchesCurrentTime()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var value = UlidGenerator.Generate();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var timestamp = DecodeTimestamp(value[..10]);

        Assert.InRange(timestamp, before - 2000, after + 2000);
    }

    [Fact]
    public void Generate_IsLexicographicallyOrderedAcrossTime()
    {
        var values = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            values.Add(UlidGenerator.Generate());
            Thread.Sleep(5);
        }

        var sorted = values.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(values, sorted);
    }

    [Fact]
    public void GenerateParts_ReturnsTenRandomBytes()
    {
        var parts = UlidGenerator.GenerateParts();
        Assert.Equal(UlidGenerator.RandomByteCount, parts.Random.Length);
    }

    [Fact]
    public void GenerateParts_TimestampIsCloseToUtcNow()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var parts = UlidGenerator.GenerateParts();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.InRange(parts.TimestampMs, before - 2000, after + 2000);
    }

    private static long DecodeTimestamp(string text)
    {
        long value = 0;
        foreach (var c in text)
        {
            value = (value << 5) | (uint)UlidGenerator.Alphabet.IndexOf(c);
        }

        return value;
    }
}
