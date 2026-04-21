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

using System.Text;
using Heimdall.App.Services;
using Heimdall.Core.Hashing;

namespace Heimdall.App.Tests;

public sealed class OtpGeneratorServiceTests
{
    private static readonly byte[] Sha1Seed = Encoding.ASCII.GetBytes("12345678901234567890");

    [Fact]
    public async Task GenerateAsync_ValidInput_ReturnsExpectedCode()
    {
        var service = new OtpGeneratorService();

        var actual = await service.GenerateAsync(Sha1Seed, 59L, HashAlgorithmKind.Sha1, 6, 30, CancellationToken.None);

        Assert.Equal("287082", actual);
    }

    [Fact]
    public async Task GenerateAsync_NullSecret_Throws()
    {
        var service = new OtpGeneratorService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.GenerateAsync(null!, 59L, HashAlgorithmKind.Sha1, 6, 30, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_InvalidAlgorithm_Throws()
    {
        var service = new OtpGeneratorService();

        await Assert.ThrowsAsync<NotSupportedException>(() => service.GenerateAsync(Sha1Seed, 59L, HashAlgorithmKind.Md5, 6, 30, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_PreCancelled_Throws()
    {
        var service = new OtpGeneratorService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GenerateAsync(Sha1Seed, 59L, HashAlgorithmKind.Sha1, 6, 30, cts.Token));
    }
}
