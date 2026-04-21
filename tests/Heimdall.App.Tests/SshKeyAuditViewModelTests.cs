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

using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Security;

namespace Heimdall.App.Tests;

public class SshKeyAuditViewModelTests
{
    private const string TestEd25519PublicKey =
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl test";

    [Fact]
    public void RunAudit_EmptyInput_ShowsEmptyState()
    {
        var sut = new SshKeyAuditViewModel();
        sut.Initialize(null);
        sut.KeyText = "   ";

        sut.RunAuditCommand.Execute(null);

        Assert.True(sut.ShowEmptyState);
        Assert.False(sut.ShowParseError);
        Assert.False(sut.ShowResults);
    }

    [Fact]
    public void RunAudit_InvalidKey_ShowsParseError()
    {
        var sut = new SshKeyAuditViewModel();
        sut.Initialize(null);
        sut.KeyText = "not a valid key at all";

        sut.RunAuditCommand.Execute(null);

        Assert.False(sut.ShowEmptyState);
        Assert.True(sut.ShowParseError);
        Assert.False(sut.ShowResults);
    }

    [Fact]
    public void RunAudit_ValidKey_ProjectsResultAndShowsResults()
    {
        var sut = new SshKeyAuditViewModel();
        sut.Initialize(null);
        sut.KeyText = TestEd25519PublicKey;

        sut.RunAuditCommand.Execute(null);

        Assert.True(sut.ShowResults);
        Assert.Equal("Ed25519", sut.Algorithm);
        Assert.Equal(256, sut.KeySize);
        Assert.Equal(SecurityRating.Strong, sut.Rating);
        Assert.StartsWith("SHA256:", sut.Fingerprint);
        Assert.NotEmpty(sut.Findings);
    }

    [Fact]
    public void RunAudit_AfterValidThenEmpty_ClearsResults()
    {
        var sut = new SshKeyAuditViewModel();
        sut.Initialize(null);
        sut.KeyText = TestEd25519PublicKey;
        sut.RunAuditCommand.Execute(null);

        Assert.True(sut.ShowResults);

        sut.KeyText = string.Empty;
        sut.RunAuditCommand.Execute(null);

        Assert.True(sut.ShowEmptyState);
        Assert.False(sut.ShowResults);
        Assert.Equal(string.Empty, sut.Fingerprint);
        Assert.Empty(sut.Findings);
    }
}
