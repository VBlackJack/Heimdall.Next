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

using System.IO;
using Heimdall.App.Services;
using Heimdall.Core.Hashing;

namespace Heimdall.App.Tests;

public sealed class HashGeneratorServiceTests
{
    [Fact]
    public async Task ComputeTextHashesAsync_ReturnsSupportedKindsInCanonicalOrder()
    {
        var service = new HashGeneratorService();

        var hashes = await service.ComputeTextHashesAsync("abc", CancellationToken.None);

        Assert.Equal(HashAlgorithmCatalog.SupportedKinds, hashes.Keys);
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", hashes[HashAlgorithmKind.Md5]);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hashes[HashAlgorithmKind.Sha256]);
    }

    [Fact]
    public async Task ComputeTextHashesAsync_PreCancelled_Throws()
    {
        var service = new HashGeneratorService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ComputeTextHashesAsync("abc", cts.Token));
    }

    [Fact]
    public async Task ComputeFileHashesAsync_ReturnsHashesAndSize()
    {
        var service = new HashGeneratorService();
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "abc");
        var progress = new CollectingProgress<double>();

        try
        {
            var result = await service.ComputeFileHashesAsync(path, progress, CancellationToken.None);

            Assert.Equal(3, result.FileSizeBytes);
            Assert.Equal("900150983cd24fb0d6963f7d28e17f72", result.Hashes[HashAlgorithmKind.Md5]);
            Assert.NotEmpty(progress.Values);
            Assert.Equal(100d, progress.Values[^1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ComputeFileHashesAsync_TooLarge_Throws()
    {
        var service = new HashGeneratorService();
        var path = Path.GetTempFileName();

        try
        {
            await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(HashGeneratorService.MaxFileSizeBytes + 1);
            }

            var ex = await Assert.ThrowsAsync<HashFileTooLargeException>(() =>
                service.ComputeFileHashesAsync(path, null, CancellationToken.None));

            Assert.Equal(HashGeneratorService.MaxFileSizeBytes, ex.LimitBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ComputeFileHashesAsync_FileNotFound_Throws()
    {
        var service = new HashGeneratorService();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.ComputeFileHashesAsync(path, null, CancellationToken.None));
    }

    [Fact]
    public async Task ComputeFileHashesAsync_EmptyFile_ReportsHundredPercent()
    {
        var service = new HashGeneratorService();
        var path = Path.GetTempFileName();
        var progress = new CollectingProgress<double>();

        try
        {
            var result = await service.ComputeFileHashesAsync(path, progress, CancellationToken.None);

            Assert.Equal(0, result.FileSizeBytes);
            Assert.Contains(100d, progress.Values);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value)
        {
            Values.Add(value);
        }
    }
}
