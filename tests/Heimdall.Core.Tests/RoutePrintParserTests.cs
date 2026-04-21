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

public sealed class RoutePrintParserTests
{
    private const string EnglishFixture = """
===========================================================================
Interface List
 13...aa bb cc dd ee ff ......Ethernet
===========================================================================

IPv4 Route Table
===========================================================================
Active Routes:
Network Destination        Netmask          Gateway       Interface  Metric
          0.0.0.0          0.0.0.0      192.0.2.1    192.0.2.10     25
        127.0.0.0        255.0.0.0         On-link      127.0.0.1    331
      192.0.2.0    255.255.255.0         On-link    192.0.2.10    281
Persistent Routes:
  None
""";

    private const string FrenchFixture = """
===========================================================================
Liste d'interfaces
 13...aa bb cc dd ee ff ......Ethernet
===========================================================================

IPv4 Table de routage
===========================================================================
Itinéraires actifs :
Destination réseau    Masque réseau   Adr. passerelle  Adr. interface Métrique
          0.0.0.0          0.0.0.0      192.0.2.1    192.0.2.10     25
        127.0.0.0        255.0.0.0         On-link      127.0.0.1    331
      192.0.2.0    255.255.255.0         On-link    192.0.2.10    281
Itinéraires persistants :
  Aucun
""";

    [Theory]
    [InlineData("IPv4 Route Table", true)]
    [InlineData("IPv4 Table de routage", true)]
    [InlineData("IPv6 Route Table", false)]
    [InlineData("", false)]
    public void IsIpv4SectionHeader_Works(string line, bool expected)
    {
        Assert.Equal(expected, RoutePrintParser.IsIpv4SectionHeader(line));
    }

    [Theory]
    [InlineData("Active Routes:", true)]
    [InlineData("Itinéraires actifs :", true)]
    [InlineData("actif", true)]
    [InlineData("Persistent Routes:", false)]
    public void IsActiveRoutesHeader_Works(string line, bool expected)
    {
        Assert.Equal(expected, RoutePrintParser.IsActiveRoutesHeader(line));
    }

    [Theory]
    [InlineData("===", true)]
    [InlineData("Persistent Routes:", true)]
    [InlineData("Itinéraires persistants :", true)]
    [InlineData("IPv6 Route Table", true)]
    [InlineData("Active Routes:", false)]
    public void IsEndOfSection_Works(string line, bool expected)
    {
        Assert.Equal(expected, RoutePrintParser.IsEndOfSection(line));
    }

    [Theory]
    [InlineData("Network Destination        Netmask", true)]
    [InlineData("Destination réseau    Masque réseau", true)]
    [InlineData("Netmask", true)]
    [InlineData("Gateway", false)]
    public void IsColumnHeader_Works(string line, bool expected)
    {
        Assert.Equal(expected, RoutePrintParser.IsColumnHeader(line));
    }

    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        Assert.Empty(RoutePrintParser.Parse(null));
    }

    [Fact]
    public void Parse_Whitespace_ReturnsEmpty()
    {
        Assert.Empty(RoutePrintParser.Parse(" \r\n\t "));
    }

    [Fact]
    public void Parse_EnglishFixture_ReturnsRoutes()
    {
        var routes = RoutePrintParser.Parse(EnglishFixture);

        Assert.Equal(3, routes.Count);
        Assert.Equal("0.0.0.0", routes[0].Destination);
        Assert.Equal("192.0.2.10", routes[0].Interface);
        Assert.Equal("281", routes[2].Metric);
    }

    [Fact]
    public void Parse_FrenchFixture_ReturnsRoutes()
    {
        var routes = RoutePrintParser.Parse(FrenchFixture);

        Assert.Equal(3, routes.Count);
        Assert.Equal("127.0.0.0", routes[1].Destination);
        Assert.Equal("255.0.0.0", routes[1].Mask);
    }

    [Fact]
    public void Parse_StopsAtIpv6Section()
    {
        var input = """
IPv4 Route Table
Active Routes:
Network Destination        Netmask          Gateway       Interface  Metric
0.0.0.0 0.0.0.0 192.0.2.1 192.0.2.10 25
IPv6 Route Table
Active Routes:
::/0 :: 2001:db8::1 2001:db8::10 25
""";

        var routes = RoutePrintParser.Parse(input);

        Assert.Single(routes);
        Assert.Equal("0.0.0.0", routes[0].Destination);
    }

    [Fact]
    public void Parse_SkipsShortLines()
    {
        var input = """
IPv4 Route Table
Active Routes:
Network Destination        Netmask          Gateway       Interface  Metric
0.0.0.0 0.0.0.0 192.0.2.1
192.0.2.0 255.255.255.0 On-link 192.0.2.10 281
""";

        var routes = RoutePrintParser.Parse(input);

        Assert.Single(routes);
        Assert.Equal("192.0.2.0", routes[0].Destination);
    }

    [Fact]
    public void Parse_SkipsRowsWithoutDotInFirstField()
    {
        var input = """
IPv4 Route Table
Active Routes:
Network Destination        Netmask          Gateway       Interface  Metric
Default 0.0.0.0 192.0.2.1 192.0.2.10 25
192.0.2.0 255.255.255.0 On-link 192.0.2.10 281
""";

        var routes = RoutePrintParser.Parse(input);

        Assert.Single(routes);
        Assert.Equal("192.0.2.0", routes[0].Destination);
    }

    [Fact]
    public void Parse_WithoutActiveRoutesSection_ReturnsEmpty()
    {
        var input = """
IPv4 Route Table
Network Destination        Netmask          Gateway       Interface  Metric
0.0.0.0 0.0.0.0 192.0.2.1 192.0.2.10 25
""";

        Assert.Empty(RoutePrintParser.Parse(input));
    }

    [Fact]
    public void Parse_ToleratesCrLfAndTabs()
    {
        var input = "IPv4 Route Table\r\nActive Routes:\r\nNetwork Destination\tNetmask\tGateway\tInterface\tMetric\r\n0.0.0.0\t0.0.0.0\t192.0.2.1\t192.0.2.10\t25\r\n";

        var routes = RoutePrintParser.Parse(input);

        Assert.Single(routes);
        Assert.Equal("25", routes[0].Metric);
    }

    [Fact]
    public void Parse_IgnoresColumnHeaderRepeatedInsideSection()
    {
        var input = """
IPv4 Route Table
Active Routes:
Network Destination        Netmask          Gateway       Interface  Metric
0.0.0.0 0.0.0.0 192.0.2.1 192.0.2.10 25
Destination réseau    Masque réseau   Adr. passerelle  Adr. interface Métrique
192.0.2.0 255.255.255.0 On-link 192.0.2.10 281
""";

        var routes = RoutePrintParser.Parse(input);

        Assert.Equal(2, routes.Count);
    }
}
