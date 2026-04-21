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

using Heimdall.Core.Identifiers;

namespace Heimdall.Core.Tests;

public sealed class UuidGeneratorTests
{
    [Fact]
    public void Generate_V4_ReturnsNonEmptyGuid()
    {
        Assert.NotEqual(Guid.Empty, UuidGenerator.Generate(UuidVersion.V4));
    }

    [Fact]
    public void Generate_V4_UniqueAcrossRepeatedCalls()
    {
        var values = Enumerable.Range(0, 100).Select(_ => UuidGenerator.Generate(UuidVersion.V4)).ToHashSet();
        Assert.Equal(100, values.Count);
    }

    [Fact]
    public void Generate_V4_VersionNibble_Is4()
    {
        Assert.Equal('4', UuidGenerator.Generate(UuidVersion.V4).ToString("D")[14]);
    }

    [Fact]
    public void Generate_V7_VersionNibble_Is7()
    {
        Assert.Equal('7', UuidGenerator.Generate(UuidVersion.V7).ToString("D")[14]);
    }

    [Fact]
    public void Generate_V7_VariantBits_Are10xx()
    {
        var variant = char.ToLowerInvariant(UuidGenerator.Generate(UuidVersion.V7).ToString("D")[19]);
        Assert.Contains(variant, "89ab");
    }

    [Fact]
    public void Generate_V7_TimestampIsCloseToUtcNow()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = UuidGenerator.Generate(UuidVersion.V7);
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var timestamp = ExtractUnixMilliseconds(guid);

        Assert.InRange(timestamp, before - 2000, after + 2000);
    }

    [Fact]
    public void Generate_V7_MonotonicAcrossMilliseconds()
    {
        var first = UuidGenerator.Generate(UuidVersion.V7);
        Thread.Sleep(2);
        var second = UuidGenerator.Generate(UuidVersion.V7);

        Assert.True(ExtractUnixMilliseconds(second) >= ExtractUnixMilliseconds(first));
    }

    [Fact]
    public void Generate_V7_RandomBitsVary()
    {
        var tails = Enumerable.Range(0, 20).Select(_ =>
        {
            var bytes = UuidGenerator.Generate(UuidVersion.V7).ToByteArray(bigEndian: true);
            bytes[6] &= 0x0F;
            bytes[8] &= 0x3F;
            return Convert.ToHexString(bytes[6..]);
        }).Distinct().Count();

        Assert.True(tails >= 15);
    }

    [Fact]
    public void Generate_InvalidVersion_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UuidGenerator.Generate((UuidVersion)99));
    }

    [Fact]
    public void Format_DefaultFormat_ProducesLowercaseWithHyphens()
    {
        var text = UuidGenerator.Format(Guid.Parse("A1B2C3D4-E5F6-47A8-9123-ABCDEF123456"), UuidFormat.Default);

        Assert.Equal("a1b2c3d4-e5f6-47a8-9123-abcdef123456", text);
    }

    [Fact]
    public void Format_UppercaseWithHyphens_ProducesUppercaseWithHyphens()
    {
        var text = UuidGenerator.Format(Guid.Parse("a1b2c3d4-e5f6-47a8-9123-abcdef123456"), new UuidFormat(true, true));

        Assert.Equal("A1B2C3D4-E5F6-47A8-9123-ABCDEF123456", text);
    }

    [Fact]
    public void Format_LowercaseNoHyphens_ProducesCompactLower()
    {
        var text = UuidGenerator.Format(Guid.Parse("A1B2C3D4-E5F6-47A8-9123-ABCDEF123456"), new UuidFormat(false, false));

        Assert.Equal("a1b2c3d4e5f647a89123abcdef123456", text);
    }

    [Fact]
    public void Format_UppercaseNoHyphens_ProducesCompactUpper()
    {
        var text = UuidGenerator.Format(Guid.Parse("a1b2c3d4-e5f6-47a8-9123-abcdef123456"), new UuidFormat(true, false));

        Assert.Equal("A1B2C3D4E5F647A89123ABCDEF123456", text);
    }

    [Fact]
    public void Format_PreservesGuidIdentity_Roundtrip()
    {
        var guid = Guid.NewGuid();
        var text = UuidGenerator.Format(guid, new UuidFormat(true, false));

        Assert.Equal(guid, Guid.Parse(text));
    }

    [Fact]
    public void Format_ZeroGuid_FormatsAsAllZeros()
    {
        Assert.Equal("00000000-0000-0000-0000-000000000000", UuidGenerator.Format(Guid.Empty, UuidFormat.Default));
    }

    private static long ExtractUnixMilliseconds(Guid guid)
    {
        var bytes = guid.ToByteArray(bigEndian: true);
        return ((long)bytes[0] << 40)
            | ((long)bytes[1] << 32)
            | ((long)bytes[2] << 24)
            | ((long)bytes[3] << 16)
            | ((long)bytes[4] << 8)
            | bytes[5];
    }
}
