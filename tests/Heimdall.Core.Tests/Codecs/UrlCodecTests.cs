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

public sealed class UrlCodecTests
{
    [Fact]
    public void Encode_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => UrlCodec.Encode(null!, false));
    }

    [Fact]
    public void Decode_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => UrlCodec.Decode(null!));
    }

    [Fact]
    public void Encode_ComponentMode_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, UrlCodec.Encode(string.Empty, true));
    }

    [Fact]
    public void Encode_ComponentMode_EncodesReservedCharacters()
    {
        var actual = UrlCodec.Encode("a/b?c=d&e", true);

        Assert.Equal("a%2Fb%3Fc%3Dd%26e", actual);
    }

    [Fact]
    public void Encode_ComponentMode_EncodesUnicode()
    {
        var actual = UrlCodec.Encode("café", true);

        Assert.Equal("caf%C3%A9", actual);
    }

    [Fact]
    public void Encode_ComponentMode_EncodesPercent()
    {
        var actual = UrlCodec.Encode("already%20encoded", true);

        Assert.Equal("already%2520encoded", actual);
    }

    [Fact]
    public void Encode_PreserveStructure_LeavesStructuralCharactersUntouched()
    {
        var actual = UrlCodec.Encode(":/?#&=@%", false);

        Assert.Equal(":/?#&=@%", actual);
    }

    [Fact]
    public void Encode_PreserveStructure_EncodesSegmentsAroundStructuralCharacters()
    {
        var actual = UrlCodec.Encode("https://example.com/a b?x=1&y=2#frag ment", false);

        Assert.Equal("https://example.com/a%20b?x=1&y=2#frag%20ment", actual);
    }

    [Fact]
    public void Encode_PreserveStructure_PreservesPercentSigns()
    {
        var actual = UrlCodec.Encode("https://example.com?q=100%25", false);

        Assert.Equal("https://example.com?q=100%25", actual);
    }

    [Fact]
    public void Encode_PreserveStructure_EncodesPlusButPreservesAt()
    {
        var actual = UrlCodec.Encode("mailto:a+b@example.com?x=1", false);

        Assert.Equal("mailto:a%2Bb@example.com?x=1", actual);
    }

    [Fact]
    public void Decode_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, UrlCodec.Decode(string.Empty));
    }

    [Fact]
    public void Decode_UnescapesUnicode()
    {
        var actual = UrlCodec.Decode("caf%C3%A9");

        Assert.Equal("café", actual);
    }

    [Fact]
    public void Decode_PlusSign_RemainsPlus()
    {
        var actual = UrlCodec.Decode("a+b");

        Assert.Equal("a+b", actual);
    }

    [Fact]
    public void RoundTrip_ComponentMode_ReturnsOriginal()
    {
        const string input = "https://example.com/a b?x=1&y=2#frag";

        var encoded = UrlCodec.Encode(input, true);
        var decoded = UrlCodec.Decode(encoded);

        Assert.Equal(input, decoded);
    }

    [Fact]
    public void RoundTrip_PreserveStructure_ReturnsOriginal()
    {
        const string input = "https://example.com/a b?x=1&y=2#frag ment";

        var encoded = UrlCodec.Encode(input, false);
        var decoded = UrlCodec.Decode(encoded);

        Assert.Equal(input, decoded);
    }
}
