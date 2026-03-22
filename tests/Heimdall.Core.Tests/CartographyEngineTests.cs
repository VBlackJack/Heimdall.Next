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

    private const int ExpectedCsvColumns = 20;

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
}
