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

using Heimdall.App.Services;
using Heimdall.Core.Network;

namespace Heimdall.App.Tests;

public sealed class NetworkInterfacesServiceTests
{
    [Fact]
    public void Constructor_NullLoader_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NetworkInterfacesService(null!));
    }

    [Fact]
    public void Load_ReturnsSnapshotsFromLoader()
    {
        var expected = new[]
        {
            new NicSnapshot("eth0", "Ethernet", "Up", "1 Gbps", "AA:BB", "192.0.2.10", "255.255.255.0", "192.0.2.1", "DHCP"),
            new NicSnapshot("wlan0", "Wireless80211", "Down", "-", "", "", "", "", "Static"),
        };
        var service = new NetworkInterfacesService(() => expected);

        var result = service.Load();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Load_NullLoaderResult_NormalizesToEmpty()
    {
        var service = new NetworkInterfacesService(() => null!);

        var result = service.Load();

        Assert.Empty(result);
    }

    [Fact]
    public void Load_PreservesOrder()
    {
        var service = new NetworkInterfacesService(() =>
        [
            new NicSnapshot("b", "Ethernet", "Up", "1 Gbps", "", "", "", "", "DHCP"),
            new NicSnapshot("a", "Ethernet", "Up", "1 Gbps", "", "", "", "", "DHCP"),
        ]);

        var result = service.Load();

        Assert.Equal("b", result[0].Name);
        Assert.Equal("a", result[1].Name);
    }

    [Fact]
    public void Load_LoaderException_Bubbles()
    {
        var service = new NetworkInterfacesService(() => throw new InvalidOperationException("boom"));

        var ex = Assert.Throws<InvalidOperationException>(() => service.Load());

        Assert.Equal("boom", ex.Message);
    }
}
