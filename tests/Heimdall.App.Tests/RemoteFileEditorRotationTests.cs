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
using Heimdall.Sftp;
using Heimdall.Ssh;

namespace Heimdall.App.Tests;

public sealed class RemoteFileEditorRotationTests
{
    [Fact]
    public void PinnedFingerprintVerifier_Matches_RejectsDifferentFingerprint()
    {
        var verifier = new PinnedFingerprintVerifier("gw.example.com", 22, "SHA256:AAA");

        Assert.True(verifier.Matches("gw.example.com", 22, "SHA256:AAA"));
        Assert.False(verifier.Matches("gw.example.com", 22, "SHA256:BBB"));
        Assert.False(verifier.Matches("other.example.com", 22, "SHA256:AAA"));
        Assert.False(verifier.Matches("gw.example.com", 23, "SHA256:AAA"));
    }

    [Fact]
    public async Task PinnedFingerprintVerifier_VerifyAsync_RejectsRotation()
    {
        var verifier = new PinnedFingerprintVerifier("gw.example.com", 22, "SHA256:AAA");

        var decision = await verifier.VerifyAsync(
            "gw.example.com",
            22,
            "ssh-ed25519",
            presentedFingerprint: "SHA256:BBB",
            storedFingerprint: "SHA256:AAA",
            ct: CancellationToken.None);

        Assert.Equal(HostKeyDecision.Reject, decision);
    }

    [Fact]
    public void EditSession_Verifier_IsCachedAfterSudoEditSessionConstruction()
    {
        var verifier = new PinnedFingerprintVerifier("gw.example.com", 22, "SHA256:AAA");
        using var sudoSession = new EditSession
        {
            RemotePath = "/etc/ssh/sshd_config",
            LocalPath = @"C:\Temp\sshd_config",
            IsSudo = true,
            SshParams = MakeFakeParams(),
            Verifier = verifier
        };

        Assert.True(sudoSession.IsSudo);
        Assert.Same(verifier, sudoSession.Verifier);

        using var directSession = new EditSession
        {
            RemotePath = "/home/user/readme.txt",
            LocalPath = @"C:\Temp\readme.txt",
            IsSudo = false,
            Verifier = null
        };

        Assert.False(directSession.IsSudo);
        Assert.Null(directSession.Verifier);
    }

    [Fact]
    public async Task UploadWithSudoAsync_ThrowsWhenVerifierMissing()
    {
        using var session = new EditSession
        {
            RemotePath = "/etc/ssh/sshd_config",
            LocalPath = @"C:\Temp\sshd_config",
            IsSudo = true,
            SshParams = MakeFakeParams(),
            Verifier = null
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RemoteFileEditor.UploadWithSudoAsync(session));

        Assert.Contains("verifier", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostKeyRotationEvent_RecordEqualsByValue()
    {
        var first = new HostKeyRotationEvent(
            "/etc/ssh/sshd_config",
            "SHA256:BBB",
            "SHA256:AAA",
            "gw.example.com",
            22);
        var second = new HostKeyRotationEvent(
            "/etc/ssh/sshd_config",
            "SHA256:BBB",
            "SHA256:AAA",
            "gw.example.com",
            22);
        var different = new HostKeyRotationEvent(
            "/etc/ssh/sshd_config",
            "SHA256:CCC",
            "SHA256:AAA",
            "gw.example.com",
            22);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.NotEqual(first, different);
    }

    private static SshConnectionParams MakeFakeParams()
    {
        return new SshConnectionParams
        {
            Host = "gw.example.com",
            Port = 22,
            Username = "root",
            Password = "secret",
            ConnectTimeout = TimeSpan.FromMilliseconds(100)
        };
    }
}
