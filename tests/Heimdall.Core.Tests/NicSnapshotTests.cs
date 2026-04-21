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

using System.Net.NetworkInformation;
using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public sealed class NicSnapshotTests
{
    [Theory]
    [InlineData(-1, "-")]
    [InlineData(0, "-")]
    [InlineData(999_000, "999 Kbps")]
    [InlineData(1_000_000, "1 Mbps")]
    [InlineData(999_000_000, "999 Mbps")]
    [InlineData(1_000_000_000, "1 Gbps")]
    public void FormatSpeed_FormatsAsExpected(long bitsPerSecond, string expected)
    {
        Assert.Equal(expected, NicFormatter.FormatSpeed(bitsPerSecond));
    }

    [Fact]
    public void FormatMac_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, NicFormatter.FormatMac(null));
    }

    [Fact]
    public void FormatMac_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, NicFormatter.FormatMac(new PhysicalAddress([])));
    }

    [Fact]
    public void FormatMac_FormatsUppercaseColonSeparated()
    {
        Assert.Equal("AA:BB:CC:00:11:22", NicFormatter.FormatMac(new PhysicalAddress([0xAA, 0xBB, 0xCC, 0x00, 0x11, 0x22])));
    }

    [Theory]
    [InlineData(true, "DHCP")]
    [InlineData(false, "Static")]
    public void FormatDhcp_ReturnsExpected(bool enabled, string expected)
    {
        Assert.Equal(expected, NicFormatter.FormatDhcp(enabled));
    }

    [Fact]
    public void RecordEquality_Works()
    {
        var left = new NicSnapshot("eth0", "Ethernet", "Up", "1 Gbps", "AA:BB", "192.0.2.1", "255.255.255.0", "192.0.2.254", "DHCP");
        var right = new NicSnapshot("eth0", "Ethernet", "Up", "1 Gbps", "AA:BB", "192.0.2.1", "255.255.255.0", "192.0.2.254", "DHCP");

        Assert.Equal(left, right);
    }
}
