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

using System.Text.Json;
using Heimdall.Core.Discovery;

namespace Heimdall.Core.Tests;

public class CartographyEngineTests
{
    // ── Probe strategy (tests the actual decision path in ProbePortAsync) ──

    [Theory]
    [InlineData(443)]
    [InlineData(8443)]
    [InlineData(9443)]
    public void GetProbeStrategy_HttpsTlsPorts_TlsInspectionAndHttpOverTls(int port)
    {
        var s = CartographyEngine.GetProbeStrategy(port);
        Assert.False(s.PlaintextHttp, $"Port {port} should NOT get plaintext HTTP");
        Assert.True(s.TlsInspection, $"Port {port} should get TLS inspection");
        Assert.True(s.HttpOverTls, $"Port {port} should get HTTP-over-TLS");
    }

    [Theory]
    [InlineData(636)]   // LDAPS
    [InlineData(993)]   // IMAPS
    [InlineData(995)]   // POP3S
    [InlineData(465)]   // SMTPS
    [InlineData(990)]   // FTPS implicit
    [InlineData(3269)]  // GC-SSL
    public void GetProbeStrategy_NonHttpTlsPorts_TlsInspectionOnly(int port)
    {
        var s = CartographyEngine.GetProbeStrategy(port);
        Assert.False(s.PlaintextHttp, $"Port {port} should NOT get plaintext HTTP");
        Assert.True(s.TlsInspection, $"Port {port} should get TLS inspection");
        Assert.False(s.HttpOverTls, $"Port {port} should NOT get HTTP-over-TLS");
    }

    [Theory]
    [InlineData(80)]
    [InlineData(8080)]
    [InlineData(3000)]
    [InlineData(8123)]
    public void GetProbeStrategy_PlaintextHttpPorts_PlaintextOnly(int port)
    {
        var s = CartographyEngine.GetProbeStrategy(port);
        Assert.True(s.PlaintextHttp, $"Port {port} should get plaintext HTTP");
        Assert.False(s.TlsInspection, $"Port {port} should NOT get TLS inspection");
        Assert.False(s.HttpOverTls, $"Port {port} should NOT get HTTP-over-TLS");
    }

    [Theory]
    [InlineData(22)]    // SSH
    [InlineData(3389)]  // RDP
    [InlineData(3306)]  // MySQL
    [InlineData(6379)]  // Redis
    public void GetProbeStrategy_NonHttpNonTlsPorts_NoProbes(int port)
    {
        var s = CartographyEngine.GetProbeStrategy(port);
        Assert.False(s.PlaintextHttp);
        Assert.False(s.TlsInspection);
        Assert.False(s.HttpOverTls);
    }

    [Fact]
    public void HttpsTlsPorts_IsSubsetOfTlsPorts()
    {
        Assert.True(CartographyEngine.HttpsTlsPorts.IsSubsetOf(CartographyEngine.TlsPorts));
    }

    // ── CIDR parsing ─────────────────────────────────────────────────

    [Fact]
    public void ParseCidr_Slash24_Returns254Hosts()
    {
        var hosts = CartographyEngine.ParseCidr("192.168.1.0/24");
        Assert.Equal(254, hosts.Count);
        Assert.Equal("192.168.1.1", hosts[0]);
        Assert.Equal("192.168.1.254", hosts[^1]);
    }

    [Fact]
    public void ParseCidr_InvalidCidr_ReturnsEmpty()
    {
        Assert.Empty(CartographyEngine.ParseCidr("not-valid"));
    }

    [Fact]
    public void ParseCidr_TooLarge_ReturnsEmpty()
    {
        // /15 is larger than /16 limit
        Assert.Empty(CartographyEngine.ParseCidr("10.0.0.0/15"));
    }

    // ── Scan history diff with typed HostChange ──────────────────────

    [Fact]
    public void ComputeDiff_NewHost_Detected()
    {
        var older = CreateSnapshot(["192.168.1.1"]);
        var newer = CreateSnapshot(["192.168.1.1", "192.168.1.2"]);

        var diff = ScanHistoryManager.ComputeDiff(older, newer);

        Assert.Single(diff.NewHosts);
        Assert.Equal("192.168.1.2", diff.NewHosts[0].IpAddress);
        Assert.Empty(diff.RemovedHosts);
    }

    [Fact]
    public void ComputeDiff_RemovedHost_Detected()
    {
        var older = CreateSnapshot(["192.168.1.1", "192.168.1.2"]);
        var newer = CreateSnapshot(["192.168.1.1"]);

        var diff = ScanHistoryManager.ComputeDiff(older, newer);

        Assert.Empty(diff.NewHosts);
        Assert.Single(diff.RemovedHosts);
        Assert.Equal("192.168.1.2", diff.RemovedHosts[0].IpAddress);
    }

    [Fact]
    public void ComputeDiff_OsChanged_ReturnsTypedHostChange()
    {
        var older = CreateSnapshot(["192.168.1.1"],
            os: new OsFingerprint("Windows", "TTL", 70));
        var newer = CreateSnapshot(["192.168.1.1"],
            os: new OsFingerprint("Linux/macOS", "TTL", 60));

        var diff = ScanHistoryManager.ComputeDiff(older, newer);

        Assert.Single(diff.ModifiedHosts);
        var (_, _, changes) = diff.ModifiedHosts[0];
        Assert.Contains(changes, c => c.Type == HostChangeType.OsChanged);
    }

    [Fact]
    public void ComputeDiff_PortAdded_ReturnsTypedHostChange()
    {
        var older = CreateSnapshot(["192.168.1.1"], ports: [22]);
        var newer = CreateSnapshot(["192.168.1.1"], ports: [22, 80]);

        var diff = ScanHistoryManager.ComputeDiff(older, newer);

        Assert.Single(diff.ModifiedHosts);
        var (_, _, changes) = diff.ModifiedHosts[0];
        Assert.Contains(changes, c => c.Type == HostChangeType.PortAdded && c.Port == 80);
    }

    [Fact]
    public void ComputeDiff_NoChanges_ModifiedEmpty()
    {
        var older = CreateSnapshot(["192.168.1.1"], ports: [22, 80]);
        var newer = CreateSnapshot(["192.168.1.1"], ports: [22, 80]);

        var diff = ScanHistoryManager.ComputeDiff(older, newer);

        Assert.Empty(diff.NewHosts);
        Assert.Empty(diff.RemovedHosts);
        Assert.Empty(diff.ModifiedHosts);
    }

    // ── CSV export header localization ─────────────────────────────

    private const int ExpectedCsvColumns = 27;

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void CsvExportHeader_ExistsInLocale_WithCorrectColumnCount(string locale)
    {
        var localeFile = FindLocaleFile(locale);
        Assert.True(File.Exists(localeFile), $"Locale file not found: {localeFile}");

        var json = File.ReadAllText(localeFile);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        Assert.NotNull(dict);
        Assert.True(dict!.ContainsKey("ToolNetMapExportHeader"),
            $"Key 'ToolNetMapExportHeader' missing from {locale}.json");

        var header = dict["ToolNetMapExportHeader"];
        var columns = header.Split(',');
        Assert.Equal(ExpectedCsvColumns, columns.Length);
    }

    [Fact]
    public void CsvExportHeader_EnAndFr_HaveSameColumnCount()
    {
        var enFile = FindLocaleFile("en");
        var frFile = FindLocaleFile("fr");

        var enDict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(enFile))!;
        var frDict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(frFile))!;

        var enCols = enDict["ToolNetMapExportHeader"].Split(',').Length;
        var frCols = frDict["ToolNetMapExportHeader"].Split(',').Length;

        Assert.Equal(enCols, frCols);
    }

    [Fact]
    public void BuildCsvExport_EmptySnapshot_ReturnsHeaderOnly()
    {
        var snapshot = new NetworkScanSnapshot(
            "empty",
            DateTime.UtcNow,
            new ScanProfile("10.0.0.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null,
            TimeSpan.Zero,
            []);

        var csv = CartographyEngine.BuildCsvExport(snapshot, key => key);
        var lines = csv.TrimEnd().Split(Environment.NewLine);

        Assert.Single(lines);
        Assert.Equal("ToolNetMapExportHeader", lines[0]);
    }

    [Fact]
    public void BuildCsvExport_SingleHost_FormatsCsvCorrectly()
    {
        var snapshot = new NetworkScanSnapshot(
            "single",
            DateTime.UtcNow,
            new ScanProfile("10.0.0.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null,
            TimeSpan.Zero,
            [CreateDetailedHost("10.0.0.10")]);

        var csv = CartographyEngine.BuildCsvExport(snapshot, key => key);

        Assert.Contains("\"10.0.0.10\"", csv);
        Assert.Contains("\"web.local\"", csv);
        Assert.Contains("\"22, 443\"", csv);
        Assert.Contains("\"HTTPS, SSH\"", csv);
        Assert.Contains("\"TLS 1.3 (ToolNetMapCertValid 2030-01-01)\"", csv);
    }

    [Fact]
    public void BuildCsvExport_SanitizesCsvInjection()
    {
        var host = CreateDetailedHost("10.0.0.20") with { Hostname = "=cmd()" };
        var snapshot = new NetworkScanSnapshot(
            "inject",
            DateTime.UtcNow,
            new ScanProfile("10.0.0.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null,
            TimeSpan.Zero,
            [host]);

        var csv = CartographyEngine.BuildCsvExport(snapshot, key => key);

        Assert.DoesNotContain("\"=cmd()\"", csv);
        Assert.Contains("\"'=cmd()\"", csv);
    }

    [Fact]
    public void BuildCsvExport_WithLocalizedHeader_UsesLocalizedHeader()
    {
        var snapshot = new NetworkScanSnapshot(
            "localized",
            DateTime.UtcNow,
            new ScanProfile("10.0.0.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null,
            TimeSpan.Zero,
            []);

        var csv = CartographyEngine.BuildCsvExport(snapshot, key => key == "ToolNetMapExportHeader" ? "HEADER" : key);

        Assert.StartsWith("HEADER", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCsvExport_WithVlans_IncludesVlanColumn()
    {
        var snapshot = new NetworkScanSnapshot(
            "vlans",
            DateTime.UtcNow,
            new ScanProfile("10.0.0.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null,
            TimeSpan.Zero,
            [CreateDetailedHost("10.0.0.30")],
            [new VlanInfo(12, "Servers", "10.0.0.0/24", "10.0.0.1", ["10.0.0.30"])]);

        var csv = CartographyEngine.BuildCsvExport(snapshot, key => key);

        Assert.Contains("\"VLAN 12 (10.0.0.0/24)\"", csv);
    }

    [Fact]
    public void FormatSsdpSummary_WithAllFields_FormatsCorrectly()
    {
        var ssdp = new SsdpInfo(
            DeviceType: "urn:schemas-upnp-org:device:MediaServer:1",
            FriendlyName: "Living Room TV",
            Manufacturer: "Contoso",
            ModelName: "Screen9000",
            Server: "UPnP/1.0");

        var summary = CartographyEngine.FormatSsdpSummary(ssdp);

        Assert.Equal("Living Room TV | Contoso | UPnP/1.0", summary);
    }

    private static string FindLocaleFile(string locale)
    {
        // Walk up from test binary directory to find locales/
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "locales", $"{locale}.json");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        // Fallback: relative to solution root
        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "locales", $"{locale}.json"));
    }

    private static NetworkScanSnapshot CreateSnapshot(
        string[] ips,
        int[]? ports = null,
        OsFingerprint? os = null)
    {
        var openPorts = ports ?? [];
        var hosts = ips.Select(ip => new HostScanResult(
            ip, null, true, 0,
            openPorts.Select(p => new ServiceResult(p, true, null, null, null, 0)).ToList(),
            null, [],
            OsFingerprint: os
        )).ToList();

        return new NetworkScanSnapshot(
            "test", DateTime.UtcNow,
            new ScanProfile("192.168.1.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null, TimeSpan.Zero, hosts);
    }

    private static HostScanResult CreateDetailedHost(string ipAddress)
    {
        return new HostScanResult(
            ipAddress,
            "web.local",
            true,
            15,
            [
                new ServiceResult(22, true, "SSH", null, null, 5),
                new ServiceResult(443, true, "HTTPS", null, null, 10, new CertificateInfo(
                    "CN=web.local",
                    "CN=Root",
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    false,
                    false,
                    "RSA 2048",
                    "sha256RSA",
                    ["web.local"],
                    "TLS 1.3",
                    "ABCDEF"))
            ],
            new RoleMatch("Web Server", 88, ["HTTPS"]),
            [new RoleMatch("Web Server", 88, ["HTTPS"])],
            MacAddress: "AA-BB-CC-DD-EE-FF",
            Manufacturer: "Contoso",
            OsFingerprint: new OsFingerprint("Linux", "TTL", 70),
            NetBiosName: "WEB01",
            NetBiosDomain: "LAB",
            SnmpInfo: new SnmpInfo("Linux Server", "web.local", "DC1", "1.3.6.1.4.1"),
            MdnsServices: ["_http._tcp.local"],
            SsdpInfo: new SsdpInfo("device", "Living Room TV", "Contoso", "Screen9000", "UPnP/1.0"),
            NtlmInfo: new NtlmInfo(null, null, "web.local", "lab.local", null, "20348"),
            SshHashFingerprint: "deadbeefcafebabe",
            FaviconHash: 12345);
    }
}
