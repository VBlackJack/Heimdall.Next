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

using System.Xml.Linq;
using Heimdall.Core.Discovery;

namespace Heimdall.Core.Tests;

public class DrawIoExporterTests
{
    // ── XML validity ─────────────────────────────────────────────────

    [Fact]
    public void Generate_ProducesValidXml()
    {
        var snapshot = CreateSnapshot(
            ("192.168.1.1", "gateway", "Router/Gateway", [80, 443]),
            ("192.168.1.10", "web-srv", "Web Server", [80, 443, 8080]));

        var xml = DrawIoExporter.Generate(snapshot);

        // Must parse without exceptions
        var doc = XDocument.Parse(xml);
        Assert.NotNull(doc.Root);
        Assert.Equal("mxfile", doc.Root!.Name.LocalName);
    }

    [Fact]
    public void Generate_ContainsDiagramElement()
    {
        var snapshot = CreateSnapshot(("192.168.1.1", null, null, [22]));

        var xml = DrawIoExporter.Generate(snapshot);
        var doc = XDocument.Parse(xml);

        var diagram = doc.Root!.Element("diagram");
        Assert.NotNull(diagram);
    }

    [Fact]
    public void Generate_ContainsHostIpInNodeLabels()
    {
        var snapshot = CreateSnapshot(("10.0.0.5", "myserver", "SSH Server", [22]));

        var xml = DrawIoExporter.Generate(snapshot);

        Assert.Contains("10.0.0.5", xml);
        Assert.Contains("myserver", xml);
    }

    // ── Role grouping ────────────────────────────────────────────────

    [Fact]
    public void Generate_GroupsHostsByRole()
    {
        var snapshot = CreateSnapshot(
            ("192.168.1.1", null, "SSH Server", [22]),
            ("192.168.1.2", null, "SSH Server", [22]),
            ("192.168.1.3", null, "Web Server", [80]));

        var xml = DrawIoExporter.Generate(snapshot);
        var doc = XDocument.Parse(xml);

        // Should have swimlane cells for each role
        var cells = doc.Descendants("mxCell").ToList();
        Assert.Contains(cells, c => c.Attribute("value")?.Value == "SSH Server");
        Assert.Contains(cells, c => c.Attribute("value")?.Value == "Web Server");
    }

    [Fact]
    public void Generate_PingOnlyHostsGroupedSeparately()
    {
        var snapshot = CreateSnapshot(
            ("192.168.1.1", null, "SSH Server", [22]),
            ("192.168.1.2", null, null, []));

        var xml = DrawIoExporter.Generate(snapshot);

        Assert.Contains("Ping Only (No Open Ports)", xml);
    }

    // ── XML escaping ─────────────────────────────────────────────────

    [Fact]
    public void Generate_EscapesSpecialCharactersInHostname()
    {
        var snapshot = CreateSnapshot(("192.168.1.1", "host<>&\"name", null, [22]));

        var xml = DrawIoExporter.Generate(snapshot);

        // Must be valid XML despite special chars in hostname
        var doc = XDocument.Parse(xml);
        Assert.NotNull(doc.Root);
        Assert.Contains("&lt;", xml);
        Assert.Contains("&amp;", xml);
        Assert.Contains("&gt;", xml);
        Assert.Contains("&quot;", xml);
    }

    // ── Enrichment data in labels ────────────────────────────────────

    [Fact]
    public void Generate_IncludesManufacturerInLabel()
    {
        var hosts = new List<HostScanResult>
        {
            new("192.168.1.1", null, true, 0,
                [new ServiceResult(22, true, "SSH", null, null, 0)],
                new RoleMatch("SSH Server", 50, []),
                [new RoleMatch("SSH Server", 50, [])],
                MacAddress: "AA-BB-CC-DD-EE-FF",
                Manufacturer: "Cisco")
        };

        var snapshot = new NetworkScanSnapshot(
            "test", DateTime.UtcNow,
            new ScanProfile("192.168.1.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null, TimeSpan.Zero, hosts);

        var xml = DrawIoExporter.Generate(snapshot);

        Assert.Contains("Cisco", xml);
    }

    [Fact]
    public void Generate_IncludesOsFingerprintInLabel()
    {
        var hosts = new List<HostScanResult>
        {
            new("192.168.1.1", null, true, 0, [],
                null, [],
                OsFingerprint: new OsFingerprint("Linux/Ubuntu", "banner", 85))
        };

        var snapshot = new NetworkScanSnapshot(
            "test", DateTime.UtcNow,
            new ScanProfile("192.168.1.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null, TimeSpan.Zero, hosts);

        var xml = DrawIoExporter.Generate(snapshot);

        Assert.Contains("Linux/Ubuntu", xml);
    }

    [Fact]
    public void Generate_IncludesExpiredCertWarning()
    {
        var cert = new CertificateInfo(
            "CN=test", "CN=issuer",
            DateTime.UtcNow.AddYears(-2), DateTime.UtcNow.AddDays(-30),
            true, false, "RSA 2048", "SHA256", [], "TLS 1.2", "AABB");

        var hosts = new List<HostScanResult>
        {
            new("192.168.1.1", null, true, 0,
                [new ServiceResult(443, true, "HTTPS", null, null, 0, cert)],
                new RoleMatch("Web Server", 70, []),
                [new RoleMatch("Web Server", 70, [])])
        };

        var snapshot = new NetworkScanSnapshot(
            "test", DateTime.UtcNow,
            new ScanProfile("192.168.1.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null, TimeSpan.Zero, hosts);

        var xml = DrawIoExporter.Generate(snapshot);

        Assert.Contains("EXPIRED", xml);
    }

    // ── Empty snapshot ───────────────────────────────────────────────

    [Fact]
    public void Generate_EmptySnapshot_ProducesValidXml()
    {
        var snapshot = new NetworkScanSnapshot(
            "test", DateTime.UtcNow,
            new ScanProfile("192.168.1.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null, TimeSpan.Zero, []);

        var xml = DrawIoExporter.Generate(snapshot);

        var doc = XDocument.Parse(xml);
        Assert.NotNull(doc.Root);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static NetworkScanSnapshot CreateSnapshot(
        params (string Ip, string? Hostname, string? Role, int[] Ports)[] hosts)
    {
        var hostResults = hosts.Select(h =>
        {
            var services = h.Ports.Select(p =>
                new ServiceResult(p, true, null, null, null, 0)).ToList();
            var role = h.Role is not null
                ? new RoleMatch(h.Role, 80, ["test"])
                : null;
            return new HostScanResult(
                h.Ip, h.Hostname, true, 1,
                services, role, role is not null ? [role] : []);
        }).ToList();

        return new NetworkScanSnapshot(
            "test", DateTime.UtcNow,
            new ScanProfile("192.168.1.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null, TimeSpan.Zero, hostResults);
    }
}
