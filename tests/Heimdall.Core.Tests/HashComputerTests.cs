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
using Heimdall.Core.Hashing;

namespace Heimdall.Core.Tests;

public sealed class HashComputerTests
{
    public static TheoryData<HashAlgorithmKind, string> KnownVectors => new()
    {
        { HashAlgorithmKind.Md5, "900150983cd24fb0d6963f7d28e17f72" },
        { HashAlgorithmKind.Sha1, "a9993e364706816aba3e25717850c26c9cd0d89d" },
        { HashAlgorithmKind.Sha256, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad" },
        { HashAlgorithmKind.Sha384, "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7" },
        { HashAlgorithmKind.Sha512, "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f" },
        { HashAlgorithmKind.Sha3_256, "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532" },
    };

    [Theory]
    [MemberData(nameof(KnownVectors))]
    public void Compute_Abc_ReturnsKnownVector(HashAlgorithmKind kind, string expected)
    {
        if (!HashAlgorithmCatalog.IsSupported(kind))
        {
            return;
        }

        var actual = HashComputer.Compute(kind, Encoding.UTF8.GetBytes("abc"));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Compute_EmptyBuffer_ReturnsExpectedMd5()
    {
        var actual = HashComputer.Compute(HashAlgorithmKind.Md5, []);

        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", actual);
    }

    [Fact]
    public void Compute_NullData_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HashComputer.Compute(HashAlgorithmKind.Md5, null!));
    }

    [Fact]
    public void Compute_InvalidKind_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => HashComputer.Compute((HashAlgorithmKind)999, [1, 2, 3]));
    }

    [Fact]
    public void Compute_Sha3Unsupported_ThrowsNotSupported()
    {
        if (HashAlgorithmCatalog.IsSupported(HashAlgorithmKind.Sha3_256))
        {
            return;
        }

        Assert.Throws<NotSupportedException>(() => HashComputer.Compute(HashAlgorithmKind.Sha3_256, [1, 2, 3]));
    }
}
