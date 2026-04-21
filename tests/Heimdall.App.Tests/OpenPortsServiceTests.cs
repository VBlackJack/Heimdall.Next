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
using Heimdall.App.Services;
using Heimdall.Core.Network;

namespace Heimdall.App.Tests;

public sealed class OpenPortsServiceTests
{
    [Fact]
    public void Constructor_NullTcp4Loader_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenPortsService(null!, () => [], () => [], () => [], _ => "proc"));
    }

    [Fact]
    public void Load_AggregatesAllProtocols()
    {
        var service = new OpenPortsService(
            () => [new Tcp4RawRow(0x0100007F, ToNetworkPort(80), 0x0100007F, ToNetworkPort(50000), 5, 10)],
            () => [new Udp4RawRow(0x0100007F, ToNetworkPort(53), 11)],
            () => [new Tcp6RawRow(IPAddress.IPv6Loopback.GetAddressBytes(), ToNetworkPort(443), IPAddress.IPv6None.GetAddressBytes(), ToNetworkPort(12345), 2, 12)],
            () => [new Udp6RawRow(IPAddress.IPv6Loopback.GetAddressBytes(), ToNetworkPort(161), 13)],
            pid => $"proc-{pid}");

        var entries = service.Load();

        Assert.Equal(4, entries.Count);
        Assert.Equal(["TCP", "UDP", "TCP6", "UDP6"], entries.Select(x => x.Protocol).ToArray());
    }

    [Fact]
    public void Load_CachesProcessNameResolutionPerCall()
    {
        var resolverCalls = 0;
        var service = new OpenPortsService(
            () => [new Tcp4RawRow(0x0100007F, ToNetworkPort(80), 0x0100007F, ToNetworkPort(50000), 5, 10)],
            () => [new Udp4RawRow(0x0100007F, ToNetworkPort(53), 10)],
            () => [],
            () => [],
            pid =>
            {
                resolverCalls++;
                return $"proc-{pid}";
            });

        var entries = service.Load();

        Assert.Equal(2, entries.Count);
        Assert.Equal(1, resolverCalls);
    }

    [Fact]
    public void Load_SwallowsSingleSourceFailure()
    {
        var service = new OpenPortsService(
            () => throw new InvalidOperationException("boom"),
            () => [new Udp4RawRow(0x0100007F, ToNetworkPort(53), 11)],
            () => [],
            () => [],
            pid => $"proc-{pid}");

        var entries = service.Load();

        Assert.Single(entries);
        Assert.Equal("UDP", entries[0].Protocol);
    }

    [Fact]
    public void Load_SwallowsResolverFailureForSource()
    {
        var service = new OpenPortsService(
            () => [new Tcp4RawRow(0x0100007F, ToNetworkPort(80), 0x0100007F, ToNetworkPort(50000), 5, 10)],
            () => [new Udp4RawRow(0x0100007F, ToNetworkPort(53), 11)],
            () => [],
            () => [],
            pid => pid == 10 ? throw new InvalidOperationException("nope") : $"proc-{pid}");

        var entries = service.Load();

        Assert.Single(entries);
        Assert.Equal("UDP", entries[0].Protocol);
    }

    [Fact]
    public void Load_CacheResetsPerCall()
    {
        var resolverCalls = 0;
        var service = new OpenPortsService(
            () => [new Tcp4RawRow(0x0100007F, ToNetworkPort(80), 0x0100007F, ToNetworkPort(50000), 5, 10)],
            () => [],
            () => [],
            () => [],
            pid =>
            {
                resolverCalls++;
                return $"proc-{pid}";
            });

        service.Load();
        service.Load();

        Assert.Equal(2, resolverCalls);
    }

    private static uint ToNetworkPort(ushort port) => unchecked((uint)(ushort)IPAddress.HostToNetworkOrder((short)port));
}
