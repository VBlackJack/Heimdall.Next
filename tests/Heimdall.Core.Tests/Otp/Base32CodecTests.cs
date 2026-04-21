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

using System.Text;
using Heimdall.Core.Otp;

namespace Heimdall.Core.Tests;

public sealed class Base32CodecTests
{
    [Fact]
    public void Decode_Null_ReturnsEmpty()
    {
        Assert.Empty(Base32Codec.Decode(null!));
    }

    [Fact]
    public void Decode_Empty_ReturnsEmpty()
    {
        Assert.Empty(Base32Codec.Decode(string.Empty));
    }

    [Fact]
    public void Decode_KnownVector_HelloWorld()
    {
        var actual = Base32Codec.Decode("JBSWY3DPEB3W64TMMQ======");

        Assert.Equal("Hello world", Encoding.UTF8.GetString(actual));
    }

    [Theory]
    [InlineData("MY======", "f")]
    [InlineData("MZXQ====", "fo")]
    [InlineData("MZXW6===", "foo")]
    [InlineData("MZXW6YQ=", "foob")]
    [InlineData("MZXW6YTB", "fooba")]
    [InlineData("MZXW6YTBOI======", "foobar")]
    public void Decode_Rfc4648Vectors_ReturnExpectedString(string encoded, string expected)
    {
        var actual = Base32Codec.Decode(encoded);

        Assert.Equal(expected, Encoding.UTF8.GetString(actual));
    }

    [Fact]
    public void Decode_Lowercase_IsCaseInsensitive()
    {
        var actual = Base32Codec.Decode("jbswy3dpeb3w64tmmq======");

        Assert.Equal("Hello world", Encoding.UTF8.GetString(actual));
    }

    [Fact]
    public void Decode_InvalidChar_ThrowsFormatException()
    {
        var ex = Assert.Throws<FormatException>(() => Base32Codec.Decode("ABC1"));

        Assert.Equal("Invalid Base32 character: 1", ex.Message);
    }

    [Fact]
    public void Decode_StripsTrailingPadding()
    {
        var withPadding = Base32Codec.Decode("AAAA====");
        var withoutPadding = Base32Codec.Decode("AAAA");

        Assert.Equal(withPadding, withoutPadding);
    }

    [Fact]
    public void Decode_SpaceChar_Throws()
    {
        var ex = Assert.Throws<FormatException>(() => Base32Codec.Decode("A A"));

        Assert.Equal("Invalid Base32 character:  ", ex.Message);
    }
}
