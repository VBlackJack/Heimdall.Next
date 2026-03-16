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

namespace Heimdall.Ssh.Tests;

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
    public void Verify_DifferentPorts_AreSeparateEntries()
    {
        var fp22 = HostKeyStore.ComputeFingerprint(_sampleKey);
        _store.Trust("server.example.com", 22, fp22);

        byte[] otherKey = [0xAA, 0xBB, 0xCC];
        var result = _store.Verify("server.example.com", 2222, otherKey);

        Assert.True(result.Trusted);
        Assert.True(result.FirstUse);
    }

    // ── HostKeyEvent ───────────────────────────────────────────────────

    [Fact]
    public void Verify_RaisesHostKeyEvent()
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

        _store.Verify("test.host", 22, _sampleKey);

        Assert.Equal("test.host:22", eventHost);
        Assert.NotNull(eventFingerprint);
        Assert.True(eventTrusted);
    }

    [Fact]
    public void Verify_Mismatch_RaisesEventWithFalse()
    {
        _store.Trust("test.host", 22, "SHA256:old");
        bool? eventTrusted = null;

        _store.HostKeyEvent += (_, _, trusted) => eventTrusted = trusted;

        _store.Verify("test.host", 22, _sampleKey);

        Assert.False(eventTrusted);
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

    // ── Trust / Remove / GetFingerprint ────────────────────────────────

    [Fact]
    public void Trust_StoresFingerprint()
    {
        _store.Trust("myhost", 22, "SHA256:test");

        Assert.Equal("SHA256:test", _store.GetFingerprint("myhost", 22));
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
}
