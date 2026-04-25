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

using System.Security.Cryptography;
using System.Text;
using Heimdall.Core.Ssh;

namespace Heimdall.Core.Tests.Ssh;

public sealed class KnownHostsParserTests
{
    private const string SampleKey = "AQIDBAU=";

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyResult()
    {
        var result = KnownHostsParser.Parse(string.Empty);

        Assert.Empty(result.Entries);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_CommentsAndBlanks_SilentlySkipped()
    {
        var result = KnownHostsParser.Parse(
            """
            # comment

             
            """);

        Assert.Empty(result.Entries);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_SimpleEd25519Line_ProducesSingleEntry()
    {
        var result = KnownHostsParser.Parse($"host ssh-ed25519 {SampleKey}");

        var entry = Assert.Single(result.Entries);
        Assert.Equal("host", entry.Host);
        Assert.Equal(22, entry.Port);
        Assert.Equal("ssh-ed25519", entry.KeyType);
        Assert.Equal([0x01, 0x02, 0x03, 0x04, 0x05], entry.Base64Key);
    }

    [Fact]
    public void Parse_BracketedHostWithExplicitPort_ProducesCorrectPort()
    {
        var result = KnownHostsParser.Parse($"[host.example.com]:2222 ssh-ed25519 {SampleKey}");

        var entry = Assert.Single(result.Entries);
        Assert.Equal("host.example.com", entry.Host);
        Assert.Equal(2222, entry.Port);
    }

    [Fact]
    public void Parse_MultiHostCommaList_ExpandsToOneEntryPerHost()
    {
        var result = KnownHostsParser.Parse($"host1,[host2]:2222,host3 ssh-ed25519 {SampleKey}");

        Assert.Collection(
            result.Entries,
            first =>
            {
                Assert.Equal("host1", first.Host);
                Assert.Equal(22, first.Port);
            },
            second =>
            {
                Assert.Equal("host2", second.Host);
                Assert.Equal(2222, second.Port);
            },
            third =>
            {
                Assert.Equal("host3", third.Host);
                Assert.Equal(22, third.Port);
            });
    }

    [Fact]
    public void Parse_MultiHostWithOneWildcard_SkipsOnlyThatToken()
    {
        var result = KnownHostsParser.Parse($"host1,*.wild,host3 ssh-ed25519 {SampleKey}");

        Assert.Equal(2, result.Entries.Count);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticCode.UnsupportedHostPattern, diagnostic.Code);
        Assert.Equal("*.wild", diagnostic.Context);
    }

    [Fact]
    public void Parse_HashedEntry_ProducesHashedEntry()
    {
        var hashedHost = CreateHashedHost("host.example.com", "test-salt");

        var result = KnownHostsParser.Parse($"{hashedHost} ssh-ed25519 {SampleKey}");

        var entry = Assert.Single(result.Entries);
        Assert.Equal(hashedHost, entry.Host);
        Assert.Equal(22, entry.Port);
        Assert.True(entry.IsHashedHost);
        Assert.Empty(result.Diagnostics);
        Assert.True(KnownHostsHash.TryMatches(hashedHost, "host.example.com"));
        Assert.False(KnownHostsHash.TryMatches(hashedHost, "other.example.com"));
    }

    [Fact]
    public void Parse_MalformedHashedEntry_SkippedWithDiagnostic()
    {
        var result = KnownHostsParser.Parse($"|1|bad-salt|bad-hash ssh-ed25519 {SampleKey}");

        Assert.Empty(result.Entries);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticCode.MalformedLine, diagnostic.Code);
        Assert.Equal("bad hashed host", diagnostic.Context);
    }

    [Fact]
    public void Parse_CertAuthorityMarker_SkippedWithDiagnostic()
    {
        var result = KnownHostsParser.Parse($"@cert-authority host ssh-ed25519 {SampleKey}");

        Assert.Empty(result.Entries);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticCode.CertAuthorityNotSupported, diagnostic.Code);
    }

    [Fact]
    public void Parse_RevokedMarker_SkippedWithDiagnostic()
    {
        var result = KnownHostsParser.Parse($"@revoked host ssh-ed25519 {SampleKey}");

        Assert.Empty(result.Entries);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticCode.RevokedEntryNotSupported, diagnostic.Code);
    }

    [Fact]
    public void Parse_UnsupportedKeyType_SkippedWithDiagnostic()
    {
        var result = KnownHostsParser.Parse($"host ssh-fake {SampleKey}");

        Assert.Empty(result.Entries);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticCode.UnsupportedKeyType, diagnostic.Code);
        Assert.Equal("ssh-fake", diagnostic.Context);
    }

    [Fact]
    public void Parse_MalformedBase64_SkippedWithDiagnostic()
    {
        var result = KnownHostsParser.Parse("host ssh-ed25519 !!!notbase64!!!");

        Assert.Empty(result.Entries);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticCode.MalformedLine, diagnostic.Code);
        Assert.Equal("bad base64", diagnostic.Context);
    }

    [Fact]
    public void Parse_MultipleAlgorithmsForSameHost_ProducesMultipleEntries()
    {
        var result = KnownHostsParser.Parse(
            $"""
            host.example.com ssh-ed25519 {SampleKey}
            host.example.com ecdsa-sha2-nistp256 {SampleKey}
            """);

        Assert.Equal(2, result.Entries.Count);
        Assert.Contains(result.Entries, entry => entry.KeyType == "ssh-ed25519");
        Assert.Contains(result.Entries, entry => entry.KeyType == "ecdsa-sha2-nistp256");
    }

    [Fact]
    public void Parse_SshDss_ParsesWithLegacyDiagnostic()
    {
        var result = KnownHostsParser.Parse($"legacy.example.com ssh-dss {SampleKey}");

        var entry = Assert.Single(result.Entries);
        Assert.Equal("legacy.example.com", entry.Host);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticLevel.Warning, diagnostic.Level);
        Assert.Equal(KnownHostsDiagnosticCode.LegacyKeyType, diagnostic.Code);
    }

    [Fact]
    public void Parse_LargeFile_ParsesAllEntries()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 1001; i++)
        {
            builder.Append("host")
                .Append(i)
                .Append(" ssh-ed25519 ")
                .AppendLine(SampleKey);
        }

        var result = KnownHostsParser.Parse(builder.ToString());

        Assert.Equal(1001, result.Entries.Count);
        Assert.Empty(result.Diagnostics);
    }

    private static string CreateHashedHost(string host, string saltText)
    {
        var salt = Encoding.UTF8.GetBytes(saltText);
        using var hmac = new HMACSHA1(salt);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(host));
        return $"|1|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(hash)}";
    }

    // ── DoS protection: oversized lines and file streaming ────────────

    [Fact]
    public void Parse_LineExceedingMaxLength_EmitsMalformedDiagnostic()
    {
        var hugeLine = new string('a', KnownHostsParser.MaxLineLength + 1);

        var result = KnownHostsParser.Parse(hugeLine);

        Assert.Empty(result.Entries);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticLevel.Warning, diagnostic.Level);
        Assert.Equal(KnownHostsDiagnosticCode.MalformedLine, diagnostic.Code);
        Assert.Equal(KnownHostsParser.LineTooLongContext, diagnostic.Context);
    }

    [Fact]
    public void Parse_TextReader_StreamsLineByLine()
    {
        var content = $"host1 ssh-ed25519 {SampleKey}\nhost2 ssh-ed25519 {SampleKey}\n";
        using var reader = new StringReader(content);

        var result = KnownHostsParser.Parse(reader);

        Assert.Equal(2, result.Entries.Count);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_TextReader_RejectsTooLongLineButContinues()
    {
        var hugeLine = new string('z', KnownHostsParser.MaxLineLength + 1);
        var content = $"good ssh-ed25519 {SampleKey}\n{hugeLine}\nalsogood ssh-ed25519 {SampleKey}\n";
        using var reader = new StringReader(content);

        var result = KnownHostsParser.Parse(reader);

        Assert.Equal(2, result.Entries.Count);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticCode.MalformedLine, diagnostic.Code);
        Assert.Equal(2, diagnostic.SourceLineNumber);
    }

    [Fact]
    public void IsSupportedKeyType_AllowsKnownAlgorithms()
    {
        Assert.True(KnownHostsParser.IsSupportedKeyType("ssh-ed25519"));
        Assert.True(KnownHostsParser.IsSupportedKeyType("ecdsa-sha2-nistp256"));
        Assert.True(KnownHostsParser.IsSupportedKeyType("ssh-rsa"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("rsa-sha2-512")] // not in OpenSSH host-key allow-list
    [InlineData("MyBadAlgo-2000")]
    public void IsSupportedKeyType_RejectsUnknownOrEmpty(string? algorithm)
    {
        Assert.False(KnownHostsParser.IsSupportedKeyType(algorithm));
    }
}
