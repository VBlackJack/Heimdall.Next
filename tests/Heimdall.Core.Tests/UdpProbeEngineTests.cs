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

public class UdpProbeEngineTests
{
    // ── NetBIOS packet tests ─────────────────────────────────────────

    [Fact]
    public void BuildNetBiosNbstatQuery_Returns50Bytes()
    {
        var packet = UdpProbeEngine.BuildNetBiosNbstatQuery();
        Assert.Equal(50, packet.Length);
    }

    [Fact]
    public void BuildNetBiosNbstatQuery_HasCorrectTypeAndClass()
    {
        var packet = UdpProbeEngine.BuildNetBiosNbstatQuery();
        // Type: NBSTAT (0x0021) at offset 46-47
        Assert.Equal(0x00, packet[46]);
        Assert.Equal(0x21, packet[47]);
        // Class: IN (0x0001) at offset 48-49
        Assert.Equal(0x00, packet[48]);
        Assert.Equal(0x01, packet[49]);
    }

    [Fact]
    public void BuildNetBiosNbstatQuery_HasOneQuestion()
    {
        var packet = UdpProbeEngine.BuildNetBiosNbstatQuery();
        // Questions count at offset 4-5
        Assert.Equal(0x00, packet[4]);
        Assert.Equal(0x01, packet[5]);
    }

    [Fact]
    public void ParseNetBiosResponse_TooShort_ReturnsNulls()
    {
        var (name, domain, mac) = UdpProbeEngine.ParseNetBiosResponse(new byte[10]);
        Assert.Null(name);
        Assert.Null(domain);
        Assert.Null(mac);
    }

    [Fact]
    public void ParseNetBiosResponse_RealisticPayload_ExtractsNameAndDomain()
    {
        // Construct a realistic NBSTAT response:
        // Header (12 bytes) + Question (echoed, 38+4=42 bytes) + Answer RR
        var packet = new List<byte>();

        // ── Header (12 bytes) ──
        packet.AddRange([0xAB, 0xCD]); // TxID
        packet.AddRange([0x84, 0x00]); // Flags (response, authoritative)
        packet.AddRange([0x00, 0x01]); // QDCount = 1 (question echoed back)
        packet.AddRange([0x00, 0x01]); // ANCount = 1
        packet.AddRange([0x00, 0x00]); // NSCount = 0
        packet.AddRange([0x00, 0x00]); // ARCount = 0

        // ── Question Section (echoed) ──
        // Encoded NetBIOS wildcard name: length(32) + 32 chars + null
        packet.Add(0x20); // length = 32
        var wildcard = new byte[16];
        wildcard[0] = 0x2A; // '*'
        for (var i = 1; i < 16; i++) wildcard[i] = 0x20; // space
        for (var i = 0; i < 16; i++)
        {
            packet.Add((byte)('A' + ((wildcard[i] >> 4) & 0x0F)));
            packet.Add((byte)('A' + (wildcard[i] & 0x0F)));
        }
        packet.Add(0x00); // null terminator
        packet.AddRange([0x00, 0x21]); // QTYPE: NBSTAT
        packet.AddRange([0x00, 0x01]); // QCLASS: IN

        // ── Answer Section ──
        // Name: compression pointer to offset 12 (question name)
        packet.AddRange([0xC0, 0x0C]);
        packet.AddRange([0x00, 0x21]); // TYPE: NBSTAT
        packet.AddRange([0x00, 0x01]); // CLASS: IN
        packet.AddRange([0x00, 0x00, 0x00, 0x00]); // TTL: 0
        // RDLENGTH: 2 name entries × 18 + 1 (count) + 6 (MAC) = 43
        packet.AddRange([0x00, 43]);

        // RDATA: name count
        packet.Add(0x02); // 2 entries

        // Entry 1: computer name "MYPC" (padded to 15 bytes) + suffix 0x00 + flags 0x0400
        var name1 = System.Text.Encoding.ASCII.GetBytes("MYPC".PadRight(15));
        packet.AddRange(name1);
        packet.Add(0x00); // suffix: workstation
        packet.AddRange([0x04, 0x00]); // flags: unique (no group flag 0x8000)

        // Entry 2: workgroup "CONTOSO" (padded to 15) + suffix 0x00 + flags 0x8400
        var name2 = System.Text.Encoding.ASCII.GetBytes("CONTOSO".PadRight(15));
        packet.AddRange(name2);
        packet.Add(0x00); // suffix: workstation
        packet.AddRange([0x84, 0x00]); // flags: group (0x8000 set)

        // MAC address: AA-BB-CC-DD-EE-FF
        packet.AddRange([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF]);

        var result = UdpProbeEngine.ParseNetBiosResponse([.. packet]);
        Assert.Equal("MYPC", result.Name);
        Assert.Equal("CONTOSO", result.Domain);
        Assert.Equal("AA-BB-CC-DD-EE-FF", result.Mac);
    }

    [Fact]
    public void ParseNetBiosResponse_NoQuestionEchoed_StillWorks()
    {
        // Some implementations don't echo the question (QDCount = 0)
        var packet = new List<byte>();

        // Header: QDCount = 0, ANCount = 1
        packet.AddRange([0x12, 0x34]); // TxID
        packet.AddRange([0x84, 0x00]); // Flags
        packet.AddRange([0x00, 0x00]); // QDCount = 0
        packet.AddRange([0x00, 0x01]); // ANCount = 1
        packet.AddRange([0x00, 0x00, 0x00, 0x00]); // NS + AR = 0

        // Answer: compression pointer + RR header
        packet.AddRange([0xC0, 0x0C]); // name pointer (doesn't matter where it points)
        packet.AddRange([0x00, 0x21]); // TYPE: NBSTAT
        packet.AddRange([0x00, 0x01]); // CLASS: IN
        packet.AddRange([0x00, 0x00, 0x00, 0x00]); // TTL
        packet.AddRange([0x00, 25]); // RDLENGTH: 1×18 + 1 + 6 = 25

        // RDATA
        packet.Add(0x01); // 1 entry
        var name = System.Text.Encoding.ASCII.GetBytes("SERVER01".PadRight(15));
        packet.AddRange(name);
        packet.Add(0x00); // suffix
        packet.AddRange([0x04, 0x00]); // flags: unique
        packet.AddRange([0x11, 0x22, 0x33, 0x44, 0x55, 0x66]); // MAC

        var result = UdpProbeEngine.ParseNetBiosResponse([.. packet]);
        Assert.Equal("SERVER01", result.Name);
        Assert.Null(result.Domain); // only 1 entry, no group
        Assert.Equal("11-22-33-44-55-66", result.Mac);
    }

    // ── SNMP packet tests ────────────────────────────────────────────

    [Fact]
    public void BuildSnmpGetRequest_ProducesValidAsn1()
    {
        var packet = UdpProbeEngine.BuildSnmpGetRequest("public");
        // Must start with SEQUENCE tag (0x30)
        Assert.Equal(0x30, packet[0]);
        // Reasonable size (should be ~80 bytes)
        Assert.True(packet.Length > 50 && packet.Length < 150,
            $"Packet length {packet.Length} is outside expected range 50-150");
    }

    [Fact]
    public void BuildSnmpGetRequest_ContainsCommunityString()
    {
        var packet = UdpProbeEngine.BuildSnmpGetRequest("public");
        // The community string "public" should appear in the packet
        var publicBytes = "public"u8.ToArray();
        var found = false;
        for (var i = 0; i <= packet.Length - publicBytes.Length; i++)
        {
            if (packet.AsSpan(i, publicBytes.Length).SequenceEqual(publicBytes))
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "Community string 'public' not found in SNMP packet");
    }

    [Fact]
    public void BuildSnmpGetRequest_ContainsGetRequestPdu()
    {
        var packet = UdpProbeEngine.BuildSnmpGetRequest("public");
        // GetRequest PDU tag is 0xA0
        Assert.Contains((byte)0xA0, packet);
    }

    [Fact]
    public void ParseSnmpResponse_EmptyData_ReturnsNull()
    {
        Assert.Null(UdpProbeEngine.ParseSnmpResponse([]));
    }

    [Fact]
    public void ParseSnmpResponse_GarbageData_ReturnsNull()
    {
        Assert.Null(UdpProbeEngine.ParseSnmpResponse([0xFF, 0xFF, 0xFF]));
    }

    // ── ASN.1 OID encoding tests ─────────────────────────────────────

    [Fact]
    public void Asn1EncodeOid_SysDescr_CorrectEncoding()
    {
        // sysDescr.0 = 1.3.6.1.2.1.1.1.0
        var oid = new[] { 1, 3, 6, 1, 2, 1, 1, 1, 0 };
        var encoded = UdpProbeEngine.Asn1EncodeOid(oid);
        // OID tag = 0x06
        Assert.Equal(0x06, encoded[0]);
        // First two components: 40*1 + 3 = 43 = 0x2B
        Assert.Equal(0x2B, encoded[2]);
    }

    // ── mDNS packet tests ────────────────────────────────────────────

    [Fact]
    public void BuildMdnsServiceQuery_HasDnsHeader()
    {
        var packet = UdpProbeEngine.BuildMdnsServiceQuery();
        // Question count should be 1
        Assert.Equal(0x00, packet[4]);
        Assert.Equal(0x01, packet[5]);
        // Must end with PTR type (0x000C) and IN class (0x0001)
        Assert.True(packet.Length > 12);
    }

    [Fact]
    public void BuildMdnsServiceQuery_ContainsServiceLabels()
    {
        var packet = UdpProbeEngine.BuildMdnsServiceQuery();
        var packetStr = System.Text.Encoding.ASCII.GetString(packet);
        Assert.Contains("_services", packetStr);
        Assert.Contains("_dns-sd", packetStr);
        Assert.Contains("_udp", packetStr);
        Assert.Contains("local", packetStr);
    }

    [Fact]
    public void ParseMdnsResponse_TooShort_ReturnsEmpty()
    {
        var result = UdpProbeEngine.ParseMdnsResponse(new byte[5]);
        Assert.Empty(result);
    }

    // ── SSDP packet tests ─────────────────────────────────────────

    [Fact]
    public void BuildSsdpMSearchQuery_HasCorrectFormat()
    {
        var packet = UdpProbeEngine.BuildSsdpMSearchQuery();
        var text = System.Text.Encoding.ASCII.GetString(packet);
        Assert.StartsWith("M-SEARCH", text);
        Assert.Contains("ssdp:discover", text);
        Assert.Contains("ssdp:all", text);
        Assert.Contains("239.255.255.250:1900", text);
    }

    [Fact]
    public void ParseSsdpResponse_ValidGateway_ExtractsDeviceType()
    {
        var response = "HTTP/1.1 200 OK\r\n" +
            "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n" +
            "USN: uuid:12345::urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n" +
            "SERVER: Linux/3.4, UPnP/1.0, Portable SDK/1.8.6\r\n" +
            "LOCATION: http://192.168.1.1:49152/rootDesc.xml\r\n\r\n";

        var result = UdpProbeEngine.ParseSsdpResponse(response);

        Assert.NotNull(result);
        Assert.Equal("InternetGatewayDevice", result.DeviceType);
        Assert.Contains("Linux", result.Server!);
    }

    [Fact]
    public void ParseSsdpResponse_EmptyResponse_ReturnsNull()
    {
        Assert.Null(UdpProbeEngine.ParseSsdpResponse(""));
        Assert.Null(UdpProbeEngine.ParseSsdpResponse("   "));
    }

    [Fact]
    public void ParseSsdpResponse_MediaRenderer_ExtractsDeviceType()
    {
        var response = "HTTP/1.1 200 OK\r\n" +
            "ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n" +
            "SERVER: Samsung TV UPnP/1.0\r\n\r\n";

        var result = UdpProbeEngine.ParseSsdpResponse(response);

        Assert.NotNull(result);
        Assert.Equal("MediaRenderer", result.DeviceType);
    }
}
