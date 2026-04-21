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

using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Heimdall.Core.Security;

namespace Heimdall.Core.Tests;

public class TlsAuditEngineTests
{
    [Theory]
    [MemberData(nameof(GradeTestCases))]
    public void CalculateGrade_ReturnsExpected(
        List<TlsProtocolTestResult> protocols,
        List<TlsCipherInfo> ciphers,
        TlsGrade expected)
    {
        var result = TlsAuditEngine.CalculateGrade(protocols, ciphers);
        Assert.Equal(expected, result);
    }

    public static IEnumerable<object[]> GradeTestCases()
    {
        yield return
        [
            MakeProtocols(ssl3: true, tls10: false, tls11: false, tls12: true, tls13: true),
            MakeCiphers("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384"),
            TlsGrade.F
        ];

        yield return
        [
            MakeProtocols(ssl3: false, tls10: false, tls11: false, tls12: true, tls13: false),
            MakeCiphers("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384", "TLS_RSA_WITH_3DES_EDE_CBC_SHA"),
            TlsGrade.D
        ];

        yield return
        [
            MakeProtocols(ssl3: false, tls10: true, tls11: false, tls12: true, tls13: false),
            MakeCiphers("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384"),
            TlsGrade.C
        ];

        yield return
        [
            MakeProtocols(ssl3: false, tls10: false, tls11: true, tls12: true, tls13: false),
            MakeCiphers("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384"),
            TlsGrade.C
        ];

        yield return
        [
            MakeProtocols(ssl3: false, tls10: false, tls11: false, tls12: true, tls13: false),
            MakeCiphers("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384", "TLS_RSA_WITH_AES_256_CBC_SHA256"),
            TlsGrade.B
        ];

        yield return
        [
            MakeProtocols(ssl3: false, tls10: false, tls11: false, tls12: true, tls13: true),
            MakeCiphers("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384"),
            TlsGrade.APlus
        ];

        yield return
        [
            MakeProtocols(ssl3: false, tls10: false, tls11: false, tls12: true, tls13: false),
            MakeCiphers("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384"),
            TlsGrade.A
        ];

        yield return
        [
            MakeProtocols(ssl3: false, tls10: false, tls11: false, tls12: false, tls13: false),
            new List<TlsCipherInfo>(),
            TlsGrade.T
        ];
    }

    [Fact]
    public void ClassifyCipher_EcdheAesGcm_ReturnsStrong()
    {
        var (strength, kx, auth, enc) = TlsAuditEngine.ClassifyCipher(
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384);

        Assert.Equal(CipherStrength.Strong, strength);
        Assert.Equal("ECDHE", kx);
        Assert.Equal("RSA", auth);
        Assert.Equal("AES-256-GCM", enc);
    }

    [Fact]
    public void ClassifyCipher_RsaCbc_ReturnsAcceptable()
    {
        var (strength, kx, _, enc) = TlsAuditEngine.ClassifyCipher(
            TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256);

        Assert.Equal(CipherStrength.Acceptable, strength);
        Assert.Equal("RSA", kx);
        Assert.Equal("AES-256-CBC", enc);
    }

    [Fact]
    public void ClassifyCipher_3Des_ReturnsWeak()
    {
        var (strength, _, _, _) = TlsAuditEngine.ClassifyCipher(
            TlsCipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA);

        Assert.Equal(CipherStrength.Weak, strength);
    }

    [Fact]
    public void ClassifyCipher_Rc4_ReturnsWeak()
    {
        var (strength, _, _, _) = TlsAuditEngine.ClassifyCipher(
            TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA);

        Assert.Equal(CipherStrength.Weak, strength);
    }

    [Fact]
    public void BuildFindings_Ssl3Enabled_ReturnsCriticalFinding()
    {
        var protocols = MakeProtocols(ssl3: true, tls10: false, tls11: false, tls12: true, tls13: true);
        var ciphers = MakeCiphers("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384");

        var findings = TlsAuditEngine.BuildFindings(protocols, ciphers);

        Assert.Contains(findings, f => f.Severity == TlsFindingSeverity.Critical);
    }

    [Fact]
    public void BuildFindings_NoTls13_ReturnsInfoFinding()
    {
        var protocols = MakeProtocols(ssl3: false, tls10: false, tls11: false, tls12: true, tls13: false);
        var ciphers = MakeCiphers("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384");

        var findings = TlsAuditEngine.BuildFindings(protocols, ciphers);

        Assert.Contains(findings, f => f.Severity == TlsFindingSeverity.Info);
    }

    [Fact]
    public void BuildFindings_AllClean_ReturnsPassOnly()
    {
        var protocols = MakeProtocols(ssl3: false, tls10: false, tls11: false, tls12: true, tls13: true);
        var ciphers = MakeCiphers("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384");

        var findings = TlsAuditEngine.BuildFindings(protocols, ciphers);

        Assert.Single(findings);
        Assert.Equal(TlsFindingSeverity.Pass, findings[0].Severity);
    }

    [Fact]
    public void BuildFindings_WithLocalize_UsesLocalizedText()
    {
        var protocols = MakeProtocols(ssl3: true, tls10: false, tls11: false, tls12: true, tls13: true);
        var ciphers = new List<TlsCipherInfo>();

        var findings = TlsAuditEngine.BuildFindings(protocols, ciphers, key => $"[{key}]");

        Assert.All(findings, finding => Assert.StartsWith("[", finding.Message));
    }

    [Fact]
    public void BuildReportText_ContainsGradeAndHost()
    {
        var protocols = MakeProtocols(ssl3: false, tls10: false, tls11: false, tls12: true, tls13: true);
        var ciphers = MakeCiphers("TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384");
        var findings = TlsAuditEngine.BuildFindings(protocols, ciphers);
        var localize = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ToolTlsAuditReportTitle"] = "TLS report for {0}:{1}",
            ["ToolTlsAuditReportGrade"] = "Grade: {0}",
            ["ToolTlsAuditReportProtocols"] = "Protocols",
            ["ToolTlsAuditSupported"] = "Supported",
            ["ToolTlsAuditNotSupported"] = "Not supported",
            ["ToolTlsAuditReportCiphers"] = "Ciphers",
            ["ToolTlsAuditReportFindings"] = "Findings"
        };

        var report = TlsAuditEngine.BuildReportText(
            TlsGrade.APlus,
            protocols,
            ciphers,
            null,
            findings,
            "example.com",
            443,
            key => localize.TryGetValue(key, out var value) ? value : key);

        Assert.Contains("A+", report);
        Assert.Contains("example.com", report);
        Assert.Contains("443", report);
    }

    [Fact]
    public void GetPublicKeySize_RsaCert_ReturnsCorrectSize()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        var keySize = TlsAuditEngine.GetPublicKeySize(cert);

        Assert.Equal(2048, keySize);
    }

    [Fact]
    public void GetPublicKeySize_EcdsaCert_ReturnsCorrectSize()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=Test",
            ecdsa,
            HashAlgorithmName.SHA256);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        var keySize = TlsAuditEngine.GetPublicKeySize(cert);

        Assert.Equal(256, keySize);
    }

    [Theory]
    [InlineData(TlsGrade.APlus, "A+")]
    [InlineData(TlsGrade.A, "A")]
    [InlineData(TlsGrade.B, "B")]
    [InlineData(TlsGrade.F, "F")]
    public void GradeToString_ReturnsExpected(TlsGrade grade, string expected)
    {
        Assert.Equal(expected, TlsAuditEngine.GradeToString(grade));
    }

    private static List<TlsProtocolTestResult> MakeProtocols(
        bool ssl3,
        bool tls10,
        bool tls11,
        bool tls12,
        bool tls13)
    {
        return
        [
#pragma warning disable CS0618, SYSLIB0039
            new() { Name = "SSL 3.0", Supported = ssl3, Rating = TlsProtocolRating.Critical },
#pragma warning restore CS0618, SYSLIB0039
            new() { Name = "TLS 1.0", Supported = tls10, Rating = TlsProtocolRating.Weak },
            new() { Name = "TLS 1.1", Supported = tls11, Rating = TlsProtocolRating.Weak },
            new() { Name = "TLS 1.2", Supported = tls12, Rating = TlsProtocolRating.Strong },
            new() { Name = "TLS 1.3", Supported = tls13, Rating = TlsProtocolRating.Strong }
        ];
    }

    private static List<TlsCipherInfo> MakeCiphers(params string[] suiteNames)
    {
        return suiteNames.Select(name =>
        {
            var strength = CipherStrength.Acceptable;
            if (name.Contains("ECDHE", StringComparison.Ordinal) && name.Contains("GCM", StringComparison.Ordinal))
                strength = CipherStrength.Strong;
            else if (name.Contains("3DES", StringComparison.Ordinal) || name.Contains("RC4", StringComparison.Ordinal))
                strength = CipherStrength.Weak;

            var encryption = name.Contains("AES_256_GCM", StringComparison.Ordinal)
                ? "AES-256-GCM"
                : name.Contains("AES_128_GCM", StringComparison.Ordinal)
                    ? "AES-128-GCM"
                    : name.Contains("AES_256_CBC", StringComparison.Ordinal)
                        ? "AES-256-CBC"
                        : name.Contains("AES_128_CBC", StringComparison.Ordinal)
                            ? "AES-128-CBC"
                            : name.Contains("3DES", StringComparison.Ordinal)
                                ? "3DES-CBC"
                                : "RC4";

            return new TlsCipherInfo
            {
                SuiteName = name,
                Strength = strength,
                KeyExchange = name.Contains("ECDHE", StringComparison.Ordinal)
                    ? "ECDHE"
                    : name.Contains("DHE", StringComparison.Ordinal)
                        ? "DHE"
                        : "RSA",
                Authentication = name.Contains("ECDSA", StringComparison.Ordinal) ? "ECDSA" : "RSA",
                Encryption = encryption
            };
        }).ToList();
    }
}
