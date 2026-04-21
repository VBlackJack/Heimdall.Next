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
using System.Security.Cryptography.X509Certificates;
using Heimdall.Core.Certificates;

namespace Heimdall.Core.Tests;

public sealed class CertificateGeneratorTests
{
    private static readonly DateTime FixedNow = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public void GenerateSelfSigned_Rsa2048_ReturnsValidPem()
    {
        var result = GenerateSelfSigned(CertificateGenerator.Rsa2048KeySize);

        Assert.Contains("BEGIN CERTIFICATE", result.CertPem, StringComparison.Ordinal);
        Assert.Contains("BEGIN PRIVATE KEY", result.KeyPem, StringComparison.Ordinal);
        Assert.NotEmpty(result.CertRawData);
        Assert.NotEmpty(result.PrivateKeyPkcs8);
    }

    [Fact]
    public void GenerateSelfSigned_Rsa4096_HasCorrectKeySize()
    {
        var result = GenerateSelfSigned(CertificateGenerator.Rsa4096KeySize);
        using var cert = X509Certificate2.CreateFromPem(result.CertPem, result.KeyPem);
        using var rsa = cert.GetRSAPrivateKey();

        Assert.NotNull(rsa);
        Assert.Equal(CertificateGenerator.Rsa4096KeySize, rsa.KeySize);
    }

    [Fact]
    public void GenerateSelfSigned_WithDnsSan_IncludesSanExtension()
    {
        var result = CertificateGenerator.GenerateSelfSigned(CreateOptions(["server.local"]), FixedNow);
        using var cert = X509Certificate2.CreateFromPem(result.CertPem, result.KeyPem);

        var san = cert.Extensions["2.5.29.17"];

        Assert.NotNull(san);
        Assert.Contains("server.local", san!.Format(true), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateSelfSigned_WithIpSan_IncludesIpAddress()
    {
        var result = CertificateGenerator.GenerateSelfSigned(CreateOptions(["10.0.0.1"]), FixedNow);
        using var cert = X509Certificate2.CreateFromPem(result.CertPem, result.KeyPem);

        var san = cert.Extensions["2.5.29.17"];

        Assert.NotNull(san);
        Assert.Contains("10.0.0.1", san!.Format(true), StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateSelfSigned_WithMixedSans_HandlesBoth()
    {
        var result = CertificateGenerator.GenerateSelfSigned(CreateOptions(["server.local", "10.0.0.1"]), FixedNow);
        using var cert = X509Certificate2.CreateFromPem(result.CertPem, result.KeyPem);

        var san = cert.Extensions["2.5.29.17"];

        Assert.NotNull(san);
        Assert.Contains("server.local", san!.Format(true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10.0.0.1", san.Format(true), StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateSelfSigned_WithoutSans_OmitsSanExtension()
    {
        var result = CertificateGenerator.GenerateSelfSigned(CreateOptions([]), FixedNow);
        using var cert = X509Certificate2.CreateFromPem(result.CertPem, result.KeyPem);

        Assert.Null(cert.Extensions["2.5.29.17"]);
    }

    [Fact]
    public void GenerateSelfSigned_FingerprintIsValidSha256Format()
    {
        var result = GenerateSelfSigned(CertificateGenerator.Rsa2048KeySize);

        Assert.StartsWith("SHA256:", result.Fingerprint, StringComparison.Ordinal);
        Assert.True(result.Fingerprint.Contains(':', StringComparison.Ordinal));
    }

    [Fact]
    public void GenerateSelfSigned_RespectsValidityDates()
    {
        var result = CertificateGenerator.GenerateSelfSigned(CreateOptions([]) with { ValidityDays = 30 }, FixedNow);
        using var cert = X509Certificate2.CreateFromPem(result.CertPem, result.KeyPem);

        Assert.Equal(FixedNow, cert.NotBefore.ToUniversalTime());
        Assert.Equal(FixedNow.AddDays(30), cert.NotAfter.ToUniversalTime());
    }

    [Fact]
    public void GenerateCaLeafPair_CaIsSelfSigned()
    {
        var result = GenerateCaLeafPair();
        using var ca = X509Certificate2.CreateFromPem(result.CaCertPem, result.CaKeyPem);

        Assert.Equal(ca.Subject, ca.Issuer);
        Assert.True(ca.Subject.Contains("CN=server.local CA", StringComparison.Ordinal));
    }

    [Fact]
    public void GenerateCaLeafPair_LeafSignedByCa()
    {
        var result = GenerateCaLeafPair();
        using var ca = X509Certificate2.CreateFromPem(result.CaCertPem, result.CaKeyPem);
        using var leaf = X509Certificate2.CreateFromPem(result.LeafCertPem, result.LeafKeyPem);

        Assert.Equal(ca.Subject, leaf.Issuer);
    }

    [Fact]
    public void GenerateCaLeafPair_CaHasBasicConstraintsCa()
    {
        var result = GenerateCaLeafPair();
        using var ca = X509Certificate2.CreateFromPem(result.CaCertPem, result.CaKeyPem);

        var constraints = ca.Extensions.OfType<X509BasicConstraintsExtension>().Single();

        Assert.True(constraints.CertificateAuthority);
    }

    [Fact]
    public void GenerateCaLeafPair_LeafHasNonCaBasicConstraints()
    {
        var result = GenerateCaLeafPair();
        using var leaf = X509Certificate2.CreateFromPem(result.LeafCertPem, result.LeafKeyPem);

        var constraints = leaf.Extensions.OfType<X509BasicConstraintsExtension>().Single();

        Assert.False(constraints.CertificateAuthority);
    }

    [Fact]
    public void GenerateCaLeafPair_FingerprintMatchesLeafNotCa()
    {
        var result = GenerateCaLeafPair();
        using var ca = X509Certificate2.CreateFromPem(result.CaCertPem, result.CaKeyPem);
        using var leaf = X509Certificate2.CreateFromPem(result.LeafCertPem, result.LeafKeyPem);

        Assert.Equal(CertificateFingerprint.ComputeSha256(leaf), result.Fingerprint);
        Assert.NotEqual(CertificateFingerprint.ComputeSha256(ca), result.Fingerprint);
    }

    [Fact]
    public void BuildPfx_SelfSigned_ReturnsValidPkcs12()
    {
        var result = GenerateSelfSigned(CertificateGenerator.Rsa2048KeySize);

        var pfxBytes = CertificateGenerator.BuildPfx(result, "secret");
        using var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, "secret", X509KeyStorageFlags.Exportable);

        Assert.True(cert.Subject.Contains("CN=server.local", StringComparison.Ordinal));
        Assert.NotNull(cert.GetRSAPrivateKey());
    }

    [Fact]
    public void BuildPfx_CaLeaf_ReturnsValidPkcs12WithLeaf()
    {
        var result = GenerateCaLeafPair();

        var pfxBytes = CertificateGenerator.BuildPfx(result, "secret");
        using var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, "secret", X509KeyStorageFlags.Exportable);

        Assert.True(cert.Subject.Contains("CN=server.local", StringComparison.Ordinal));
        Assert.False(cert.Subject.Contains(" CA", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPfx_WithEmptyPassword_Succeeds()
    {
        var result = GenerateCaLeafPair();

        var pfxBytes = CertificateGenerator.BuildPfx(result, string.Empty);
        using var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, string.Empty, X509KeyStorageFlags.Exportable);

        Assert.True(cert.Subject.Contains("CN=server.local", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPfx_WithPassword_RequiresPasswordToReload()
    {
        var result = GenerateSelfSigned(CertificateGenerator.Rsa2048KeySize);
        var pfxBytes = CertificateGenerator.BuildPfx(result, "secret");

        Assert.ThrowsAny<CryptographicException>(() =>
            X509CertificateLoader.LoadPkcs12(pfxBytes, string.Empty, X509KeyStorageFlags.Exportable));
    }

    private static CertificateOptions CreateOptions(IReadOnlyList<string> sans) =>
        new("server.local", "Heimdall", "FR", CertificateGenerator.Rsa2048KeySize, 365, sans);

    private static SelfSignedCertificateResult GenerateSelfSigned(int keySize) =>
        CertificateGenerator.GenerateSelfSigned(
            new CertificateOptions("server.local", "Heimdall", "FR", keySize, 365, []),
            FixedNow);

    private static CaLeafCertificateResult GenerateCaLeafPair() =>
        CertificateGenerator.GenerateCaLeafPair(CreateOptions([]), CertificateGenerator.CaValidityDays, FixedNow);
}
