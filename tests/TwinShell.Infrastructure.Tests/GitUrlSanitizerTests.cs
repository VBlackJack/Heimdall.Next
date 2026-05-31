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

public sealed class GitUrlSanitizerTests
{
    [Theory]
    [InlineData("https://user:pat@github.com/org/repo.git", "https://github.com/org/repo.git")]
    [InlineData("https://github.com/org/repo.git", "https://github.com/org/repo.git")]
    [InlineData("https://ghp_token@gitlab.com/o/r.git", "https://gitlab.com/o/r.git")]
    [InlineData("ssh://git:secret@host:2222/o/r.git", "ssh://host:2222/o/r.git")]
    [InlineData("git@github.com:org/repo.git", "github.com:org/repo.git")]
    [InlineData(null, "(none)")]
    [InlineData("", "(none)")]
    [InlineData("   ", "(none)")]
    [InlineData("plain text with no credential marker", "plain text with no credential marker")]
    public void SanitizeForLogging_RedactsCredentialBearingUrls(string? url, string expected)
    {
        string actual = GitUrlSanitizer.SanitizeForLogging(url);

        actual.Should().Be(expected);
    }

    [Fact]
    public void RedactCredentials_RemovesEmbeddedUrlCredentialsFromText()
    {
        const string Text = "fatal: unable to access 'https://user:pat@github.com/o/r.git/': 403";
        const string Expected = "fatal: unable to access 'https://github.com/o/r.git/': 403";

        string? actual = GitUrlSanitizer.RedactCredentials(Text);

        actual.Should().Be(Expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void RedactCredentials_PreservesNullAndEmptyValues(string? text, string? expected)
    {
        string? actual = GitUrlSanitizer.RedactCredentials(text);

        actual.Should().Be(expected);
    }
}
