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

using System.Net;
using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public sealed class PortMapperTests
{
    [Fact]
    public void FromTcp4Row_MapsFields()
    {
        var row = new Tcp4RawRow(0x010200C0, ToNetworkPort(443), 0x020200C0, ToNetworkPort(12345), 5, 42);

        var entry = PortMapper.FromTcp4Row(row, pid => $"proc-{pid}");

        Assert.Equal("TCP", entry.Protocol);
        Assert.Equal(new IPAddress(row.LocalAddr).ToString(), entry.LocalAddress);
        Assert.Equal(443, entry.LocalPort);
        Assert.Equal(new IPAddress(row.RemoteAddr).ToString(), entry.RemoteAddress);
        Assert.Equal(12345, entry.RemotePort);
        Assert.Equal("ESTABLISHED", entry.State);
        Assert.Equal(42, entry.Pid);
        Assert.Equal("proc-42", entry.ProcessName);
    }

    [Fact]
    public void FromUdp4Row_MapsFields()
    {
        var row = new Udp4RawRow(0x010200C0, ToNetworkPort(53), 7);

        var entry = PortMapper.FromUdp4Row(row, pid => $"proc-{pid}");

        Assert.Equal("UDP", entry.Protocol);
        Assert.Equal(new IPAddress(row.LocalAddr).ToString(), entry.LocalAddress);
        Assert.Equal(53, entry.LocalPort);
        Assert.Equal("*", entry.RemoteAddress);
        Assert.Equal(0, entry.RemotePort);
        Assert.Equal("LISTENING", entry.State);
        Assert.Equal("proc-7", entry.ProcessName);
    }

    [Fact]
    public void FromTcp6Row_MapsFields()
    {
        var row = new Tcp6RawRow(IPAddress.IPv6Loopback.GetAddressBytes(), ToNetworkPort(443), IPAddress.IPv6None.GetAddressBytes(), ToNetworkPort(5555), 2, 9);

        var entry = PortMapper.FromTcp6Row(row, pid => $"proc-{pid}");

        Assert.Equal("TCP6", entry.Protocol);
        Assert.Equal(IPAddress.IPv6Loopback.ToString(), entry.LocalAddress);
        Assert.Equal(443, entry.LocalPort);
        Assert.Equal(IPAddress.IPv6None.ToString(), entry.RemoteAddress);
        Assert.Equal(5555, entry.RemotePort);
        Assert.Equal("LISTEN", entry.State);
        Assert.Equal("proc-9", entry.ProcessName);
    }

    [Fact]
    public void FromUdp6Row_MapsFields()
    {
        var row = new Udp6RawRow(IPAddress.IPv6Loopback.GetAddressBytes(), ToNetworkPort(161), 12);

        var entry = PortMapper.FromUdp6Row(row, pid => $"proc-{pid}");

        Assert.Equal("UDP6", entry.Protocol);
        Assert.Equal(IPAddress.IPv6Loopback.ToString(), entry.LocalAddress);
        Assert.Equal(161, entry.LocalPort);
        Assert.Equal("*", entry.RemoteAddress);
        Assert.Equal(0, entry.RemotePort);
        Assert.Equal("LISTENING", entry.State);
        Assert.Equal("proc-12", entry.ProcessName);
    }

    [Fact]
    public void FromTcp4Row_NullResolver_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PortMapper.FromTcp4Row(new Tcp4RawRow(0, 0, 0, 0, 1, 0), null!));
    }

    [Fact]
    public void FromTcp6Row_NullResolver_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PortMapper.FromTcp6Row(new Tcp6RawRow([], 0, [], 0, 1, 0), null!));
    }

    private static uint ToNetworkPort(ushort port) => unchecked((uint)(ushort)IPAddress.HostToNetworkOrder((short)port));
}
