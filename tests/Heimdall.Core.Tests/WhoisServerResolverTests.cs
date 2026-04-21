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

using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public sealed class WhoisServerResolverTests
{
    [Theory]
    [InlineData("example.com", "whois.verisign-grs.com")]
    [InlineData("example.net", "whois.verisign-grs.com")]
    [InlineData("example.org", "whois.pir.org")]
    [InlineData("example.io", "whois.nic.io")]
    [InlineData("example.dev", "whois.nic.google")]
    [InlineData("example.fr", "whois.nic.fr")]
    [InlineData("example.de", "whois.denic.de")]
    [InlineData("example.uk", "whois.nic.uk")]
    [InlineData("example.eu", "whois.eu")]
    [InlineData("example.nl", "whois.domain-registry.nl")]
    [InlineData("example.be", "whois.dns.be")]
    [InlineData("example.ch", "whois.nic.ch")]
    [InlineData("example.au", "whois.auda.org.au")]
    [InlineData("example.ca", "whois.cira.ca")]
    [InlineData("example.jp", "whois.jprs.jp")]
    public void GetWhoisServer_KnownTlds_ReturnsRegistrySpecificServer(string domain, string expected)
    {
        Assert.Equal(expected, WhoisServerResolver.GetWhoisServer(domain));
    }

    [Theory]
    [InlineData("example.test")]
    [InlineData("example.zzz")]
    [InlineData("example.xyz123")]
    public void GetWhoisServer_UnknownTld_ReturnsIanaFallback(string domain)
    {
        Assert.Equal(WhoisServerResolver.IanaServer, WhoisServerResolver.GetWhoisServer(domain));
    }

    [Theory]
    [InlineData("EXAMPLE.COM", "whois.verisign-grs.com")]
    [InlineData("Example.Com", "whois.verisign-grs.com")]
    [InlineData("example.COM", "whois.verisign-grs.com")]
    [InlineData("example.FR", "whois.nic.fr")]
    public void GetWhoisServer_IsCaseInsensitive(string domain, string expected)
    {
        Assert.Equal(expected, WhoisServerResolver.GetWhoisServer(domain));
    }

    [Fact]
    public void GetWhoisServer_SubdomainUsesLastSegment()
    {
        Assert.Equal("whois.nic.fr", WhoisServerResolver.GetWhoisServer("a.b.c.example.fr"));
    }

    [Theory]
    [InlineData("bareword")]
    [InlineData("8")]
    [InlineData("test")]
    public void GetWhoisServer_NoDot_ReturnsIanaFallback(string domain)
    {
        Assert.Equal(WhoisServerResolver.IanaServer, WhoisServerResolver.GetWhoisServer(domain));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void GetWhoisServer_NullOrWhitespace_ReturnsIanaFallback(string? domain)
    {
        Assert.Equal(WhoisServerResolver.IanaServer, WhoisServerResolver.GetWhoisServer(domain));
    }

    [Fact]
    public void GetWhoisServer_TrailingDot_ReturnsIanaFallback()
    {
        Assert.Equal(WhoisServerResolver.IanaServer, WhoisServerResolver.GetWhoisServer("example."));
    }

    [Fact]
    public void GetWhoisServer_LeadingDot_UsesLastSegment()
    {
        Assert.Equal("whois.verisign-grs.com", WhoisServerResolver.GetWhoisServer(".com"));
    }

    [Fact]
    public void IanaServer_Constant_IsExpectedHostname()
    {
        Assert.Equal("whois.iana.org", WhoisServerResolver.IanaServer);
    }
}
