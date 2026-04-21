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

public sealed class KnownHostsParserIpv6Tests
{
    private const string SampleKey = "AQIDBAU=";

    [Fact]
    public void Parse_BareIpv6_DefaultsToPort22()
    {
        var result = KnownHostsParser.Parse($"::1 ssh-ed25519 {SampleKey}");

        var entry = Assert.Single(result.Entries);
        Assert.Equal("::1", entry.Host);
        Assert.Equal(22, entry.Port);
    }

    [Fact]
    public void Parse_BracketedIpv6_DefaultsToPort22()
    {
        var result = KnownHostsParser.Parse($"[::1] ssh-ed25519 {SampleKey}");

        var entry = Assert.Single(result.Entries);
        Assert.Equal("::1", entry.Host);
        Assert.Equal(22, entry.Port);
    }

    [Fact]
    public void Parse_BareColonNonIpv6_SkippedAsUnsupported()
    {
        var result = KnownHostsParser.Parse($"foo:2222 ssh-ed25519 {SampleKey}");

        Assert.Empty(result.Entries);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticCode.UnsupportedHostPattern, diagnostic.Code);
        Assert.Equal("bare colon non-IPv6", diagnostic.Context);
    }

    [Fact]
    public void Parse_ZoneIdentifiedIpv6_SkippedWithDiagnostic()
    {
        var result = KnownHostsParser.Parse($"[fe80::1%eth0]:22 ssh-ed25519 {SampleKey}");

        Assert.Empty(result.Entries);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(KnownHostsDiagnosticCode.UnsupportedHostPattern, diagnostic.Code);
    }
}
