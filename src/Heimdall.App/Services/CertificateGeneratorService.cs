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

using Heimdall.Core.Certificates;

namespace Heimdall.App.Services;

public sealed class CertificateGeneratorService : ICertificateGeneratorService
{
    public Task<SelfSignedCertificateResult> GenerateSelfSignedAsync(
        CertificateOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Task.Run(() => CertificateGenerator.GenerateSelfSigned(options, DateTime.UtcNow), cancellationToken);
    }

    public Task<CaLeafCertificateResult> GenerateCaLeafPairAsync(
        CertificateOptions options,
        int caValidityDays,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Task.Run(() => CertificateGenerator.GenerateCaLeafPair(options, caValidityDays, DateTime.UtcNow), cancellationToken);
    }

    public byte[] BuildPfx(SelfSignedCertificateResult result, string password) =>
        CertificateGenerator.BuildPfx(result, password);

    public byte[] BuildPfx(CaLeafCertificateResult result, string password) =>
        CertificateGenerator.BuildPfx(result, password);
}
