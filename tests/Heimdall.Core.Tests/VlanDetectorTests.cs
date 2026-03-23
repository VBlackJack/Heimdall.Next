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

using Heimdall.Core.Discovery;

namespace Heimdall.Core.Tests;

public class VlanDetectorTests
{
    // ── InferFromHosts ───────────────────────────────────────────────

    [Fact]
    public void InferFromHosts_SingleSubnet_ReturnsOneVlan()
    {
        var hosts = CreateHosts("192.168.1.1", "192.168.1.10", "192.168.1.100");

        var vlans = VlanDetector.InferFromHosts(hosts);

        Assert.Single(vlans);
        Assert.Equal("192.168.1.0/24", vlans[0].Subnet);
        Assert.Equal(3, vlans[0].MemberIps.Count);
    }

    [Fact]
    public void InferFromHosts_TwoSubnets_ReturnsTwoVlans()
    {
        var hosts = CreateHosts("192.168.1.1", "192.168.1.50", "10.0.0.5", "10.0.0.10");

        var vlans = VlanDetector.InferFromHosts(hosts);

        Assert.Equal(2, vlans.Count);
        Assert.Contains(vlans, v => v.Subnet == "10.0.0.0/24");
        Assert.Contains(vlans, v => v.Subnet == "192.168.1.0/24");
    }

    [Fact]
    public void InferFromHosts_EmptyList_ReturnsEmpty()
    {
        var vlans = VlanDetector.InferFromHosts([]);
        Assert.Empty(vlans);
    }

    [Fact]
    public void InferFromHosts_DetectsGatewayDot1()
    {
        var hosts = CreateHosts("192.168.1.1", "192.168.1.10");

        var vlans = VlanDetector.InferFromHosts(hosts);

        Assert.Single(vlans);
        Assert.Equal("192.168.1.1", vlans[0].Gateway);
    }

    [Fact]
    public void InferFromHosts_DetectsGatewayDot254()
    {
        var hosts = CreateHosts("192.168.1.10", "192.168.1.254");

        var vlans = VlanDetector.InferFromHosts(hosts);

        Assert.Single(vlans);
        Assert.Equal("192.168.1.254", vlans[0].Gateway);
    }

    [Fact]
    public void InferFromHosts_NoGateway_ReturnsNull()
    {
        var hosts = CreateHosts("192.168.1.10", "192.168.1.20");

        var vlans = VlanDetector.InferFromHosts(hosts);

        Assert.Single(vlans);
        Assert.Null(vlans[0].Gateway);
    }

    [Fact]
    public void InferFromHosts_MembersAreSortedByIp()
    {
        var hosts = CreateHosts("192.168.1.100", "192.168.1.1", "192.168.1.50");

        var vlans = VlanDetector.InferFromHosts(hosts);

        Assert.Equal("192.168.1.1", vlans[0].MemberIps[0]);
        Assert.Equal("192.168.1.50", vlans[0].MemberIps[1]);
        Assert.Equal("192.168.1.100", vlans[0].MemberIps[2]);
    }

    [Fact]
    public void InferFromHosts_SkipsDeadHosts()
    {
        var hosts = new List<HostScanResult>
        {
            new("192.168.1.1", null, true, 0, [], null, []),
            new("192.168.1.2", null, false, 0, [], null, []),
        };

        var vlans = VlanDetector.InferFromHosts(hosts);

        Assert.Single(vlans);
        Assert.Single(vlans[0].MemberIps);
        Assert.Equal("192.168.1.1", vlans[0].MemberIps[0]);
    }

    [Fact]
    public void InferFromHosts_VlanIdsIncrement()
    {
        var hosts = CreateHosts("192.168.1.1", "10.0.0.1");

        var vlans = VlanDetector.InferFromHosts(hosts);

        Assert.Equal(2, vlans.Count);
        Assert.Equal(1, vlans[0].VlanId);
        Assert.Equal(2, vlans[1].VlanId);
    }

    // ── ParseShowVlanBrief ───────────────────────────────────────────

    [Fact]
    public void ParseShowVlanBrief_ValidOutput_ReturnsActiveVlans()
    {
        var output = """
            VLAN Name                             Status    Ports
            ---- -------------------------------- --------- ---------------------------
            1    default                          active    Gi0/1, Gi0/2
            10   SERVERS                          active    Gi0/3, Gi0/4
            20   MANAGEMENT                       active    Gi0/5
            999  UNUSED                           act/unsup
            """;

        var vlans = VlanDetector.ParseShowVlanBrief(output);

        Assert.Equal(4, vlans.Count);
        Assert.Equal(1, vlans[0].VlanId);
        Assert.Equal("default", vlans[0].Name);
        Assert.Equal(10, vlans[1].VlanId);
        Assert.Equal("SERVERS", vlans[1].Name);
        Assert.Equal(20, vlans[2].VlanId);
    }

    [Fact]
    public void ParseShowVlanBrief_SkipsNonActiveVlans()
    {
        var output = """
            VLAN Name                             Status    Ports
            ---- -------------------------------- --------- ---------------------------
            1    default                          active    Gi0/1
            50   SUSPENDED                        suspended
            """;

        var vlans = VlanDetector.ParseShowVlanBrief(output);

        Assert.Single(vlans);
        Assert.Equal(1, vlans[0].VlanId);
    }

    [Fact]
    public void ParseShowVlanBrief_EmptyOutput_ReturnsEmpty()
    {
        Assert.Empty(VlanDetector.ParseShowVlanBrief(""));
    }

    [Fact]
    public void ParseShowVlanBrief_SkipsHeaderAndSeparatorLines()
    {
        var output = """
            VLAN Name                             Status    Ports
            ---- -------------------------------- --------- ---------------------------
            """;

        Assert.Empty(VlanDetector.ParseShowVlanBrief(output));
    }

    // ── EnrichVlansWithSubnets ───────────────────────────────────────

    [Fact]
    public void EnrichVlansWithSubnets_MatchesVlanId()
    {
        var vlans = new List<VlanInfo>
        {
            new(10, "SERVERS", "", null, []),
            new(20, "MGMT", "", null, []),
        };

        var output = """
            Vlan10  10.0.10.1  YES manual up  up
            Vlan20  10.0.20.1  YES manual up  up
            """;

        VlanDetector.EnrichVlansWithSubnets(vlans, output);

        Assert.Equal("10.0.10.0/24", vlans[0].Subnet);
        Assert.Equal("10.0.10.1", vlans[0].Gateway);
        Assert.Equal("10.0.20.0/24", vlans[1].Subnet);
        Assert.Equal("10.0.20.1", vlans[1].Gateway);
    }

    [Fact]
    public void EnrichVlansWithSubnets_SkipsNonVlanLines()
    {
        var vlans = new List<VlanInfo>
        {
            new(10, "TEST", "", null, []),
        };

        var output = """
            GigabitEthernet0/1  192.168.1.1  YES manual up  up
            Vlan10  10.0.10.1  YES manual up  up
            """;

        VlanDetector.EnrichVlansWithSubnets(vlans, output);

        Assert.Equal("10.0.10.0/24", vlans[0].Subnet);
    }

    [Fact]
    public void EnrichVlansWithSubnets_IgnoresUnmatchedVlanIds()
    {
        var vlans = new List<VlanInfo>
        {
            new(10, "TEST", "", null, []),
        };

        var output = "Vlan99  10.0.99.1  YES manual up  up";

        VlanDetector.EnrichVlansWithSubnets(vlans, output);

        Assert.Equal("", vlans[0].Subnet);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static List<HostScanResult> CreateHosts(params string[] ips) =>
        ips.Select(ip => new HostScanResult(ip, null, true, 0, [], null, [])).ToList();
}
