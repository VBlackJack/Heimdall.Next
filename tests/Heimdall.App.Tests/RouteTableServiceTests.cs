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

public sealed class RouteTableServiceTests
{
    private const string Fixture = """
IPv4 Route Table
Active Routes:
Network Destination        Netmask          Gateway       Interface  Metric
0.0.0.0 0.0.0.0 192.0.2.1 192.0.2.10 25
192.0.2.0 255.255.255.0 On-link 192.0.2.10 281
Persistent Routes:
""";

    [Fact]
    public void Constructor_NullLoader_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RouteTableService(null!));
    }

    [Fact]
    public void Load_ParsesOutput()
    {
        var service = new RouteTableService(() => Fixture);

        var routes = service.Load();

        Assert.Equal(2, routes.Count);
        Assert.Equal("0.0.0.0", routes[0].Destination);
    }

    [Fact]
    public void Load_EmptyOutput_ReturnsEmpty()
    {
        var service = new RouteTableService(() => string.Empty);

        Assert.Empty(service.Load());
    }

    [Fact]
    public void Load_ProcessException_ReturnsEmpty()
    {
        var service = new RouteTableService(() => throw new InvalidOperationException("boom"));

        Assert.Empty(service.Load());
    }

    [Fact]
    public void Load_PreservesOrder()
    {
        var service = new RouteTableService(() => Fixture);

        var routes = service.Load();

        Assert.Equal("0.0.0.0", routes[0].Destination);
        Assert.Equal("192.0.2.0", routes[1].Destination);
    }
}
