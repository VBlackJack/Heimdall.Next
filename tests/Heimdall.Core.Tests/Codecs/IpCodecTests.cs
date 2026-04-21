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

using Heimdall.Core.Codecs;

namespace Heimdall.Core.Tests;

public sealed class IpCodecTests
{
    [Fact]
    public void TryConvert_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => IpCodec.TryConvert(null!, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryConvert_EmptyOrWhitespace_ReturnsFalse(string input)
    {
        Assert.False(IpCodec.TryConvert(input, out var result));
        Assert.Equal(default, result);
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData(" 192.168.1.1 ")]
    [InlineData("3232235777")]
    [InlineData("0xC0A80101")]
    [InlineData("0Xc0a80101")]
    [InlineData("11000000.10101000.00000001.00000001")]
    public void TryConvert_ValidRepresentations_ReturnTrue(string input)
    {
        Assert.True(IpCodec.TryConvert(input, out _));
    }

    [Fact]
    public void TryConvert_DottedIpv4_FormatsAllRepresentations()
    {
        Assert.True(IpCodec.TryConvert("192.168.1.1", out var result));

        Assert.Equal("192.168.1.1", result.Dotted);
        Assert.Equal("3232235777", result.Decimal);
        Assert.Equal("0xC0A80101", result.Hex);
        Assert.Equal("11000000.10101000.00000001.00000001", result.Binary);
        Assert.Equal("::ffff:c0a8:0101", result.MappedIpv6);
    }

    [Fact]
    public void TryConvert_Integer_FormatsAllRepresentations()
    {
        Assert.True(IpCodec.TryConvert("3232235777", out var result));

        Assert.Equal("192.168.1.1", result.Dotted);
        Assert.Equal("3232235777", result.Decimal);
        Assert.Equal("0xC0A80101", result.Hex);
        Assert.Equal("11000000.10101000.00000001.00000001", result.Binary);
        Assert.Equal("::ffff:c0a8:0101", result.MappedIpv6);
    }

    [Fact]
    public void TryConvert_Hex_FormatsAllRepresentations()
    {
        Assert.True(IpCodec.TryConvert("0xC0A80101", out var result));

        Assert.Equal("192.168.1.1", result.Dotted);
        Assert.Equal("3232235777", result.Decimal);
    }

    [Fact]
    public void TryConvert_DottedBinary_FormatsAllRepresentations()
    {
        Assert.True(IpCodec.TryConvert("11000000.10101000.00000001.00000001", out var result));

        Assert.Equal("192.168.1.1", result.Dotted);
        Assert.Equal("3232235777", result.Decimal);
    }

    [Fact]
    public void TryConvert_Quirk_OneDotOneDotOneDotOne_UsesBinaryBranch()
    {
        Assert.True(IpCodec.TryConvert("1.1.1.1", out var result));

        Assert.Equal("1.1.1.1", result.Dotted);
        Assert.Equal("16843009", result.Decimal);
        Assert.Equal("0x01010101", result.Hex);
        Assert.Equal("00000001.00000001.00000001.00000001", result.Binary);
        Assert.Equal("::ffff:0101:0101", result.MappedIpv6);
    }

    [Theory]
    [InlineData("0.0.0.0", "0", "0x00000000", "00000000.00000000.00000000.00000000", "::ffff:0000:0000")]
    [InlineData("255.255.255.255", "4294967295", "0xFFFFFFFF", "11111111.11111111.11111111.11111111", "::ffff:ffff:ffff")]
    [InlineData("127.0.0.1", "2130706433", "0x7F000001", "01111111.00000000.00000000.00000001", "::ffff:7f00:0001")]
    public void TryConvert_Boundaries_AndCommonAddresses_FormatExpected(
        string input,
        string decimalText,
        string hex,
        string binary,
        string mappedIpv6)
    {
        Assert.True(IpCodec.TryConvert(input, out var result));
        Assert.Equal(input, result.Dotted);
        Assert.Equal(decimalText, result.Decimal);
        Assert.Equal(hex, result.Hex);
        Assert.Equal(binary, result.Binary);
        Assert.Equal(mappedIpv6, result.MappedIpv6);
    }

    [Fact]
    public void TryConvert_Quirk_TenDotZeroDotZeroDotOne_UsesBinaryBranch()
    {
        Assert.True(IpCodec.TryConvert("10.0.0.1", out var result));

        Assert.Equal("2.0.0.1", result.Dotted);
        Assert.Equal("33554433", result.Decimal);
        Assert.Equal("0x02000001", result.Hex);
        Assert.Equal("00000010.00000000.00000000.00000001", result.Binary);
        Assert.Equal("::ffff:0200:0001", result.MappedIpv6);
    }

    [Theory]
    [InlineData("2001:db8::1")]
    [InlineData("-1")]
    [InlineData("4294967296")]
    [InlineData("0x100000000")]
    [InlineData("0xZZZZZZZZ")]
    [InlineData("11000000.10101000.00000001")]
    [InlineData("11000000.10101000.00000001.000000012")]
    [InlineData("999.999.999.999")]
    [InlineData("abc")]
    public void TryConvert_InvalidInputs_ReturnFalse(string input)
    {
        Assert.False(IpCodec.TryConvert(input, out var result));
        Assert.Equal(default, result);
    }

    [Fact]
    public void TryConvert_RoundTrips_DottedToDecimalToDotted()
    {
        Assert.True(IpCodec.TryConvert("192.168.1.1", out var first));
        Assert.True(IpCodec.TryConvert(first.Decimal, out var second));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryConvert_RoundTrips_DottedToHexToDotted()
    {
        Assert.True(IpCodec.TryConvert("192.168.1.1", out var first));
        Assert.True(IpCodec.TryConvert(first.Hex, out var second));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryConvert_RoundTrips_DottedToBinaryToDotted()
    {
        Assert.True(IpCodec.TryConvert("192.168.1.1", out var first));
        Assert.True(IpCodec.TryConvert(first.Binary, out var second));

        Assert.Equal(first, second);
    }
}
