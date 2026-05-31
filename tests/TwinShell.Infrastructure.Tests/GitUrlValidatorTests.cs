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

using FluentAssertions;
using TwinShell.Core.Utilities;

namespace TwinShell.Infrastructure.Tests;

public sealed class GitUrlValidatorTests
{
    [Theory]
    [InlineData("https://github.com/o/r.git", true)]
    [InlineData("https://github.com/o/r.git", false)]
    [InlineData("ssh://git@host/o/r.git", true)]
    [InlineData("git@github.com:org/repo.git", true)]
    [InlineData("git://host/o/r.git", false)]
    [InlineData("http://host/o/r.git", false)]
    public void IsAllowed_AcceptsSupportedGitRemoteUrls(string url, bool hasToken)
    {
        bool allowed = GitUrlValidator.IsAllowed(url, hasToken, out string failureReason);

        allowed.Should().BeTrue();
        failureReason.Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://host/o/r.git", true, "cleartext")]
    [InlineData("file:///C:/repo", false, "file://")]
    [InlineData("ftp://host/o/r.git", false, "Unsupported Git remote scheme")]
    [InlineData(null, false, "not configured")]
    [InlineData("", false, "not configured")]
    [InlineData("   ", false, "not configured")]
    [InlineData("not a url", false, "valid URL")]
    public void IsAllowed_RejectsUnsafeOrInvalidGitRemoteUrls(string? url, bool hasToken, string expectedReasonFragment)
    {
        bool allowed = GitUrlValidator.IsAllowed(url, hasToken, out string failureReason);

        allowed.Should().BeFalse();
        failureReason.Should().Contain(expectedReasonFragment);
    }
}
