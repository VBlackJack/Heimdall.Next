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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Heimdall.Core.Certificates;

public static class CertificateGenerator
{
    public const int Rsa2048KeySize = 2048;
    public const int Rsa4096KeySize = 4096;
    public const int CaValidityDays = 3650;
    public const int SerialNumberLength = 16;
    public const byte PositiveMsbMask = 0x7F;

    public static SelfSignedCertificateResult GenerateSelfSigned(CertificateOptions options, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var rsa = RSA.Create(options.KeySize);
        var subject = DistinguishedNameBuilder.Build(options.Cn, options.Org, options.Country);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [
                    new Oid("1.3.6.1.5.5.7.3.1"),
                    new Oid("1.3.6.1.5.5.7.3.2"),
                ],
                false));

        AddSans(request, options.Sans);

        var notBefore = AsUtcOffset(now);
        using var cert = request.CreateSelfSigned(notBefore, notBefore.AddDays(options.ValidityDays));

        return new SelfSignedCertificateResult(
            cert.ExportCertificatePem(),
            rsa.ExportPkcs8PrivateKeyPem(),
            CertificateFingerprint.ComputeSha256(cert),
            rsa.ExportPkcs8PrivateKey(),
            cert.RawData);
    }

    public static CaLeafCertificateResult GenerateCaLeafPair(CertificateOptions options, int caValidityDays, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(options);

        var anchor = AsUtcOffset(now);

        using var caRsa = RSA.Create(options.KeySize);
        var caSubject = DistinguishedNameBuilder.Build($"{options.Cn} CA", options.Org, options.Country);
        var caRequest = new CertificateRequest(caSubject, caRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true));

        using var caCert = caRequest.CreateSelfSigned(anchor, anchor.AddDays(caValidityDays));
        var caCertPem = caCert.ExportCertificatePem();
        var caKeyPem = caRsa.ExportPkcs8PrivateKeyPem();

        using var leafRsa = RSA.Create(options.KeySize);
        var leafSubject = DistinguishedNameBuilder.Build(options.Cn, options.Org, options.Country);
        var leafRequest = new CertificateRequest(leafSubject, leafRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));
        leafRequest.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [
                    new Oid("1.3.6.1.5.5.7.3.1"),
                    new Oid("1.3.6.1.5.5.7.3.2"),
                ],
                false));

        AddSans(leafRequest, options.Sans);

        using var leafCert = leafRequest.Create(
            caCert,
            anchor,
            anchor.AddDays(options.ValidityDays),
            NewPositiveSerial());

        var leafCertPem = leafCert.ExportCertificatePem();
        var leafKeyPem = leafRsa.ExportPkcs8PrivateKeyPem();
        var fingerprint = CertificateFingerprint.ComputeSha256(leafCert);

        using var leafWithKey = leafCert.CopyWithPrivateKey(leafRsa);
        var pfxBytes = leafWithKey.Export(X509ContentType.Pfx, string.Empty);

        return new CaLeafCertificateResult(
            caCertPem,
            caKeyPem,
            leafCertPem,
            leafKeyPem,
            fingerprint,
            pfxBytes);
    }

    public static byte[] BuildPfx(SelfSignedCertificateResult result, string password)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(result.PrivateKeyPkcs8, out _);
        using var cert = X509CertificateLoader.LoadCertificate(result.CertRawData);
        using var combined = cert.CopyWithPrivateKey(rsa);
        return combined.Export(X509ContentType.Pfx, password);
    }

    public static byte[] BuildPfx(CaLeafCertificateResult result, string password)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var pfx = X509CertificateLoader.LoadPkcs12(
            result.PfxBytes,
            string.Empty,
            X509KeyStorageFlags.Exportable);
        return pfx.Export(X509ContentType.Pfx, password);
    }

    private static void AddSans(CertificateRequest request, IReadOnlyList<string> sans)
    {
        if (sans.Count == 0)
        {
            return;
        }

        var sanBuilder = new SubjectAlternativeNameBuilder();
        foreach (var san in sans)
        {
            if (IPAddress.TryParse(san, out var ip))
            {
                sanBuilder.AddIpAddress(ip);
            }
            else
            {
                sanBuilder.AddDnsName(san);
            }
        }

        request.CertificateExtensions.Add(sanBuilder.Build());
    }

    private static byte[] NewPositiveSerial()
    {
        var serial = new byte[SerialNumberLength];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= PositiveMsbMask;
        return serial;
    }

    private static DateTimeOffset AsUtcOffset(DateTime now)
    {
        var normalized = now.Kind switch
        {
            DateTimeKind.Utc => now,
            DateTimeKind.Local => now.ToUniversalTime(),
            _ => DateTime.SpecifyKind(now, DateTimeKind.Utc),
        };

        return new DateTimeOffset(normalized, TimeSpan.Zero);
    }
}
