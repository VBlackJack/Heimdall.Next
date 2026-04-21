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
using Heimdall.App.Services;
using Heimdall.Core.Certificates;

namespace Heimdall.App.Tests;

public sealed class CertificateGeneratorServiceTests
{
    [Fact]
    public async Task GenerateSelfSignedAsync_ReturnsResult()
    {
        var service = new CertificateGeneratorService();

        var result = await service.GenerateSelfSignedAsync(CreateOptions());

        Assert.NotEmpty(result.CertPem);
        Assert.NotEmpty(result.KeyPem);
    }

    [Fact]
    public async Task GenerateCaLeafPairAsync_ReturnsResult()
    {
        var service = new CertificateGeneratorService();

        var result = await service.GenerateCaLeafPairAsync(CreateOptions(), CertificateGenerator.CaValidityDays);

        Assert.NotEmpty(result.CaCertPem);
        Assert.NotEmpty(result.LeafCertPem);
        Assert.NotEmpty(result.PfxBytes);
    }

    [Fact]
    public async Task BuildPfx_SelfSigned_ReturnsReloadablePkcs12()
    {
        var service = new CertificateGeneratorService();
        var result = await service.GenerateSelfSignedAsync(CreateOptions());

        var pfxBytes = service.BuildPfx(result, "secret");
        using var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, "secret", X509KeyStorageFlags.Exportable);

        Assert.Contains("CN=server.local", cert.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildPfx_CaLeaf_ReturnsReloadablePkcs12()
    {
        var service = new CertificateGeneratorService();
        var result = await service.GenerateCaLeafPairAsync(CreateOptions(), CertificateGenerator.CaValidityDays);

        var pfxBytes = service.BuildPfx(result, "secret");
        using var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, "secret", X509KeyStorageFlags.Exportable);

        Assert.Contains("CN=server.local", cert.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateSelfSignedAsync_HonoursCancellation()
    {
        var service = new CertificateGeneratorService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GenerateSelfSignedAsync(CreateOptions(), cts.Token));
    }

    private static CertificateOptions CreateOptions() =>
        new("server.local", "Heimdall", "FR", CertificateGenerator.Rsa2048KeySize, 365, []);
}
