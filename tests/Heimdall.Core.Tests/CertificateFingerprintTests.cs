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

using System.Security.Cryptography.X509Certificates;
using Heimdall.Core.Certificates;

namespace Heimdall.Core.Tests;

public sealed class CertificateFingerprintTests
{
    [Fact]
    public void ComputeSha256_StartsWithSha256Prefix()
    {
        using var cert = LoadSelfSignedCertificate();

        var fingerprint = CertificateFingerprint.ComputeSha256(cert);

        Assert.StartsWith("SHA256:", fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeSha256_UsesColonSeparators()
    {
        using var cert = LoadSelfSignedCertificate();

        var fingerprint = CertificateFingerprint.ComputeSha256(cert);

        Assert.True(fingerprint.Contains(':', StringComparison.Ordinal));
        Assert.False(fingerprint.Contains('-', StringComparison.Ordinal));
    }

    [Fact]
    public void ComputeSha256_IsStableForSameCert()
    {
        using var cert = LoadSelfSignedCertificate();

        var first = CertificateFingerprint.ComputeSha256(cert);
        var second = CertificateFingerprint.ComputeSha256(cert);

        Assert.Equal(first, second);
    }

    private static X509Certificate2 LoadSelfSignedCertificate()
    {
        var result = CertificateGenerator.GenerateSelfSigned(
            new CertificateOptions("server.local", string.Empty, string.Empty, CertificateGenerator.Rsa2048KeySize, 365, []),
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        return X509CertificateLoader.LoadCertificate(result.CertRawData);
    }
}
