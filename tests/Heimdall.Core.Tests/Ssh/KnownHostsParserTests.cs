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
    public void Parse_HashedEntry_SkippedWithDiagnostic()
    {
        var result = KnownHostsParser.Parse($"|1|salt|hash ssh-ed25519 {SampleKey}");

        Assert.Empty(result.Entries);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticCode.HashedEntryNotSupported, diagnostic.Code);
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
}
