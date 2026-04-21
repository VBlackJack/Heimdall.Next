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

public sealed class HmacGeneratorServiceTests
{
    [Fact]
    public async Task ComputeAsync_ReturnsKnownSha256Vector()
    {
        var service = new HmacGeneratorService();
        var key = Enumerable.Repeat((byte)0x0b, 20).ToArray();
        var data = Encoding.ASCII.GetBytes("Hi There");

        var actual = await service.ComputeAsync(HashAlgorithmKind.Sha256, key, data, CancellationToken.None);

        Assert.Equal("b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7", HmacComputer.Format(actual, HmacOutputFormat.Hex));
    }

    [Fact]
    public async Task ComputeAsync_PreCancelled_Throws()
    {
        var service = new HmacGeneratorService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ComputeAsync(HashAlgorithmKind.Sha256, Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("message"), cts.Token));
    }

    [Fact]
    public async Task ComputeAsync_UnsupportedKind_Throws()
    {
        var service = new HmacGeneratorService();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.ComputeAsync(HashAlgorithmKind.Sha3_256, Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("message"), CancellationToken.None));
    }

    [Fact]
    public async Task ComputeAsync_NullKey_Throws()
    {
        var service = new HmacGeneratorService();
        byte[]? key = null;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.ComputeAsync(HashAlgorithmKind.Sha256, key!, Encoding.UTF8.GetBytes("message"), CancellationToken.None));
    }
}
