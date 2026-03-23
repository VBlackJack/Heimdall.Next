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

public class KnowledgeBaseManagerTests
{
    private static readonly DateTime T0 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = T0.AddHours(6);
    private static readonly DateTime T2 = T0.AddHours(12);

    // ── CreateEmpty ─────────────────────────────────────────────────

    [Fact]
    public void CreateEmpty_ReturnsValidKb()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();

        Assert.Equal(1, kb.Version);
        Assert.Empty(kb.Hosts);
        Assert.NotNull(kb.TtlConfig);
    }

    // ── MergeHost (new host) ────────────────────────────────────────

    [Fact]
    public void MergeHost_NewHost_CreatesKnownHostFromScanResult()
    {
        var scanned = CreateHostResult("192.168.1.1", hostname: "web-srv",
            ports: [22, 80], mac: "AA-BB-CC-DD-EE-FF");

        var known = KnowledgeBaseManager.MergeHost(null, scanned, T0, "local");

        Assert.Equal("192.168.1.1", known.IpAddress);
        Assert.Equal(T0, known.FirstSeen);
        Assert.Equal(T0, known.LastSeen);
        Assert.Equal(1, known.ScanCount);
        Assert.Equal("web-srv", known.Hostname?.Value);
        Assert.True(known.IsAlive?.Value);
        Assert.Equal(2, known.Services.Count);
        Assert.Equal("AA-BB-CC-DD-EE-FF", known.MacAddress?.Value);
    }

    // ── MergeHost (existing host, newer wins) ───────────────────────

    [Fact]
    public void MergeHost_ExistingHost_UpdatesTimestampsAndCount()
    {
        var first = CreateHostResult("192.168.1.1");
        var existing = KnowledgeBaseManager.MergeHost(null, first, T0, "local");

        var second = CreateHostResult("192.168.1.1");
        var merged = KnowledgeBaseManager.MergeHost(existing, second, T1, "local");

        Assert.Equal(T0, merged.FirstSeen);
        Assert.Equal(T1, merged.LastSeen);
        Assert.Equal(2, merged.ScanCount);
    }

    [Fact]
    public void MergeHost_NewerHostname_Wins()
    {
        var first = CreateHostResult("192.168.1.1", hostname: "old-name");
        var existing = KnowledgeBaseManager.MergeHost(null, first, T0, "local");

        var second = CreateHostResult("192.168.1.1", hostname: "new-name");
        var merged = KnowledgeBaseManager.MergeHost(existing, second, T1, "local");

        Assert.Equal("new-name", merged.Hostname?.Value);
        Assert.Equal(T1, merged.Hostname?.ObservedAt);
    }

    [Fact]
    public void MergeHost_NullHostname_PreservesExisting()
    {
        var first = CreateHostResult("192.168.1.1", hostname: "keep-me");
        var existing = KnowledgeBaseManager.MergeHost(null, first, T0, "local");

        var second = CreateHostResult("192.168.1.1", hostname: null);
        var merged = KnowledgeBaseManager.MergeHost(existing, second, T1, "local");

        Assert.Equal("keep-me", merged.Hostname?.Value);
        Assert.Equal(T0, merged.Hostname?.ObservedAt);
    }

    // ── OS fingerprint merge (confidence-based) ─────────────────────

    [Fact]
    public void MergeHost_HigherConfidenceOs_Wins()
    {
        var first = CreateHostResult("192.168.1.1",
            os: new OsFingerprint("Windows", "TTL", 60));
        var existing = KnowledgeBaseManager.MergeHost(null, first, T0, "local");

        var second = CreateHostResult("192.168.1.1",
            os: new OsFingerprint("Linux/Ubuntu", "banner", 85));
        var merged = KnowledgeBaseManager.MergeHost(existing, second, T1, "local");

        Assert.Equal("Linux/Ubuntu", merged.OsFingerprint?.Value.OsGuess);
    }

    [Fact]
    public void MergeHost_LowerConfidenceOs_Loses()
    {
        var first = CreateHostResult("192.168.1.1",
            os: new OsFingerprint("Linux/Ubuntu", "banner", 85));
        var existing = KnowledgeBaseManager.MergeHost(null, first, T0, "local");

        var second = CreateHostResult("192.168.1.1",
            os: new OsFingerprint("Windows", "TTL", 60));
        var merged = KnowledgeBaseManager.MergeHost(existing, second, T1, "local");

        Assert.Equal("Linux/Ubuntu", merged.OsFingerprint?.Value.OsGuess);
    }

    [Fact]
    public void MergeHost_SameConfidenceOs_NewestWins()
    {
        var first = CreateHostResult("192.168.1.1",
            os: new OsFingerprint("Windows", "TTL", 70));
        var existing = KnowledgeBaseManager.MergeHost(null, first, T0, "local");

        var second = CreateHostResult("192.168.1.1",
            os: new OsFingerprint("Linux", "TTL", 70));
        var merged = KnowledgeBaseManager.MergeHost(existing, second, T1, "local");

        Assert.Equal("Linux", merged.OsFingerprint?.Value.OsGuess);
    }

    // ── Service port merge ──────────────────────────────────────────

    [Fact]
    public void MergeHost_NewPortAppears_AddedToServices()
    {
        var first = CreateHostResult("192.168.1.1", ports: [22]);
        var existing = KnowledgeBaseManager.MergeHost(null, first, T0, "local");

        var second = CreateHostResult("192.168.1.1", ports: [22, 80]);
        var merged = KnowledgeBaseManager.MergeHost(existing, second, T1, "local");

        Assert.Equal(2, merged.Services.Count);
        Assert.Contains(merged.Services, s => s.Port == 80);
    }

    [Fact]
    public void MergeHost_OldPortNotInNewScan_PreservedWithOldTimestamp()
    {
        var first = CreateHostResult("192.168.1.1", ports: [22, 443]);
        var existing = KnowledgeBaseManager.MergeHost(null, first, T0, "local");

        var second = CreateHostResult("192.168.1.1", ports: [22]);
        var merged = KnowledgeBaseManager.MergeHost(existing, second, T1, "local");

        Assert.Equal(2, merged.Services.Count);
        var port443 = merged.Services.First(s => s.Port == 443);
        Assert.Equal(T0, port443.ObservedAt);
    }

    [Fact]
    public void MergeHost_SamePortRescanned_UpdatesTimestamp()
    {
        var first = CreateHostResult("192.168.1.1", ports: [22]);
        var existing = KnowledgeBaseManager.MergeHost(null, first, T0, "local");

        var second = CreateHostResult("192.168.1.1", ports: [22]);
        var merged = KnowledgeBaseManager.MergeHost(existing, second, T1, "local");

        var port22 = merged.Services.First(s => s.Port == 22);
        Assert.Equal(T1, port22.ObservedAt);
    }

    // ── MergeSnapshot ───────────────────────────────────────────────

    [Fact]
    public void MergeSnapshot_AddsNewHostsToKb()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();
        var snapshot = CreateSnapshot(T0, "local",
            CreateHostResult("192.168.1.1"),
            CreateHostResult("192.168.1.2"));

        var merged = KnowledgeBaseManager.MergeSnapshot(kb, snapshot);

        Assert.Equal(2, KnowledgeBaseManager.HostCount(merged));
    }

    [Fact]
    public void MergeSnapshot_MergesExistingHosts()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();
        var snap1 = CreateSnapshot(T0, "local",
            CreateHostResult("192.168.1.1", hostname: "first"));
        kb = KnowledgeBaseManager.MergeSnapshot(kb, snap1);

        var snap2 = CreateSnapshot(T1, "local",
            CreateHostResult("192.168.1.1", hostname: "second"),
            CreateHostResult("192.168.1.5"));
        kb = KnowledgeBaseManager.MergeSnapshot(kb, snap2);

        Assert.Equal(2, KnowledgeBaseManager.HostCount(kb));
        var host = KnowledgeBaseManager.Lookup(kb, "192.168.1.1")!;
        Assert.Equal("second", host.Hostname?.Value);
        Assert.Equal(2, host.ScanCount);
    }

    [Fact]
    public void MergeSnapshot_PreservesGatewaySource()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();
        var snapshot = CreateSnapshot(T0, "gw-paris",
            CreateHostResult("10.0.0.1"));

        kb = KnowledgeBaseManager.MergeSnapshot(kb, snapshot);

        var host = KnowledgeBaseManager.Lookup(kb, "10.0.0.1")!;
        Assert.Equal("gw-paris", host.IsAlive?.Source);
    }

    // ── PurgeStaleHosts ─────────────────────────────────────────────

    [Fact]
    public void PurgeStaleHosts_RemovesOldEntries()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();
        var oldTime = DateTime.UtcNow.AddHours(-100);
        var recentTime = DateTime.UtcNow.AddHours(-1);

        var snap1 = CreateSnapshot(oldTime, "local",
            CreateHostResult("192.168.1.1"));
        kb = KnowledgeBaseManager.MergeSnapshot(kb, snap1);

        var snap2 = CreateSnapshot(recentTime, "local",
            CreateHostResult("192.168.1.2"));
        kb = KnowledgeBaseManager.MergeSnapshot(kb, snap2);

        var purged = KnowledgeBaseManager.PurgeStaleHosts(kb, 48);

        Assert.Equal(1, KnowledgeBaseManager.HostCount(purged));
        Assert.NotNull(KnowledgeBaseManager.Lookup(purged, "192.168.1.2"));
        Assert.Null(KnowledgeBaseManager.Lookup(purged, "192.168.1.1"));
    }

    [Fact]
    public void PurgeStaleHosts_KeepsRecentEntries()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();
        var recentTime = DateTime.UtcNow.AddMinutes(-30);
        var snap = CreateSnapshot(recentTime, "local",
            CreateHostResult("192.168.1.1"));
        kb = KnowledgeBaseManager.MergeSnapshot(kb, snap);

        var purged = KnowledgeBaseManager.PurgeStaleHosts(kb, 48);

        Assert.Equal(1, KnowledgeBaseManager.HostCount(purged));
    }

    // ── Clear ───────────────────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllHosts()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();
        var snap = CreateSnapshot(T0, "local",
            CreateHostResult("192.168.1.1"),
            CreateHostResult("192.168.1.2"));
        kb = KnowledgeBaseManager.MergeSnapshot(kb, snap);

        var cleared = KnowledgeBaseManager.Clear(kb);

        Assert.Equal(0, KnowledgeBaseManager.HostCount(cleared));
    }

    // ── Lookup ──────────────────────────────────────────────────────

    [Fact]
    public void Lookup_ExistingHost_ReturnsHost()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();
        var snap = CreateSnapshot(T0, "local",
            CreateHostResult("192.168.1.1"));
        kb = KnowledgeBaseManager.MergeSnapshot(kb, snap);

        Assert.NotNull(KnowledgeBaseManager.Lookup(kb, "192.168.1.1"));
    }

    [Fact]
    public void Lookup_MissingHost_ReturnsNull()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();
        Assert.Null(KnowledgeBaseManager.Lookup(kb, "192.168.1.99"));
    }

    // ── IsFresh ─────────────────────────────────────────────────────

    [Fact]
    public void IsFresh_RecentObservation_ReturnsTrue()
    {
        Assert.True(KnowledgeBaseManager.IsFresh(
            DateTime.UtcNow.AddHours(-1), 4));
    }

    [Fact]
    public void IsFresh_StaleObservation_ReturnsFalse()
    {
        Assert.False(KnowledgeBaseManager.IsFresh(
            DateTime.UtcNow.AddHours(-10), 4));
    }

    // ── ToScanResult round-trip ─────────────────────────────────────

    [Fact]
    public void ToScanResult_PreservesAllFields()
    {
        var original = CreateHostResult("192.168.1.1",
            hostname: "test-host", ports: [22, 80],
            mac: "AA-BB-CC-DD-EE-FF",
            os: new OsFingerprint("Linux", "banner", 85));

        var known = KnowledgeBaseManager.MergeHost(null, original, T0, "local");
        var result = KnowledgeBaseManager.ToScanResult(known);

        Assert.Equal("192.168.1.1", result.IpAddress);
        Assert.Equal("test-host", result.Hostname);
        Assert.True(result.IsAlive);
        Assert.Equal(2, result.Services.Count);
        Assert.Equal("AA-BB-CC-DD-EE-FF", result.MacAddress);
        Assert.Equal("Linux", result.OsFingerprint?.OsGuess);
    }

    // ── ArePortsFresh / IsAliveFresh / AreUdpProbesFresh ────────────

    [Fact]
    public void ArePortsFresh_AllServicesRecent_ReturnsTrue()
    {
        var host = KnowledgeBaseManager.MergeHost(null,
            CreateHostResult("192.168.1.1", ports: [22, 80]),
            DateTime.UtcNow, "local");

        Assert.True(KnowledgeBaseManager.ArePortsFresh(
            host, new KnowledgeBaseTtlConfig()));
    }

    [Fact]
    public void ArePortsFresh_StaleService_ReturnsFalse()
    {
        var host = KnowledgeBaseManager.MergeHost(null,
            CreateHostResult("192.168.1.1", ports: [22]),
            DateTime.UtcNow.AddHours(-48), "local");

        Assert.False(KnowledgeBaseManager.ArePortsFresh(
            host, new KnowledgeBaseTtlConfig(PortScanHours: 24)));
    }

    [Fact]
    public void IsAliveFresh_RecentPing_ReturnsTrue()
    {
        var host = KnowledgeBaseManager.MergeHost(null,
            CreateHostResult("192.168.1.1"),
            DateTime.UtcNow, "local");

        Assert.True(KnowledgeBaseManager.IsAliveFresh(
            host, new KnowledgeBaseTtlConfig()));
    }

    // ── Serialization round-trip ───────────────────────────────────

    [Fact]
    public async Task RoundTrip_SerializationPreservesAllFields()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();
        var host = new HostScanResult(
            "192.168.1.1", "web-srv", true, 12,
            [new ServiceResult(443, true, "HTTPS", "HTTP/1.1 200 OK", "nginx/1.25", 42,
                new CertificateInfo("CN=web-srv", "CN=CA", T0.AddYears(-1), T0.AddYears(1),
                    false, false, "RSA 2048", "SHA256", ["web-srv.local"], "TLS 1.3", "AABB"),
                new Dictionary<string, string> { ["Server"] = "nginx/1.25" })],
            new RoleMatch("Web Server", 80, ["port:443"]),
            [new RoleMatch("Web Server", 80, ["port:443"])],
            MacAddress: "AA-BB-CC-DD-EE-FF",
            Manufacturer: "Dell",
            OsFingerprint: new OsFingerprint("Linux/Ubuntu", "banner", 85),
            NetBiosName: "WEBSRV",
            NetBiosDomain: "CORP",
            SnmpInfo: new SnmpInfo("Linux web01", "web01", "DC1-Rack3"),
            MdnsServices: ["SSH", "HTTP"],
            HttpHeaders: new Dictionary<string, string> { ["Server"] = "nginx" },
            SsdpInfo: new SsdpInfo("MediaServer", "WebMedia", "Dell", "PowerEdge", "Linux/UPnP"));

        var snapshot = new NetworkScanSnapshot(
            "roundtrip-test", T0,
            new ScanProfile("192.168.1.0/24", ScanDepth.Standard, null, 50, 2000, true, true),
            "gw-paris", TimeSpan.FromSeconds(42), [host]);

        kb = KnowledgeBaseManager.MergeSnapshot(kb, snapshot);

        // Serialize then deserialize via JSON
        var json = System.Text.Json.JsonSerializer.Serialize(kb, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        var deserialized = System.Text.Json.JsonSerializer.Deserialize<NetworkKnowledgeBase>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(deserialized);
        Assert.Single(deserialized!.Hosts);

        var h = deserialized.Hosts.Values.First();
        Assert.Equal("192.168.1.1", h.IpAddress);
        Assert.Equal("web-srv", h.Hostname?.Value);
        Assert.Equal("gw-paris", h.Hostname?.Source);
        Assert.Equal(T0, h.Hostname?.ObservedAt);
        Assert.True(h.IsAlive?.Value);
        Assert.Equal(12L, h.PingLatencyMs?.Value);
        Assert.Equal("AA-BB-CC-DD-EE-FF", h.MacAddress?.Value);
        Assert.Equal("Dell", h.Manufacturer?.Value);
        Assert.Equal("Linux/Ubuntu", h.OsFingerprint?.Value.OsGuess);
        Assert.Equal(85, h.OsFingerprint?.Value.Confidence);
        Assert.Equal("WEBSRV", h.NetBiosName?.Value);
        Assert.Equal("CORP", h.NetBiosDomain?.Value);
        Assert.Equal("web01", h.SnmpInfo?.Value.SysName);
        Assert.Equal("DC1-Rack3", h.SnmpInfo?.Value.SysLocation);
        Assert.Contains("SSH", h.MdnsServices!.Value);
        Assert.Equal("nginx", h.HttpHeaders?.Value["Server"]);
        Assert.Equal("MediaServer", h.SsdpInfo?.Value.DeviceType);

        // Service round-trip
        Assert.Single(h.Services);
        var svc = h.Services[0];
        Assert.Equal(443, svc.Port);
        Assert.True(svc.IsOpen);
        Assert.Equal("HTTPS", svc.ServiceName);
        Assert.Equal("HTTP/1.1 200 OK", svc.Banner);
        Assert.NotNull(svc.Certificate);
        Assert.Equal("CN=web-srv", svc.Certificate!.Subject);
        Assert.Equal("TLS 1.3", svc.Certificate.TlsVersion);
        Assert.Contains("web-srv.local", svc.Certificate.SubjectAltNames);

        // Role round-trip
        Assert.NotNull(h.PrimaryRole);
        Assert.Equal("Web Server", h.PrimaryRole!.Value.Role);
        Assert.Equal(80, h.PrimaryRole.Value.Confidence);

        // TTL config preserved
        Assert.Equal(4, deserialized.TtlConfig.HostAliveHours);
        Assert.Equal(24, deserialized.TtlConfig.PortScanHours);
        Assert.Equal(720, deserialized.TtlConfig.CertificateHours);
    }

    [Fact]
    public void MergeSnapshot_CalledTwice_IsIdempotentOnScanCount()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();
        var snapshot = CreateSnapshot(T0, "local",
            CreateHostResult("192.168.1.1", hostname: "srv"));

        kb = KnowledgeBaseManager.MergeSnapshot(kb, snapshot);
        var countAfterFirst = KnowledgeBaseManager.Lookup(kb, "192.168.1.1")!.ScanCount;

        kb = KnowledgeBaseManager.MergeSnapshot(kb, snapshot);
        var countAfterSecond = KnowledgeBaseManager.Lookup(kb, "192.168.1.1")!.ScanCount;

        // Each merge increments — expected behavior (not idempotent by design,
        // because the same snapshot could be re-merged if save failed mid-way)
        Assert.Equal(1, countAfterFirst);
        Assert.Equal(2, countAfterSecond);
    }

    // ── Multi-gateway merge ─────────────────────────────────────────

    [Fact]
    public void MergeSnapshot_DifferentGateways_MergesCorrectly()
    {
        var kb = KnowledgeBaseManager.CreateEmpty();

        var snap1 = CreateSnapshot(T0, "gw-paris",
            CreateHostResult("10.0.0.1", hostname: "paris-view"));
        kb = KnowledgeBaseManager.MergeSnapshot(kb, snap1);

        var snap2 = CreateSnapshot(T1, "gw-london",
            CreateHostResult("10.0.0.1", hostname: "london-view"));
        kb = KnowledgeBaseManager.MergeSnapshot(kb, snap2);

        var host = KnowledgeBaseManager.Lookup(kb, "10.0.0.1")!;
        Assert.Equal("london-view", host.Hostname?.Value);
        Assert.Equal("gw-london", host.Hostname?.Source);
        Assert.Equal(2, host.ScanCount);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static HostScanResult CreateHostResult(
        string ip,
        string? hostname = null,
        int[]? ports = null,
        string? mac = null,
        OsFingerprint? os = null)
    {
        var services = (ports ?? [])
            .Select(p => new ServiceResult(p, true, null, null, null, 0))
            .ToList();

        return new HostScanResult(
            ip, hostname, true, 5, services, null, [],
            MacAddress: mac,
            OsFingerprint: os);
    }

    private static NetworkScanSnapshot CreateSnapshot(
        DateTime timestamp, string? gateway, params HostScanResult[] hosts) => new(
        "test", timestamp,
        new ScanProfile("192.168.1.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
        gateway, TimeSpan.FromSeconds(10), [.. hosts]);
}
