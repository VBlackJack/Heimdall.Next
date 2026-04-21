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
using System.Text;
using Heimdall.Core.Hashing;

namespace Heimdall.App.Services;

public sealed class HashGeneratorService : IHashGeneratorService
{
    public const long MaxFileSizeBytes = 50L * 1024 * 1024;

    public async Task<IReadOnlyDictionary<HashAlgorithmKind, string>> ComputeTextHashesAsync(
        string text,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = Encoding.UTF8.GetBytes(text);
        var supportedKinds = HashAlgorithmCatalog.SupportedKinds;

        return await Task.Run<IReadOnlyDictionary<HashAlgorithmKind, string>>(() =>
        {
            ct.ThrowIfCancellationRequested();

            var hashes = new Dictionary<HashAlgorithmKind, string>(supportedKinds.Count);
            foreach (var kind in supportedKinds)
            {
                ct.ThrowIfCancellationRequested();
                hashes[kind] = HashComputer.Compute(kind, bytes);
            }

            return hashes;
        }, ct).ConfigureAwait(false);
    }

    public async Task<HashFileResult> ComputeFileHashesAsync(
        string filePath,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ct.ThrowIfCancellationRequested();

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("File not found.", filePath);
        }

        if (fileInfo.Length > MaxFileSizeBytes)
        {
            throw new HashFileTooLargeException(fileInfo.Length, MaxFileSizeBytes);
        }

        await using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        IProgress<long>? byteProgress = progress is null
            ? null
            : new SynchronousProgress<long>(bytesProcessed =>
            {
                var percent = fileInfo.Length == 0
                    ? 100d
                    : Math.Min(100d, (double)bytesProcessed / fileInfo.Length * 100d);
                progress.Report(percent);
            });

        var hashes = await HashComputer.ComputeStreamMultiAsync(
            HashAlgorithmCatalog.SupportedKinds,
            fileStream,
            byteProgress,
            ct).ConfigureAwait(false);

        return new HashFileResult(hashes, fileInfo.Length);
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        private readonly Action<T> _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
