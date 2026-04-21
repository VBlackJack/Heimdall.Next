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

public sealed class HmacAlgorithmCatalogTests
{
    [Fact]
    public void SupportedKinds_HasFiveEntries_InCanonicalOrder()
    {
        Assert.Equal(
        [
            HashAlgorithmKind.Sha256,
            HashAlgorithmKind.Sha384,
            HashAlgorithmKind.Sha512,
            HashAlgorithmKind.Sha1,
            HashAlgorithmKind.Md5,
        ], HmacAlgorithmCatalog.SupportedKinds);
    }

    [Fact]
    public void SupportedKinds_DoesNotContainSha3_256()
    {
        Assert.DoesNotContain(HashAlgorithmKind.Sha3_256, HmacAlgorithmCatalog.SupportedKinds);
    }

    [Theory]
    [InlineData(HashAlgorithmKind.Sha256)]
    [InlineData(HashAlgorithmKind.Sha384)]
    [InlineData(HashAlgorithmKind.Sha512)]
    [InlineData(HashAlgorithmKind.Sha1)]
    [InlineData(HashAlgorithmKind.Md5)]
    public void IsSupported_KnownHmacKinds_ReturnsTrue(HashAlgorithmKind kind)
    {
        Assert.True(HmacAlgorithmCatalog.IsSupported(kind));
    }

    [Fact]
    public void IsSupported_Sha3_256_ReturnsFalse()
    {
        Assert.False(HmacAlgorithmCatalog.IsSupported(HashAlgorithmKind.Sha3_256));
    }

    [Theory]
    [InlineData(HashAlgorithmKind.Md5, "HMAC-MD5")]
    [InlineData(HashAlgorithmKind.Sha1, "HMAC-SHA1")]
    [InlineData(HashAlgorithmKind.Sha256, "HMAC-SHA256")]
    [InlineData(HashAlgorithmKind.Sha384, "HMAC-SHA384")]
    [InlineData(HashAlgorithmKind.Sha512, "HMAC-SHA512")]
    public void DisplayName_ReturnsVerbatimLegacyValues(HashAlgorithmKind kind, string expected)
    {
        Assert.Equal(expected, HmacAlgorithmCatalog.DisplayName(kind));
    }

    [Fact]
    public void DisplayName_Sha3_256_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HmacAlgorithmCatalog.DisplayName(HashAlgorithmKind.Sha3_256));
    }
}
