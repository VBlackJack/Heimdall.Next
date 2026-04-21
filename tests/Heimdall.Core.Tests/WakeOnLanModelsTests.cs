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

using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public sealed class WakeOnLanModelsTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF")]
    [InlineData("aa-bb-cc-dd-ee-ff")]
    [InlineData("AABB.CCDD.EEFF")]
    [InlineData("aabbccddeeff")]
    public void TryParse_ValidFormats_ReturnsBytes(string input)
    {
        var success = MacAddressParser.TryParse(input, out var bytes);

        Assert.True(success);
        Assert.Equal([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF], bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("AA:BB:CC:DD:EE")]
    [InlineData("AA:BB:CC:DD:EE:FG")]
    [InlineData("GGGGGGGGGGGG")]
    public void TryParse_InvalidFormats_ReturnsFalse(string? input)
    {
        var success = MacAddressParser.TryParse(input, out var bytes);

        Assert.False(success);
        Assert.Empty(bytes);
    }

    [Fact]
    public void TryNormalize_ValidInput_ReturnsCanonicalUppercase()
    {
        var success = MacAddressParser.TryNormalize("aa-bb-cc-dd-ee-ff", out var normalized);

        Assert.True(success);
        Assert.Equal("AA:BB:CC:DD:EE:FF", normalized);
    }

    [Fact]
    public void TryNormalize_InvalidInput_ReturnsFalse()
    {
        var success = MacAddressParser.TryNormalize("not-a-mac", out var normalized);

        Assert.False(success);
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void Format_InvalidLength_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => MacAddressParser.Format([1, 2, 3]));
        Assert.Contains("6 bytes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MagicPacketBuilder_Build_Produces102BytePacket()
    {
        var packet = MagicPacketBuilder.Build([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF]);

        Assert.Equal(MagicPacketBuilder.PacketLength, packet.Length);
        Assert.All(packet.Take(6), b => Assert.Equal((byte)0xFF, b));
    }

    [Fact]
    public void MagicPacketBuilder_Build_RepeatsMacSixteenTimes()
    {
        var mac = new byte[] { 1, 2, 3, 4, 5, 6 };
        var packet = MagicPacketBuilder.Build(mac);

        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(mac, packet.Skip(6 + (i * 6)).Take(6));
        }
    }

    [Fact]
    public void MagicPacketBuilder_Build_InvalidLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => MagicPacketBuilder.Build([1, 2, 3]));
    }

    [Fact]
    public void WakeOnLanResult_SentFactory_SetsSuccess()
    {
        var result = WakeOnLanResult.Sent("AA:BB:CC:DD:EE:FF", "255.255.255.255", 9);

        Assert.True(result.Success);
        Assert.Equal(WakeOnLanStatusKind.Sent, result.StatusKind);
        Assert.Null(result.ErrorKey);
        Assert.Null(result.ErrorArg);
    }

    [Fact]
    public void WakeOnLanResult_ErrorFactory_NormalizesNullArgument()
    {
        var result = WakeOnLanResult.Error("AA:BB:CC:DD:EE:FF", "255.255.255.255", 9, "ToolWolErrorSocket", null);

        Assert.False(result.Success);
        Assert.Equal(WakeOnLanStatusKind.Error, result.StatusKind);
        Assert.Equal(string.Empty, result.ErrorArg);
    }
}
