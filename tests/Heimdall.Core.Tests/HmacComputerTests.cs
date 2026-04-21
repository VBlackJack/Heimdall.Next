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

public sealed class HmacComputerTests
{
    [Fact]
    public void Compute_Md5_TestVector1_MatchesKnownValue()
    {
        var key = Enumerable.Repeat((byte)0x0b, 16).ToArray();
        var data = Encoding.ASCII.GetBytes("Hi There");

        var actual = HmacComputer.Compute(HashAlgorithmKind.Md5, key, data);

        Assert.Equal("9294727a3638bb1c13f48ef8158bfc9d", HmacComputer.Format(actual, HmacOutputFormat.Hex));
    }

    [Fact]
    public void Compute_Sha1_TestVector1_MatchesKnownValue()
    {
        var key = Enumerable.Repeat((byte)0x0b, 20).ToArray();
        var data = Encoding.ASCII.GetBytes("Hi There");

        var actual = HmacComputer.Compute(HashAlgorithmKind.Sha1, key, data);

        Assert.Equal("b617318655057264e28bc0b6fb378c8ef146be00", HmacComputer.Format(actual, HmacOutputFormat.Hex));
    }

    [Fact]
    public void Compute_Sha256_TestVector1_MatchesKnownValue()
    {
        var key = Enumerable.Repeat((byte)0x0b, 20).ToArray();
        var data = Encoding.ASCII.GetBytes("Hi There");

        var actual = HmacComputer.Compute(HashAlgorithmKind.Sha256, key, data);

        Assert.Equal("b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7", HmacComputer.Format(actual, HmacOutputFormat.Hex));
    }

    [Fact]
    public void Compute_Sha384_TestVector1_MatchesKnownValue()
    {
        var key = Enumerable.Repeat((byte)0x0b, 20).ToArray();
        var data = Encoding.ASCII.GetBytes("Hi There");

        var actual = HmacComputer.Compute(HashAlgorithmKind.Sha384, key, data);

        Assert.Equal("afd03944d84895626b0825f4ab46907f15f9dadbe4101ec682aa034c7cebc59cfaea9ea9076ede7f4af152e8b2fa9cb6", HmacComputer.Format(actual, HmacOutputFormat.Hex));
    }

    [Fact]
    public void Compute_Sha512_TestVector1_MatchesKnownValue()
    {
        var key = Enumerable.Repeat((byte)0x0b, 20).ToArray();
        var data = Encoding.ASCII.GetBytes("Hi There");

        var actual = HmacComputer.Compute(HashAlgorithmKind.Sha512, key, data);

        Assert.Equal("87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cdedaa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854", HmacComputer.Format(actual, HmacOutputFormat.Hex));
    }

    [Theory]
    [InlineData(HashAlgorithmKind.Md5, 16)]
    [InlineData(HashAlgorithmKind.Sha1, 20)]
    [InlineData(HashAlgorithmKind.Sha256, 32)]
    [InlineData(HashAlgorithmKind.Sha384, 48)]
    [InlineData(HashAlgorithmKind.Sha512, 64)]
    public void Compute_ReturnsExpectedByteLength(HashAlgorithmKind kind, int expectedLength)
    {
        var key = Encoding.UTF8.GetBytes("key");
        var data = Encoding.UTF8.GetBytes("message");

        var actual = HmacComputer.Compute(kind, key, data);

        Assert.Equal(expectedLength, actual.Length);
    }

    [Fact]
    public void Compute_Sha3_256_ThrowsNotSupported()
    {
        var key = Encoding.UTF8.GetBytes("key");
        var data = Encoding.UTF8.GetBytes("message");

        Assert.Throws<NotSupportedException>(() => HmacComputer.Compute(HashAlgorithmKind.Sha3_256, key, data));
    }

    [Fact]
    public void Compute_NullKey_Throws()
    {
        byte[]? key = null;
        var data = Encoding.UTF8.GetBytes("message");

        Assert.Throws<ArgumentNullException>(() => HmacComputer.Compute(HashAlgorithmKind.Sha256, key!, data));
    }

    [Fact]
    public void Compute_NullData_Throws()
    {
        var key = Encoding.UTF8.GetBytes("key");
        byte[]? data = null;

        Assert.Throws<ArgumentNullException>(() => HmacComputer.Compute(HashAlgorithmKind.Sha256, key, data!));
    }

    [Fact]
    public void Format_Hex_ReturnsLowercase()
    {
        var bytes = new byte[] { 0xAB, 0xCD, 0xEF };

        var actual = HmacComputer.Format(bytes, HmacOutputFormat.Hex);

        Assert.Equal("abcdef", actual);
    }

    [Fact]
    public void Format_Base64_ReturnsBuiltInValue()
    {
        var bytes = new byte[] { 0x12, 0x34, 0x56, 0x78 };

        var actual = HmacComputer.Format(bytes, HmacOutputFormat.Base64);

        Assert.Equal(Convert.ToBase64String(bytes), actual);
    }

    [Fact]
    public void Format_UnknownEnumValue_Throws()
    {
        var bytes = new byte[] { 0x01 };

        Assert.Throws<ArgumentOutOfRangeException>(() => HmacComputer.Format(bytes, (HmacOutputFormat)999));
    }

    [Fact]
    public void Format_NullBytes_Throws()
    {
        byte[]? bytes = null;

        Assert.Throws<ArgumentNullException>(() => HmacComputer.Format(bytes!, HmacOutputFormat.Hex));
    }
}
