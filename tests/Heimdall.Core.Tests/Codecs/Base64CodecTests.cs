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
using Heimdall.Core.Codecs;

namespace Heimdall.Core.Tests;

public sealed class Base64CodecTests
{
    [Fact]
    public void Encode_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Base64Codec.Encode(null!, false));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "Zg==")]
    [InlineData("fo", "Zm8=")]
    [InlineData("foo", "Zm9v")]
    [InlineData("foob", "Zm9vYg==")]
    [InlineData("fooba", "Zm9vYmE=")]
    [InlineData("foobar", "Zm9vYmFy")]
    public void Encode_Rfc4648Vectors_ReturnExpectedText(string input, string expected)
    {
        var actual = Base64Codec.Encode(Encoding.UTF8.GetBytes(input), false);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Encode_UrlSafe_RewritesAlphabetAndTrimsPadding()
    {
        var actual = Base64Codec.Encode(new byte[] { 251, 255, 255 }, true);

        Assert.Equal("-___", actual);
    }

    [Fact]
    public void Encode_InsertLineBreaks_RemainsEnabled()
    {
        var input = Enumerable.Repeat((byte)'a', 60).ToArray();

        var actual = Base64Codec.Encode(input, false);

        Assert.Contains(Environment.NewLine, actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_Rfc4648Vector_Foobar_ReturnsBytes()
    {
        var actual = Base64Codec.Decode("Zm9vYmFy", false);

        Assert.Equal("foobar", Encoding.UTF8.GetString(actual));
    }

    [Fact]
    public void Decode_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Base64Codec.Decode(null!, false));
    }

    [Fact]
    public void Decode_UrlSafe_RestoresPadding()
    {
        var actual = Base64Codec.Decode("-___", true);

        Assert.Equal(new byte[] { 251, 255, 255 }, actual);
    }

    [Theory]
    [InlineData("-w", 251)]
    [InlineData("_w", 255)]
    public void Decode_UrlSafe_Mod2Length_RestoresDoublePadding(string input, byte expected)
    {
        var actual = Base64Codec.Decode(input, true);

        Assert.Equal([expected], actual);
    }

    [Fact]
    public void Decode_UrlSafe_Mod3Length_RestoresSinglePadding()
    {
        var actual = Base64Codec.Decode("-_8", true);

        Assert.Equal(new byte[] { 251, 255 }, actual);
    }

    [Fact]
    public void Decode_InvalidInput_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Base64Codec.Decode("not-base64!", false));
    }

    [Fact]
    public void Decode_WithLineBreaks_ReturnsBytes()
    {
        var payload = Enumerable.Repeat((byte)'a', 60).ToArray();
        var encoded = Base64Codec.Encode(payload, false);

        var actual = Base64Codec.Decode(encoded, false);

        Assert.Equal(payload, actual);
    }

    [Fact]
    public void RoundTrip_UrlSafe_RandomPayload()
    {
        var payload = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        var encoded = Base64Codec.Encode(payload, true);
        var decoded = Base64Codec.Decode(encoded, true);

        Assert.Equal(payload, decoded);
    }
}
