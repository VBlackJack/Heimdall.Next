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

using Heimdall.Core.Ssh;

#pragma warning disable CS0618 // Type or member is obsolete — this test exercises the legacy byte[] contract intentionally.

namespace Heimdall.Ssh.Tests;

// The plink TOFU caller shells out to plink.exe and is not currently isolated
// behind a unit-test seam. These tests cover the HostKeyStore contract used by
// both the SSH.NET path and the plink fallback, including first-trust
// persistence signaling via HostKeyEvent.
public class HostKeyStoreTests
{
    private readonly HostKeyStore _store = new();
    private readonly byte[] _sampleKey = [0x01, 0x02, 0x03, 0x04, 0x05];

    // ── Verify (TOFU) ──────────────────────────────────────────────────

    [Fact]
    public void Verify_FirstUse_ReturnsTrustedAndFirstUse()
    {
        var result = _store.Verify("server.example.com", 22, _sampleKey);

        Assert.True(result.Trusted);
        Assert.True(result.FirstUse);
        Assert.Null(result.StoredFingerprint);
        Assert.StartsWith("SHA256:", result.Fingerprint);
    }

    [Fact]
    public void LegacyByteOverload_FirstUse_ReturnsTrustedTrueByDesign()
    {
        // This test documents the obsolete legacy contract: the byte[] overload
        // returns Trusted=true on first use, which is why production code must
        // route through HostKeyTrustService instead. See the [Obsolete] message
        // on HostKeyStore.Verify(string, int, byte[]) for the migration path.
        var result = _store.Verify("first.example.com", 22, _sampleKey);

        Assert.True(result.FirstUse);
        Assert.True(result.Trusted);
    }

    [Fact]
    public void Verify_KnownHost_MatchingKey_ReturnsTrusted()
    {
        var fingerprint = HostKeyStore.ComputeFingerprint(_sampleKey);
        _store.Trust("server.example.com", 22, fingerprint);

        var result = _store.Verify("server.example.com", 22, _sampleKey);

        Assert.True(result.Trusted);
        Assert.False(result.FirstUse);
        Assert.Equal(fingerprint, result.StoredFingerprint);
        Assert.Equal(fingerprint, result.Fingerprint);
    }

    [Fact]
    public void Verify_KnownHost_MismatchedKey_ReturnsUntrusted()
    {
        _store.Trust("server.example.com", 22, "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        byte[] differentKey = [0xFF, 0xFE, 0xFD, 0xFC];
        var result = _store.Verify("server.example.com", 22, differentKey);

        Assert.False(result.Trusted);
        Assert.False(result.FirstUse);
        Assert.Equal("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", result.StoredFingerprint);
    }

    [Fact]
    public void Verify_SessionTrustedHost_MatchingKey_ReturnsTrustedWithoutPersisting()
    {
        var fingerprint = HostKeyStore.ComputeFingerprint(_sampleKey);
        var eventRaised = false;
        _store.HostKeyEvent += (_, _, _) => eventRaised = true;

        _store.TrustForSession("server.example.com", 22, fingerprint, "ssh-ed25519");

        var result = _store.Verify("server.example.com", 22, _sampleKey);

        Assert.True(result.Trusted);
        Assert.False(result.FirstUse);
        Assert.Equal(fingerprint, result.StoredFingerprint);
        Assert.Null(_store.GetFingerprint("server.example.com", 22));
        Assert.Empty(_store.GetAllTrusted());
        Assert.False(eventRaised);
    }

    [Fact]
    public void Verify_SessionTrustedHost_DoesNotSurviveFreshStore()
    {
        var fingerprint = HostKeyStore.ComputeFingerprint(_sampleKey);
        _store.TrustForSession("server.example.com", 22, fingerprint, "ssh-ed25519");

        var freshStore = new HostKeyStore();
        var result = freshStore.Verify("server.example.com", 22, _sampleKey);

        Assert.True(result.Trusted);
        Assert.True(result.FirstUse);
        Assert.Null(result.StoredFingerprint);
    }

    [Fact]
    public void Verify_DifferentPorts_AreSeparateEntries()
    {
        var fp22 = HostKeyStore.ComputeFingerprint(_sampleKey);
        _store.Trust("server.example.com", 22, fp22);

        byte[] otherKey = [0xAA, 0xBB, 0xCC];
        var result = _store.Verify("server.example.com", 2222, otherKey);

        Assert.True(result.Trusted);
        Assert.True(result.FirstUse);
    }

    [Fact]
    public void Verify_CaseInsensitiveHost_TreatedAsSeparateEntries()
    {
        // MakeKey is case-sensitive: "Server.Example.com:22" != "server.example.com:22"
        var fingerprint = HostKeyStore.ComputeFingerprint(_sampleKey);
        _store.Trust("Server.Example.COM", 22, fingerprint);

        // Lowercase lookup should not find the uppercase entry (TOFU = first use)
        var result = _store.Verify("server.example.com", 22, _sampleKey);

        Assert.True(result.Trusted);
        Assert.True(result.FirstUse);
    }

    [Fact]
    public void LoadFromConfig_EmptyCollection_DoesNotThrow()
    {
        var entries = new List<(string host, int port, string? fingerprint)>();

        _store.LoadFromConfig(entries);

        Assert.Empty(_store.GetAllTrusted());
    }

    [Fact]
    public void Trust_SameHostDifferentPort_StoresBoth()
    {
        _store.Trust("myhost", 22, "SHA256:first");
        _store.Trust("myhost", 2222, "SHA256:second");

        Assert.Equal("SHA256:first", _store.GetFingerprint("myhost", 22));
        Assert.Equal("SHA256:second", _store.GetFingerprint("myhost", 2222));
    }

    // ── HostKeyEvent ───────────────────────────────────────────────────

    [Fact]
    public void Verify_DoesNotRaiseHostKeyEvent()
    {
        var eventRaised = false;

        _store.HostKeyEvent += (_, _, _) => eventRaised = true;

        _store.Verify("test.host", 22, _sampleKey);

        Assert.False(eventRaised);
    }

    [Fact]
    public void Verify_Mismatch_DoesNotRaiseHostKeyEvent()
    {
        _store.Trust("test.host", 22, "SHA256:old");
        var eventRaised = false;

        _store.HostKeyEvent += (_, _, _) => eventRaised = true;

        _store.Verify("test.host", 22, _sampleKey);

        Assert.False(eventRaised);
    }

    // ── LoadFromConfig ─────────────────────────────────────────────────

    [Fact]
    public void LoadFromConfig_PopulatesStore()
    {
        var entries = new List<(string host, int port, string? fingerprint)>
        {
            ("host1.example.com", 22, "SHA256:abc123"),
            ("host2.example.com", 2222, "SHA256:def456"),
            ("host3.example.com", 22, null) // Null fingerprint should be skipped
        };

        _store.LoadFromConfig(entries);

        Assert.Equal("SHA256:abc123", _store.GetFingerprint("host1.example.com", 22));
        Assert.Equal("SHA256:def456", _store.GetFingerprint("host2.example.com", 2222));
        Assert.Null(_store.GetFingerprint("host3.example.com", 22));
    }

    [Fact]
    public void LoadFromConfig_SkipsWhitespaceFingerprints()
    {
        var entries = new List<(string host, int port, string? fingerprint)>
        {
            ("host.example.com", 22, "   ")
        };

        _store.LoadFromConfig(entries);

        Assert.Null(_store.GetFingerprint("host.example.com", 22));
    }

    [Fact]
    public void LoadEntriesFromConfig_PopulatesMetadataStore()
    {
        var entry = new HostKeyEntry(
            "SHA256:abc123",
            DateTimeOffset.Parse("2026-04-24T10:15:00Z"),
            DateTimeOffset.Parse("2026-04-24T10:16:00Z"),
            "ssh-ed25519",
            HostKeySource.ImportedKnownHosts);

        _store.LoadEntriesFromConfig([("host.example.com", 2222, entry)]);

        var stored = _store.GetEntry("host.example.com", 2222);
        Assert.NotNull(stored);
        Assert.Equal(entry, stored);
        Assert.Equal("SHA256:abc123", _store.GetFingerprint("host.example.com", 2222));
    }

    // ── Trust / Remove / GetFingerprint ────────────────────────────────

    [Fact]
    public void Trust_StoresFingerprint()
    {
        _store.Trust("myhost", 22, "SHA256:test");

        Assert.Equal("SHA256:test", _store.GetFingerprint("myhost", 22));
    }

    [Fact]
    public void Trust_WithMetadata_StoresEntry()
    {
        _store.Trust("myhost", 22, "SHA256:test", "ssh-ed25519", HostKeySource.UserConfirmed);

        var entry = _store.GetEntry("myhost", 22);
        Assert.NotNull(entry);
        Assert.Equal("SHA256:test", entry.Fingerprint);
        Assert.Equal("ssh-ed25519", entry.Algorithm);
        Assert.Equal(HostKeySource.UserConfirmed, entry.Source);
        Assert.True(entry.FirstSeen > DateTimeOffset.MinValue);
        Assert.True(entry.LastSeen > DateTimeOffset.MinValue);
    }

    [Fact]
    public void Trust_RaisesHostKeyEventWithTrustedTrue()
    {
        string? eventHost = null;
        string? eventFingerprint = null;
        bool? eventTrusted = null;

        _store.HostKeyEvent += (host, fp, trusted) =>
        {
            eventHost = host;
            eventFingerprint = fp;
            eventTrusted = trusted;
        };

        _store.Trust("myhost", 22, "SHA256:test");

        Assert.Equal("myhost:22", eventHost);
        Assert.Equal("SHA256:test", eventFingerprint);
        Assert.True(eventTrusted);
    }

    [Fact]
    public void Verify_FirstUseThenTrust_RaisesTrustedEventOnlyOnce()
    {
        var trustedEvents = new List<(string Host, string Fingerprint, bool Trusted)>();

        _store.HostKeyEvent += (host, fingerprint, trusted) =>
            trustedEvents.Add((host, fingerprint, trusted));

        var result = _store.Verify("test.host", 22, _sampleKey);
        _store.Trust("test.host", 22, result.Fingerprint);

        var trustedEvent = Assert.Single(trustedEvents);
        Assert.Equal("test.host:22", trustedEvent.Host);
        Assert.Equal(result.Fingerprint, trustedEvent.Fingerprint);
        Assert.True(trustedEvent.Trusted);
    }

    [Fact]
    public void Trust_OverwritesPreviousFingerprint()
    {
        _store.Trust("myhost", 22, "SHA256:old");
        _store.Trust("myhost", 22, "SHA256:new");

        Assert.Equal("SHA256:new", _store.GetFingerprint("myhost", 22));
    }

    [Fact]
    public void Remove_RemovesExistingEntry()
    {
        _store.Trust("myhost", 22, "SHA256:test");

        var removed = _store.Remove("myhost", 22);

        Assert.True(removed);
        Assert.Null(_store.GetFingerprint("myhost", 22));
    }

    [Fact]
    public void Remove_NonExistentEntry_ReturnsFalse()
    {
        var removed = _store.Remove("nonexistent", 22);

        Assert.False(removed);
    }

    [Fact]
    public void GetFingerprint_UnknownHost_ReturnsNull()
    {
        Assert.Null(_store.GetFingerprint("unknown.host", 22));
    }

    // ── GetAllTrusted ──────────────────────────────────────────────────

    [Fact]
    public void GetAllTrusted_ReturnsAllEntries()
    {
        _store.Trust("host1", 22, "SHA256:aaa");
        _store.Trust("host2", 2222, "SHA256:bbb");

        var all = _store.GetAllTrusted();

        Assert.Equal(2, all.Count);
        Assert.Equal("SHA256:aaa", all["host1:22"]);
        Assert.Equal("SHA256:bbb", all["host2:2222"]);
    }

    [Fact]
    public void GetAllEntries_ReturnsMetadataEntries()
    {
        _store.Trust("host1", 22, "SHA256:aaa", "ssh-ed25519", HostKeySource.UserConfirmed);

        var all = _store.GetAllEntries();

        var entry = Assert.Single(all);
        Assert.Equal("host1:22", entry.Key);
        Assert.Equal("SHA256:aaa", entry.Value.Fingerprint);
        Assert.Equal("ssh-ed25519", entry.Value.Algorithm);
    }

    // ── ComputeFingerprint ─────────────────────────────────────────────

    [Fact]
    public void ComputeFingerprint_ReturnsSha256Prefix()
    {
        var fp = HostKeyStore.ComputeFingerprint(_sampleKey);

        Assert.StartsWith("SHA256:", fp);
    }

    [Fact]
    public void ComputeFingerprint_NoPaddingInBase64()
    {
        var fp = HostKeyStore.ComputeFingerprint(_sampleKey);

        Assert.DoesNotContain("=", fp);
    }

    [Fact]
    public void ComputeFingerprint_DeterministicForSameInput()
    {
        var fp1 = HostKeyStore.ComputeFingerprint(_sampleKey);
        var fp2 = HostKeyStore.ComputeFingerprint(_sampleKey);

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void ComputeFingerprint_DifferentInputsProduceDifferentResults()
    {
        byte[] otherKey = [0xAA, 0xBB, 0xCC];

        var fp1 = HostKeyStore.ComputeFingerprint(_sampleKey);
        var fp2 = HostKeyStore.ComputeFingerprint(otherKey);

        Assert.NotEqual(fp1, fp2);
    }

    // ── Null argument validation ───────────────────────────────────────

    [Fact]
    public void Verify_NullHost_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => _store.Verify(null!, 22, _sampleKey));
    }

    [Fact]
    public void Verify_NullHostKey_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => _store.Verify("host", 22, null!));
    }

    [Fact]
    public void Trust_NullHost_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => _store.Trust(null!, 22, "fp"));
    }

    [Fact]
    public void Trust_NullFingerprint_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => _store.Trust("host", 22, null!));
    }

    [Fact]
    public void LoadFromConfig_NullEntries_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => _store.LoadFromConfig(null!));
    }

    [Fact]
    public void GetFingerprint_NullHost_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => _store.GetFingerprint(null!, 22));
    }

    [Fact]
    public void Remove_NullHost_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => _store.Remove(null!, 22));
    }

    // ── IPv6 address support ────────────────────────────────────────────

    [Fact]
    public void Trust_IPv6Address_StoresFingerprint()
    {
        _store.Trust("[::1]", 22, "SHA256:ipv6test");

        Assert.Equal("SHA256:ipv6test", _store.GetFingerprint("[::1]", 22));
    }

    [Fact]
    public void Verify_IPv6Address_FirstUse_ReturnsTrustedAndFirstUse()
    {
        var result = _store.Verify("[::1]", 22, _sampleKey);

        Assert.True(result.Trusted);
        Assert.True(result.FirstUse);
    }

    [Fact]
    public void Trust_Verify_IPv6_Roundtrip()
    {
        var fingerprint = HostKeyStore.ComputeFingerprint(_sampleKey);
        _store.Trust("[::1]", 22, fingerprint);

        var result = _store.Verify("[::1]", 22, _sampleKey);

        Assert.True(result.Trusted);
        Assert.False(result.FirstUse);
        Assert.Equal(fingerprint, result.StoredFingerprint);
    }

    [Fact]
    public void Trust_IPv6FullAddress_Roundtrip()
    {
        var fingerprint = HostKeyStore.ComputeFingerprint(_sampleKey);
        _store.Trust("[2001:db8::1]", 22, fingerprint);

        var result = _store.Verify("[2001:db8::1]", 22, _sampleKey);

        Assert.True(result.Trusted);
        Assert.False(result.FirstUse);
    }

    [Fact]
    public void MixedIPv4AndIPv6_AreSeparateEntries()
    {
        _store.Trust("192.168.1.1", 22, "SHA256:ipv4key");
        _store.Trust("[::1]", 22, "SHA256:ipv6key");

        Assert.Equal("SHA256:ipv4key", _store.GetFingerprint("192.168.1.1", 22));
        Assert.Equal("SHA256:ipv6key", _store.GetFingerprint("[::1]", 22));

        var all = _store.GetAllTrusted();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void IPv6_DifferentPorts_AreSeparateEntries()
    {
        _store.Trust("[::1]", 22, "SHA256:port22");
        _store.Trust("[::1]", 2222, "SHA256:port2222");

        Assert.Equal("SHA256:port22", _store.GetFingerprint("[::1]", 22));
        Assert.Equal("SHA256:port2222", _store.GetFingerprint("[::1]", 2222));
    }

    [Fact]
    public void LoadFromConfig_WithIPv6Entries()
    {
        var entries = new List<(string host, int port, string? fingerprint)>
        {
            ("[::1]", 22, "SHA256:loopback6"),
            ("[2001:db8::1]", 2222, "SHA256:remote6"),
            ("192.168.1.1", 22, "SHA256:ipv4host"),
        };

        _store.LoadFromConfig(entries);

        Assert.Equal("SHA256:loopback6", _store.GetFingerprint("[::1]", 22));
        Assert.Equal("SHA256:remote6", _store.GetFingerprint("[2001:db8::1]", 2222));
        Assert.Equal("SHA256:ipv4host", _store.GetFingerprint("192.168.1.1", 22));
    }

    [Fact]
    public void Remove_IPv6Entry()
    {
        _store.Trust("[::1]", 22, "SHA256:ipv6test");

        Assert.True(_store.Remove("[::1]", 22));
        Assert.Null(_store.GetFingerprint("[::1]", 22));
    }

    [Fact]
    public void Verify_IPv6_Mismatch_ReturnsUntrusted()
    {
        _store.Trust("[::1]", 22, "SHA256:originalkey");

        byte[] differentKey = [0xAA, 0xBB, 0xCC];
        var result = _store.Verify("[::1]", 22, differentKey);

        Assert.False(result.Trusted);
        Assert.False(result.FirstUse);
        Assert.Equal("SHA256:originalkey", result.StoredFingerprint);
    }

    // ── ConstantTimeEquals ─────────────────────────────────────────────

    [Theory]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "SHA256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", false)]
    [InlineData("SHA256:short", "SHA256:longer-fingerprint-string", false)]
    [InlineData("", "", true)]
    public void ConstantTimeEquals_MatchesByValueRegardlessOfLength(string a, string b, bool expected)
    {
        Assert.Equal(expected, HostKeyStore.ConstantTimeEquals(a, b));
    }

    [Fact]
    public void ConstantTimeEquals_NullOperands_ReturnsFalse()
    {
        Assert.False(HostKeyStore.ConstantTimeEquals(null!, "x"));
        Assert.False(HostKeyStore.ConstantTimeEquals("x", null!));
        Assert.False(HostKeyStore.ConstantTimeEquals(null!, null!));
    }
}

#pragma warning restore CS0618
