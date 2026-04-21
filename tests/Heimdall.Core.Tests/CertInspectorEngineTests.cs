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

using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Heimdall.Core.Security;

namespace Heimdall.Core.Tests;

public class CertInspectorEngineTests
{
    [Theory]
    [InlineData("example.com", "example.com", true)]
    [InlineData("EXAMPLE.com", "example.com", true)]
    [InlineData("*.example.com", "api.example.com", true)]
    [InlineData("*.example.com", "deep.api.example.com", false)]
    [InlineData("*.example.com", "example.com", false)]
    [InlineData("other.example.com", "api.example.com", false)]
    public void MatchesHostname_ReturnsExpected(string pattern, string host, bool expected)
    {
        var result = CertInspectorEngine.MatchesHostname(pattern, host);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("CN=example.com", "example.com")]
    [InlineData("O=Test, CN=example.com, C=FR", "example.com")]
    [InlineData("CN=example.com, O=Test", "example.com")]
    [InlineData("O=Test, C=FR", "")]
    public void ExtractCn_ReturnsExpected(string subject, string expected)
    {
        var result = CertInspectorEngine.ExtractCn(subject);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("443", new[] { 443 })]
    [InlineData("443,8443", new[] { 443, 8443 })]
    [InlineData("443,443,8443", new[] { 443, 8443 })]
    [InlineData("443, 8443-8445, 9443", new[] { 443, 8443, 8444, 8445, 9443 })]
    public void ParsePorts_ReturnsOrderedUniquePorts(string input, int[] expected)
    {
        var result = CertInspectorEngine.ParsePorts(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("abc,99999,-1", 0)]
    [InlineData("80-70", 0)]
    [InlineData("80,443-445,443", 4)]
    public void ParsePorts_HandlesInvalidSegments(string input, int expectedCount)
    {
        var result = CertInspectorEngine.ParsePorts(input);
        Assert.Equal(expectedCount, result.Count);
    }

    [Theory]
    [InlineData(-10, CertExpirationStatus.Expired)]
    [InlineData(-1, CertExpirationStatus.Expired)]
    [InlineData(0, CertExpirationStatus.Warning)]
    [InlineData(1, CertExpirationStatus.Warning)]
    [InlineData(30, CertExpirationStatus.Warning)]
    [InlineData(31, CertExpirationStatus.Valid)]
    [InlineData(365, CertExpirationStatus.Valid)]
    public void DetermineExpirationStatus_ReturnsExpected(int daysRemaining, CertExpirationStatus expected)
    {
        var result = CertInspectorEngine.DetermineExpirationStatus(daysRemaining);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("safe-name", "safe-name")]
    [InlineData("name<with>chars", "name_with_chars")]
    [InlineData("cert:example/443", "cert_example_443")]
    public void SanitizeFileName_ReplacesInvalidCharacters(string input, string expected)
    {
        var result = CertInspectorEngine.SanitizeFileName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractSans_ReturnsDnsAndIpAddresses()
    {
        using var cert = CreateCertificate("CN=test.example.com", "test.example.com", IPAddress.Parse("127.0.0.1"));

        var sans = CertInspectorEngine.ExtractSans(cert);

        Assert.Contains("test.example.com", sans);
        Assert.Contains("127.0.0.1", sans);
    }

    [Fact]
    public void CheckHostnameMatch_UsesSansBeforeCn()
    {
        using var cert = CreateCertificate("CN=other.example.com", "test.example.com");
        var sans = CertInspectorEngine.ExtractSans(cert);

        var result = CertInspectorEngine.CheckHostnameMatch(cert, sans, "test.example.com");

        Assert.True(result);
    }

    [Fact]
    public void CheckHostnameMatch_FallsBackToCnWhenSansMissing()
    {
        using var cert = CreateCertificateWithoutSans("CN=test.example.com");

        var result = CertInspectorEngine.CheckHostnameMatch(cert, [], "test.example.com");

        Assert.True(result);
    }

    [Fact]
    public void BuildSummary_UsesProbeResultAndFormatsFields()
    {
        using var cert = CreateCertificate("CN=test.example.com", "test.example.com");
        using var probe = new CertProbeResult
        {
            Certificate = cert,
            Protocol = SslProtocols.Tls13,
            Chain = CertInspectorEngine.BuildChain(cert)
        };

        var summary = CertInspectorEngine.BuildSummary(probe, "test.example.com", 443);

        Assert.Equal("CN=test.example.com", summary.Subject);
        Assert.Equal(443, summary.Port);
        Assert.Equal("TLS 1.3", summary.TlsVersion);
        Assert.True(summary.HostnameMatches);
        Assert.True(summary.KeySizeBits >= 2048);
        Assert.NotEmpty(summary.Thumbprint);
        Assert.NotEmpty(summary.Chain);
    }

    [Fact]
    public void BuildDetailsText_ContainsKeyCertificateFields()
    {
        var summary = CreateSummary();

        var details = CertInspectorEngine.BuildDetailsText(summary, "test.example.com");

        Assert.Contains("ToolCertDetailHost", details);
        Assert.Contains("test.example.com:443", details);
        Assert.Contains(summary.Subject, details);
        Assert.Contains(summary.Thumbprint, details);
    }

    [Fact]
    public void BuildScanCsv_ContainsHeaderAndData()
    {
        var csv = CertInspectorEngine.BuildScanCsv([CreateSummary()]);

        Assert.Contains("ToolCertCsvPort", csv);
        Assert.Contains("443", csv);
        Assert.Contains("test.example.com", csv);
    }

    [Fact]
    public void BuildScanCsv_UsesServiceLabelAndEscapesQuotes()
    {
        var source = CreateSummary();
        var summary = new CertInspectionSummary
        {
            Subject = "CN=\"quoted\"",
            Issuer = source.Issuer,
            NotBefore = source.NotBefore,
            NotAfter = source.NotAfter,
            DaysRemaining = source.DaysRemaining,
            Serial = source.Serial,
            Thumbprint = source.Thumbprint,
            SigAlgorithm = source.SigAlgorithm,
            KeySizeBits = source.KeySizeBits,
            Sans = source.Sans,
            TlsVersion = source.TlsVersion,
            HostnameMatches = source.HostnameMatches,
            ExpirationStatus = source.ExpirationStatus,
            Chain = source.Chain,
            Port = source.Port
        };
        var csv = CertInspectorEngine.BuildScanCsv(
            [summary],
            key => key,
            port => $"svc-{port}");

        Assert.Contains("\"svc-443\"", csv);
        Assert.Contains("\"CN=\"\"quoted\"\"\"", csv);
    }

    [Fact]
    public void BuildChain_ReturnsAtLeastLeafCertificate()
    {
        using var cert = CreateCertificate("CN=test.example.com", "test.example.com");

        var chain = CertInspectorEngine.BuildChain(cert);

        Assert.NotEmpty(chain);
        Assert.Contains(chain, element => element.Subject.Contains("test.example.com", StringComparison.Ordinal));
    }

    private static X509Certificate2 CreateCertificate(string cn, string dnsName, IPAddress? ipAddress = null)
    {
        using var rsa = RSA.Create(2048);
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(dnsName);
        if (ipAddress is not null)
            sanBuilder.AddIpAddress(ipAddress);

        var request = new CertificateRequest(
            cn,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(sanBuilder.Build());

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));
    }

    private static X509Certificate2 CreateCertificateWithoutSans(string cn)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            cn,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));
    }

    private static CertInspectionSummary CreateSummary()
    {
        return new CertInspectionSummary
        {
            Subject = "CN=test.example.com",
            Issuer = "CN=Test CA",
            NotBefore = DateTime.UtcNow.AddDays(-1),
            NotAfter = DateTime.UtcNow.AddDays(365),
            DaysRemaining = 365,
            Serial = "ABC123",
            Thumbprint = "DEADBEEF",
            SigAlgorithm = "sha256RSA",
            KeySizeBits = 2048,
            Sans = ["test.example.com"],
            TlsVersion = "TLS 1.3",
            HostnameMatches = true,
            ExpirationStatus = CertExpirationStatus.Valid,
            Chain =
            [
                new CertChainElement
                {
                    Subject = "CN=test.example.com",
                    Expiry = DateTime.UtcNow.AddDays(365).ToString("yyyy-MM-dd HH:mm:ss UTC")
                }
            ],
            Port = 443
        };
    }
}
