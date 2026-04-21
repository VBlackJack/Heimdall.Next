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

using Heimdall.Core.Hashing;

namespace Heimdall.Core.Tests;

public sealed class HashComputerStreamTests
{
    [Fact]
    public async Task ComputeStreamMultiAsync_EmptyStream_ReturnsExpectedHashesForSupportedKinds()
    {
        await using var stream = new MemoryStream([]);

        var hashes = await HashComputer.ComputeStreamMultiAsync(HashAlgorithmCatalog.SupportedKinds, stream, null, CancellationToken.None);

        Assert.Equal(HashAlgorithmCatalog.SupportedKinds.Count, hashes.Count);
        foreach (var kind in HashAlgorithmCatalog.SupportedKinds)
        {
            Assert.Equal(HashComputer.Compute(kind, []), hashes[kind]);
        }
    }

    [Fact]
    public async Task ComputeStreamMultiAsync_MatchesSyncCompute_ForSameData()
    {
        var bytes = new byte[200_000];
        Random.Shared.NextBytes(bytes);
        await using var stream = new MemoryStream(bytes);

        var hashes = await HashComputer.ComputeStreamMultiAsync(HashAlgorithmCatalog.SupportedKinds, stream, null, CancellationToken.None);

        foreach (var kind in HashAlgorithmCatalog.SupportedKinds)
        {
            Assert.Equal(HashComputer.Compute(kind, bytes), hashes[kind]);
        }
    }

    [Fact]
    public async Task ComputeStreamMultiAsync_ReportsProgressMonotonically()
    {
        var bytes = new byte[50_000];
        Random.Shared.NextBytes(bytes);
        await using var stream = new MemoryStream(bytes);
        var progressValues = new List<long>();
        var progress = new SynchronousProgress<long>(value => progressValues.Add(value));

        await HashComputer.ComputeStreamMultiAsync(HashAlgorithmCatalog.SupportedKinds, stream, progress, CancellationToken.None);

        Assert.NotEmpty(progressValues);
        Assert.Equal(bytes.Length, progressValues[^1]);
        Assert.True(progressValues.SequenceEqual(progressValues.Order()));
    }

    [Fact]
    public async Task ComputeStreamMultiAsync_HonorsCancellation_Throws()
    {
        await using var stream = new MemoryStream([1, 2, 3]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HashComputer.ComputeStreamMultiAsync(HashAlgorithmCatalog.SupportedKinds, stream, null, cts.Token));
    }

    [Fact]
    public async Task ComputeStreamMultiAsync_UnsupportedKindInList_Throws()
    {
        await using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            HashComputer.ComputeStreamMultiAsync([(HashAlgorithmKind)999], stream, null, CancellationToken.None));
    }

    [Fact]
    public async Task ComputeStreamMultiAsync_SinglePassOverNonReplayableStream_ReturnsAllHashes()
    {
        var bytes = new byte[32_000];
        Random.Shared.NextBytes(bytes);
        await using var stream = new NonSeekableLimitedReadStream(bytes, 4096);

        var hashes = await HashComputer.ComputeStreamMultiAsync(HashAlgorithmCatalog.SupportedKinds, stream, null, CancellationToken.None);

        foreach (var kind in HashAlgorithmCatalog.SupportedKinds)
        {
            Assert.Equal(HashComputer.Compute(kind, bytes), hashes[kind]);
        }
    }

    [Fact]
    public async Task ComputeStreamMultiAsync_ChunkBoundariesDoNotAffectResult()
    {
        var bytes = new byte[250_000];
        Random.Shared.NextBytes(bytes);
        var expected = HashAlgorithmCatalog.SupportedKinds.ToDictionary(kind => kind, kind => HashComputer.Compute(kind, bytes));

        foreach (var chunkSize in new[] { 1, 100, 81920 })
        {
            await using var stream = new NonSeekableLimitedReadStream(bytes, chunkSize);
            var actual = await HashComputer.ComputeStreamMultiAsync(HashAlgorithmCatalog.SupportedKinds, stream, null, CancellationToken.None);
            Assert.Equal(expected, actual);
        }
    }

    private sealed class NonSeekableLimitedReadStream : Stream
    {
        private readonly byte[] _buffer;
        private readonly int _maxReadSize;
        private int _position;

        public NonSeekableLimitedReadStream(byte[] buffer, int maxReadSize)
        {
            _buffer = buffer;
            _maxReadSize = maxReadSize;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _buffer.Length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _buffer.Length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var bytesToRead = Math.Min(Math.Min(remaining, count), _maxReadSize);
            Array.Copy(_buffer, _position, buffer, offset, bytesToRead);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = _buffer.Length - _position;
            if (remaining <= 0)
            {
                return ValueTask.FromResult(0);
            }

            var bytesToRead = Math.Min(Math.Min(remaining, buffer.Length), _maxReadSize);
            _buffer.AsMemory(_position, bytesToRead).CopyTo(buffer);
            _position += bytesToRead;
            return ValueTask.FromResult(bytesToRead);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        private readonly Action<T> _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
