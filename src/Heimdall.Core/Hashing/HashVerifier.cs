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

namespace Heimdall.Core.Hashing;

public sealed record VerifyResult(bool Matched, HashAlgorithmKind? MatchedKind, bool DetectedByLength);

public static class HashVerifier
{
    public static HashAlgorithmKind? DetectByLength(int length) => length switch
    {
        32 => HashAlgorithmKind.Md5,
        40 => HashAlgorithmKind.Sha1,
        64 => HashAlgorithmKind.Sha256,
        96 => HashAlgorithmKind.Sha384,
        128 => HashAlgorithmKind.Sha512,
        _ => null,
    };

    public static VerifyResult FindMatch(
        IReadOnlyDictionary<HashAlgorithmKind, string> computed,
        string? candidate)
    {
        ArgumentNullException.ThrowIfNull(computed);

        var normalizedCandidate = (candidate ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedCandidate))
        {
            return new VerifyResult(false, null, false);
        }

        var detectedKind = DetectByLength(normalizedCandidate.Length);
        if (detectedKind is not null
            && computed.TryGetValue(detectedKind.Value, out var detectedHash)
            && string.Equals(detectedHash, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return new VerifyResult(true, detectedKind, true);
        }

        foreach (var pair in computed)
        {
            if (string.Equals(pair.Value, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                return new VerifyResult(true, pair.Key, false);
            }
        }

        return new VerifyResult(false, null, false);
    }
}
