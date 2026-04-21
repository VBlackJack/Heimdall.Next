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

namespace Heimdall.Core.Tests.Ssh;

public sealed class OpenSshConfigParserTests
{
    [Fact]
    public void Parse_EmptyString_ReturnsEmptyResult()
    {
        var result = OpenSshConfigParser.Parse(string.Empty);

        Assert.Empty(result.Candidates);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_SimpleHost_ReturnsOneCandidateWithAllFields()
    {
        var result = OpenSshConfigParser.Parse(
            """
            Host prod
                HostName server.example.com
                Port 2222
                User alice
                IdentityFile ~/.ssh/id_ed25519
            """);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("prod", candidate.Alias);
        Assert.Equal("server.example.com", candidate.HostName);
        Assert.Equal(2222, candidate.Port);
        Assert.Equal("alice", candidate.User);
        Assert.NotNull(candidate.IdentityFile);
        Assert.Contains($"{Path.DirectorySeparatorChar}.ssh{Path.DirectorySeparatorChar}id_ed25519", candidate.IdentityFile!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_HostWithoutHostName_FallsBackToAlias_AndEmitsDiagnostic()
    {
        var result = OpenSshConfigParser.Parse(
            """
            Host prod
                User alice
            """);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("prod", candidate.HostName);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(OpenSshDiagnosticCode.HostNameFallbackToAlias, diagnostic.Code);
        Assert.Equal("prod", diagnostic.Context);
    }

    [Fact]
    public void Parse_MultiAliasHostLine_ProducesMultipleCandidates()
    {
        var result = OpenSshConfigParser.Parse(
            """
            Host prod1 prod2 bastion
                HostName server.example.com
            """);

        Assert.Equal(3, result.Candidates.Count);
        Assert.Collection(
            result.Candidates,
            first => Assert.Equal("prod1", first.Alias),
            second => Assert.Equal("prod2", second.Alias),
            third => Assert.Equal("bastion", third.Alias));
    }

    [Fact]
    public void Parse_WildcardAlias_SkippedWithDiagnostic()
    {
        var result = OpenSshConfigParser.Parse(
            """
            Host * web-? !prod prod
                HostName server.example.com
            """);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("prod", candidate.Alias);
        Assert.Equal(3, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(OpenSshDiagnosticCode.WildcardAliasIgnored, diagnostic.Code));
    }

    [Fact]
    public void Parse_IdentityFileWithTilde_ExpandsToUserProfile_AndEmitsDiagnostic()
    {
        var result = OpenSshConfigParser.Parse(
            """
            Host prod
                IdentityFile ~/.ssh/id_ed25519
            """);

        var candidate = Assert.Single(result.Candidates);
        Assert.NotNull(candidate.IdentityFile);
        Assert.DoesNotContain('~', candidate.IdentityFile);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == OpenSshDiagnosticCode.IdentityFileTildeExpanded);
    }

    [Fact]
    public void Parse_IncludeDirective_IgnoredWithDiagnostic_AndContinues()
    {
        var result = OpenSshConfigParser.Parse(
            """
            Include ~/.ssh/conf.d/*.conf
            Host prod
                HostName server.example.com
            """);

        Assert.Single(result.Candidates);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(OpenSshDiagnosticCode.IncludeDirectiveIgnored, diagnostic.Code);
    }

    [Fact]
    public void Parse_MatchBlock_IgnoredUntilNextHost()
    {
        var result = OpenSshConfigParser.Parse(
            """
            Host prod
                HostName server.example.com
            Match host *.internal
                User should-not-apply
            Host logs
                HostName logs.example.com
            """);

        Assert.Equal(2, result.Candidates.Count);
        Assert.Null(result.Candidates[1].User);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == OpenSshDiagnosticCode.MatchBlockIgnored);
    }

    [Fact]
    public void Parse_ProxyJump_CapturedInDiagnostics_NotStoredOnCandidate()
    {
        var result = OpenSshConfigParser.Parse(
            """
            Host prod
                ProxyJump bastion
            """);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("prod", candidate.HostName);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == OpenSshDiagnosticCode.ProxyJumpCapturedButNotMapped);
        Assert.Equal("bastion", diagnostic.Context);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("65536")]
    public void Parse_InvalidPort_DefaultsTo22WithDiagnostic(string rawPort)
    {
        var result = OpenSshConfigParser.Parse(
            $$"""
            Host prod
                Port {{rawPort}}
            """);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(22, candidate.Port);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == OpenSshDiagnosticCode.InvalidPort);
        Assert.Equal(rawPort, diagnostic.Context);
    }

    [Fact]
    public void Parse_DuplicateAliasInFile_FirstWins_AndEmitsDiagnostic()
    {
        var result = OpenSshConfigParser.Parse(
            """
            Host prod
                HostName first.example.com
            Host PROD
                HostName second.example.com
            """);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("first.example.com", candidate.HostName);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == OpenSshDiagnosticCode.DuplicateAliasInFile);
        Assert.Equal("PROD", diagnostic.Context);
    }

    [Fact]
    public void Parse_CommentsAndCasingAndBlankLines_AreTolerated()
    {
        var result = OpenSshConfigParser.Parse(
            """
            # comment

            HoSt "prod"   # trailing comment
                HoStNaMe "server.example.com"
                uSeR alice
                UnknownThing yes
            """);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("prod", candidate.Alias);
        Assert.Equal("server.example.com", candidate.HostName);
        Assert.Equal("alice", candidate.User);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == OpenSshDiagnosticCode.UnknownDirectiveIgnored);
    }
}
