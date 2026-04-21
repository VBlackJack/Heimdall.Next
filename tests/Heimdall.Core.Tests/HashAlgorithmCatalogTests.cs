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

public sealed class HashAlgorithmCatalogTests
{
    [Fact]
    public void AllKinds_HasSixEntries_InCanonicalOrder()
    {
        HashAlgorithmKind[] expected =
        [
            HashAlgorithmKind.Md5,
            HashAlgorithmKind.Sha1,
            HashAlgorithmKind.Sha256,
            HashAlgorithmKind.Sha384,
            HashAlgorithmKind.Sha512,
            HashAlgorithmKind.Sha3_256,
        ];

        Assert.Equal(expected, HashAlgorithmCatalog.AllKinds);
    }

    [Fact]
    public void SupportedKinds_IsSubsetOfAllKinds()
    {
        Assert.All(HashAlgorithmCatalog.SupportedKinds, kind => Assert.Contains(kind, HashAlgorithmCatalog.AllKinds));
    }

    [Theory]
    [InlineData(HashAlgorithmKind.Md5, "MD5")]
    [InlineData(HashAlgorithmKind.Sha1, "SHA1")]
    [InlineData(HashAlgorithmKind.Sha256, "SHA256")]
    [InlineData(HashAlgorithmKind.Sha384, "SHA384")]
    [InlineData(HashAlgorithmKind.Sha512, "SHA512")]
    [InlineData(HashAlgorithmKind.Sha3_256, "SHA3-256")]
    public void DisplayName_ReturnsExpectedName(HashAlgorithmKind kind, string expected)
    {
        Assert.Equal(expected, HashAlgorithmCatalog.DisplayName(kind));
    }

    [Theory]
    [InlineData(HashAlgorithmKind.Md5, 32)]
    [InlineData(HashAlgorithmKind.Sha1, 40)]
    [InlineData(HashAlgorithmKind.Sha256, 64)]
    [InlineData(HashAlgorithmKind.Sha384, 96)]
    [InlineData(HashAlgorithmKind.Sha512, 128)]
    [InlineData(HashAlgorithmKind.Sha3_256, 64)]
    public void HexLength_ReturnsExpectedValue(HashAlgorithmKind kind, int expected)
    {
        Assert.Equal(expected, HashAlgorithmCatalog.HexLength(kind));
    }

    [Fact]
    public void IsSupported_InvalidEnum_ReturnsFalse()
    {
        Assert.False(HashAlgorithmCatalog.IsSupported((HashAlgorithmKind)999));
    }

    [Fact]
    public void SupportedKinds_MatchIsSupportedPredicate()
    {
        var expected = HashAlgorithmCatalog.AllKinds.Where(HashAlgorithmCatalog.IsSupported).ToArray();

        Assert.Equal(expected, HashAlgorithmCatalog.SupportedKinds);
    }
}
