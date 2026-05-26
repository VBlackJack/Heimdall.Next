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
using FluentAssertions;
using Heimdall.Terminal.Logging;

namespace Heimdall.Terminal.Tests.Logging;

public sealed class StreamingUtf8DecoderTests
{
    [Fact]
    public void DecodeChunk_WithEmptyChunk_ReturnsEmptyString()
    {
        StreamingUtf8Decoder decoder = new StreamingUtf8Decoder();

        string result = decoder.DecodeChunk(ReadOnlySpan<byte>.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DecodeChunk_WithPureAscii_ReturnsDecodedText()
    {
        StreamingUtf8Decoder decoder = new StreamingUtf8Decoder();

        string result = decoder.DecodeChunk(Utf8("Hello, world!"));

        result.Should().Be("Hello, world!");
    }

    [Fact]
    public void DecodeChunk_WithCompleteTwoByteCharacter_ReturnsDecodedText()
    {
        StreamingUtf8Decoder decoder = new StreamingUtf8Decoder();

        string result = decoder.DecodeChunk(new byte[] { 0xC3, 0xA9 });

        result.Should().Be("\u00E9");
    }

    [Fact]
    public void DecodeChunk_WithFragmentedTwoByteCharacter_ReturnsDecodedTextAfterSecondChunk()
    {
        StreamingUtf8Decoder decoder = new StreamingUtf8Decoder();

        string first = decoder.DecodeChunk(new byte[] { 0xC3 });
        string second = decoder.DecodeChunk(new byte[] { 0xA9 });

        first.Should().BeEmpty();
        second.Should().Be("\u00E9");
        (first + second).Should().Be("\u00E9");
    }

    [Fact]
    public void DecodeChunk_WithFragmentedFourByteCharacterAcrossTwoChunks_ReturnsDecodedText()
    {
        StreamingUtf8Decoder decoder = new StreamingUtf8Decoder();

        string first = decoder.DecodeChunk(new byte[] { 0xF0, 0x9F });
        string second = decoder.DecodeChunk(new byte[] { 0xA6, 0x80 });

        first.Should().BeEmpty();
        second.Should().Be("\U0001F980");
    }

    [Fact]
    public void DecodeChunk_WithFragmentedFourByteCharacterAcrossFourChunks_ReturnsDecodedText()
    {
        StreamingUtf8Decoder decoder = new StreamingUtf8Decoder();

        string first = decoder.DecodeChunk(new byte[] { 0xF0 });
        string second = decoder.DecodeChunk(new byte[] { 0x9F });
        string third = decoder.DecodeChunk(new byte[] { 0xA6 });
        string fourth = decoder.DecodeChunk(new byte[] { 0x80 });

        first.Should().BeEmpty();
        second.Should().BeEmpty();
        third.Should().BeEmpty();
        fourth.Should().Be("\U0001F980");
    }

    [Fact]
    public void DecodeChunk_WithByteByByteMixedString_ReturnsOriginalText()
    {
        StreamingUtf8Decoder decoder = new StreamingUtf8Decoder();
        string original = BuildMixedString();
        byte[] bytes = Utf8(original);
        StringBuilder decoded = new StringBuilder();

        foreach (byte value in bytes)
        {
            decoded.Append(decoder.DecodeChunk(new byte[] { value }));
        }

        decoded.ToString().Should().Be(original);
    }

    [Fact]
    public void Flush_AfterCompleteStream_ReturnsEmptyString()
    {
        StreamingUtf8Decoder decoder = new StreamingUtf8Decoder();

        string decoded = decoder.DecodeChunk(new byte[] { 0xC3, 0xA9 });
        string residue = decoder.Flush();

        decoded.Should().Be("\u00E9");
        residue.Should().BeEmpty();
    }

    [Fact]
    public void Flush_AfterPendingFragment_ReturnsReplacementCharacter()
    {
        StreamingUtf8Decoder decoder = new StreamingUtf8Decoder();

        string decoded = decoder.DecodeChunk(new byte[] { 0xC3 });
        string residue = decoder.Flush();

        decoded.Should().BeEmpty();
        residue.Should().NotBeEmpty();
        residue.Should().Contain("\uFFFD");
    }

    [Fact]
    public void Reset_AfterPendingFragment_ClearsDecoderState()
    {
        StreamingUtf8Decoder decoder = new StreamingUtf8Decoder();

        string pending = decoder.DecodeChunk(new byte[] { 0xC3 });
        decoder.Reset();
        string decoded = decoder.DecodeChunk(new byte[] { 0xC3, 0xA9 });

        pending.Should().BeEmpty();
        decoded.Should().Be("\u00E9");
    }

    private static byte[] Utf8(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    private static string BuildMixedString()
    {
        StringBuilder builder = new StringBuilder();
        for (int index = 0; index < 170; index++)
        {
            builder.Append("A");
            builder.Append("\u00E9");
            builder.Append("\u00FC");
            builder.Append("\u6F22");
            builder.Append("\U0001F980");
        }

        builder.Append("A");
        builder.Append("\u00E9");
        builder.Append("\u00FC");
        builder.Append("\u6F22");

        return builder.ToString();
    }
}
