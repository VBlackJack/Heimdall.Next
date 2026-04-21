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

namespace Heimdall.Core.Hashing;

public static class HashComputer
{
    private const int StreamChunkSize = 81920;

    public static string Compute(HashAlgorithmKind kind, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!HashAlgorithmCatalog.IsSupported(kind))
        {
            throw new NotSupportedException($"Hash algorithm '{kind}' is not supported on this platform.");
        }

        var hash = kind switch
        {
            HashAlgorithmKind.Md5 => MD5.HashData(data),
            HashAlgorithmKind.Sha1 => SHA1.HashData(data),
            HashAlgorithmKind.Sha256 => SHA256.HashData(data),
            HashAlgorithmKind.Sha384 => SHA384.HashData(data),
            HashAlgorithmKind.Sha512 => SHA512.HashData(data),
            HashAlgorithmKind.Sha3_256 => SHA3_256.HashData(data),
            _ => throw new NotSupportedException($"Unknown hash algorithm '{kind}'."),
        };

        return Convert.ToHexStringLower(hash);
    }

    public static async Task<IReadOnlyDictionary<HashAlgorithmKind, string>> ComputeStreamMultiAsync(
        IReadOnlyList<HashAlgorithmKind> kinds,
        Stream stream,
        IProgress<long>? bytesProgress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(stream);

        var requestedKinds = kinds.Distinct().ToArray();
        foreach (var kind in requestedKinds)
        {
            if (!HashAlgorithmCatalog.IsSupported(kind))
            {
                throw new NotSupportedException($"Hash algorithm '{kind}' is not supported on this platform.");
            }
        }

        ct.ThrowIfCancellationRequested();

        var incrementalHashes = requestedKinds.ToDictionary(
            kind => kind,
            ToIncrementalHash);

        try
        {
            var buffer = new byte[StreamChunkSize];
            long totalBytesRead = 0;

            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                foreach (var incrementalHash in incrementalHashes.Values)
                {
                    incrementalHash.AppendData(buffer, 0, bytesRead);
                }

                totalBytesRead += bytesRead;
                bytesProgress?.Report(totalBytesRead);
            }

            if (totalBytesRead == 0)
            {
                bytesProgress?.Report(0);
            }

            var results = new Dictionary<HashAlgorithmKind, string>(requestedKinds.Length);
            foreach (var kind in requestedKinds)
            {
                results[kind] = Convert.ToHexStringLower(incrementalHashes[kind].GetHashAndReset());
            }

            return results;
        }
        finally
        {
            foreach (var incrementalHash in incrementalHashes.Values)
            {
                incrementalHash.Dispose();
            }
        }
    }

    private static IncrementalHash ToIncrementalHash(HashAlgorithmKind kind) =>
        IncrementalHash.CreateHash(ToHashAlgorithmName(kind));

    private static HashAlgorithmName ToHashAlgorithmName(HashAlgorithmKind kind) => kind switch
    {
        HashAlgorithmKind.Md5 => HashAlgorithmName.MD5,
        HashAlgorithmKind.Sha1 => HashAlgorithmName.SHA1,
        HashAlgorithmKind.Sha256 => HashAlgorithmName.SHA256,
        HashAlgorithmKind.Sha384 => HashAlgorithmName.SHA384,
        HashAlgorithmKind.Sha512 => HashAlgorithmName.SHA512,
        HashAlgorithmKind.Sha3_256 => HashAlgorithmName.SHA3_256,
        _ => throw new NotSupportedException($"Unknown hash algorithm '{kind}'."),
    };
}

public sealed class HashFileTooLargeException : Exception
{
    public HashFileTooLargeException(long actualSizeBytes, long limitBytes)
        : base($"File size {actualSizeBytes} exceeds maximum allowed size {limitBytes}.")
    {
        ActualSizeBytes = actualSizeBytes;
        LimitBytes = limitBytes;
    }

    public long ActualSizeBytes { get; }

    public long LimitBytes { get; }
}
