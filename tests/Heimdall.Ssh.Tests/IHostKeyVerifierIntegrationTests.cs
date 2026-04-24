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

namespace Heimdall.Ssh.Tests;

/// <summary>
/// Mirrors the host-key decision flow used by SshConnectionFactory and the plink fallback:
/// known+matching hosts proceed silently, otherwise the verifier decides whether the
/// presented fingerprint is persisted through HostKeyStore.Trust.
/// </summary>
public sealed class IHostKeyVerifierIntegrationTests
{
    private const string Host = "host.example.com";
    private const int Port = 22;
    private const string Algorithm = "ssh-ed25519";
    private static readonly byte[] FirstKey = [0x01, 0x02, 0x03, 0x04, 0x05];
    private static readonly byte[] DifferentKey = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE];

    [Fact]
    public async Task MatchingKnownHost_SkipsVerifier_AndDoesNotEmitEvent()
    {
        var store = new HostKeyStore();
        var trustedFingerprint = HostKeyStore.ComputeFingerprint(FirstKey);
        store.Trust(Host, Port, trustedFingerprint);

        var verifier = new FixedDecisionVerifier(HostKeyDecision.Reject);
        var trustedEvents = 0;
        store.HostKeyEvent += (_, _, trusted) =>
        {
            if (trusted)
            {
                trustedEvents++;
            }
        };

        var canTrust = await ApplyDecisionAsync(store, verifier, FirstKey);

        Assert.True(canTrust);
        Assert.Equal(trustedFingerprint, store.GetFingerprint(Host, Port));
        Assert.Equal(0, verifier.CallCount);
        Assert.Equal(0, trustedEvents);
    }

    [Fact]
    public async Task FirstUseAccept_TrustsFingerprint_AndEmitsTrustedEvent()
    {
        var store = new HostKeyStore();
        var verifier = new FixedDecisionVerifier(HostKeyDecision.Accept);
        var trustedEvents = new List<(string Host, string Fingerprint, bool Trusted)>();
        store.HostKeyEvent += (host, fingerprint, trusted) =>
            trustedEvents.Add((host, fingerprint, trusted));

        var canTrust = await ApplyDecisionAsync(store, verifier, FirstKey);
        var storedFingerprint = store.GetFingerprint(Host, Port);

        Assert.True(canTrust);
        Assert.NotNull(storedFingerprint);
        Assert.Equal(1, verifier.CallCount);
        Assert.Null(verifier.LastStoredFingerprint);
        var trustedEvent = Assert.Single(trustedEvents);
        Assert.Equal($"{Host}:{Port}", trustedEvent.Host);
        Assert.Equal(storedFingerprint, trustedEvent.Fingerprint);
        Assert.True(trustedEvent.Trusted);
    }

    [Fact]
    public async Task FirstUseReject_DoesNotTrust_AndDoesNotEmitEvent()
    {
        var store = new HostKeyStore();
        var verifier = new FixedDecisionVerifier(HostKeyDecision.Reject);
        var eventRaised = false;
        store.HostKeyEvent += (_, _, _) => eventRaised = true;

        var canTrust = await ApplyDecisionAsync(store, verifier, FirstKey);

        Assert.False(canTrust);
        Assert.Null(store.GetFingerprint(Host, Port));
        Assert.Equal(1, verifier.CallCount);
        Assert.False(eventRaised);
    }

    [Fact]
    public async Task MismatchAccept_OverwritesStoredFingerprint()
    {
        var store = new HostKeyStore();
        store.Trust(Host, Port, HostKeyStore.ComputeFingerprint(FirstKey));
        var verifier = new FixedDecisionVerifier(HostKeyDecision.Accept);

        var canTrust = await ApplyDecisionAsync(store, verifier, DifferentKey);
        var storedFingerprint = store.GetFingerprint(Host, Port);

        Assert.True(canTrust);
        Assert.Equal(1, verifier.CallCount);
        Assert.NotNull(verifier.LastStoredFingerprint);
        Assert.Equal(HostKeyStore.ComputeFingerprint(DifferentKey), storedFingerprint);
    }

    [Fact]
    public async Task MismatchReject_PreservesStoredFingerprint()
    {
        var store = new HostKeyStore();
        var originalFingerprint = HostKeyStore.ComputeFingerprint(FirstKey);
        store.Trust(Host, Port, originalFingerprint);
        var verifier = new FixedDecisionVerifier(HostKeyDecision.Reject);
        var eventRaised = false;
        store.HostKeyEvent += (_, _, _) => eventRaised = true;

        var canTrust = await ApplyDecisionAsync(store, verifier, DifferentKey);

        Assert.False(canTrust);
        Assert.Equal(1, verifier.CallCount);
        Assert.Equal(originalFingerprint, verifier.LastStoredFingerprint);
        Assert.Equal(originalFingerprint, store.GetFingerprint(Host, Port));
        Assert.False(eventRaised);
    }

    private static async Task<bool> ApplyDecisionAsync(
        HostKeyStore store,
        IHostKeyVerifier verifier,
        byte[] hostKey,
        CancellationToken ct = default)
    {
        var result = store.Verify(Host, Port, hostKey);
        if (!result.FirstUse && result.Trusted)
        {
            return true;
        }

        var decision = await verifier.VerifyAsync(
            Host,
            Port,
            Algorithm,
            result.Fingerprint,
            result.StoredFingerprint,
            ct);

        if (decision == HostKeyDecision.Accept)
        {
            store.Trust(Host, Port, result.Fingerprint);
            return true;
        }

        return false;
    }

    private sealed class FixedDecisionVerifier(HostKeyDecision decision) : IHostKeyVerifier
    {
        public int CallCount { get; private set; }

        public string? LastStoredFingerprint { get; private set; }

        public Task<HostKeyDecision> VerifyAsync(
            string host,
            int port,
            string algorithm,
            string presentedFingerprint,
            string? storedFingerprint,
            CancellationToken ct = default)
        {
            CallCount++;
            LastStoredFingerprint = storedFingerprint;
            return Task.FromResult(decision);
        }
    }
}
