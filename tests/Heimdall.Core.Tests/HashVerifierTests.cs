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

public sealed class HashVerifierTests
{
    [Theory]
    [InlineData(32, HashAlgorithmKind.Md5)]
    [InlineData(40, HashAlgorithmKind.Sha1)]
    [InlineData(64, HashAlgorithmKind.Sha256)]
    [InlineData(96, HashAlgorithmKind.Sha384)]
    [InlineData(128, HashAlgorithmKind.Sha512)]
    public void DetectByLength_ReturnsExpectedKind(int length, HashAlgorithmKind expected)
    {
        Assert.Equal(expected, HashVerifier.DetectByLength(length));
    }

    [Fact]
    public void DetectByLength_UnknownLength_ReturnsNull()
    {
        Assert.Null(HashVerifier.DetectByLength(12));
    }

    [Fact]
    public void FindMatch_DetectedByLength_ReturnsMatched()
    {
        var hashes = new Dictionary<HashAlgorithmKind, string>
        {
            [HashAlgorithmKind.Md5] = "900150983cd24fb0d6963f7d28e17f72",
        };

        var result = HashVerifier.FindMatch(hashes, "900150983cd24fb0d6963f7d28e17f72");

        Assert.True(result.Matched);
        Assert.Equal(HashAlgorithmKind.Md5, result.MatchedKind);
        Assert.True(result.DetectedByLength);
    }

    [Fact]
    public void FindMatch_FallbackAnyMatch_ReturnsMatched()
    {
        var hashes = new Dictionary<HashAlgorithmKind, string>
        {
            [HashAlgorithmKind.Sha3_256] = "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532",
        };

        var result = HashVerifier.FindMatch(hashes, "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532");

        Assert.True(result.Matched);
        Assert.Equal(HashAlgorithmKind.Sha3_256, result.MatchedKind);
        Assert.False(result.DetectedByLength);
    }

    [Fact]
    public void FindMatch_NoMatch_ReturnsFalse()
    {
        var hashes = new Dictionary<HashAlgorithmKind, string>
        {
            [HashAlgorithmKind.Sha256] = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
        };

        var result = HashVerifier.FindMatch(hashes, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.False(result.Matched);
        Assert.Null(result.MatchedKind);
    }

    [Fact]
    public void FindMatch_EmptyCandidate_ReturnsFalse()
    {
        var result = HashVerifier.FindMatch(new Dictionary<HashAlgorithmKind, string>(), string.Empty);

        Assert.False(result.Matched);
    }

    [Fact]
    public void FindMatch_TrimsAndIgnoresCase()
    {
        var hashes = new Dictionary<HashAlgorithmKind, string>
        {
            [HashAlgorithmKind.Sha1] = "a9993e364706816aba3e25717850c26c9cd0d89d",
        };

        var result = HashVerifier.FindMatch(hashes, "  A9993E364706816ABA3E25717850C26C9CD0D89D  ");

        Assert.True(result.Matched);
        Assert.Equal(HashAlgorithmKind.Sha1, result.MatchedKind);
    }

    [Fact]
    public void FindMatch_NullDictionary_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HashVerifier.FindMatch(null!, "abc"));
    }
}
