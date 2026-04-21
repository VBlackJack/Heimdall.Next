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
using Heimdall.Core.Otp;

namespace Heimdall.Core.Tests;

public sealed class TotpGeneratorTests
{
    private static readonly byte[] Sha1Seed = Encoding.ASCII.GetBytes("12345678901234567890");
    private static readonly byte[] Sha256Seed = Encoding.ASCII.GetBytes("12345678901234567890123456789012");
    private static readonly byte[] Sha512Seed = Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Generate_Rfc6238Sha1Vectors_ReturnExpectedCode(long unixSeconds, string expected)
    {
        var actual = TotpGenerator.Generate(Sha1Seed, unixSeconds);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Generate_NullSecret_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TotpGenerator.Generate(null!, 59L));
    }

    [Fact]
    public void Generate_DigitsBelowMin_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TotpGenerator.Generate(Sha1Seed, 59L, digits: 0));
    }

    [Fact]
    public void Generate_DigitsAboveMax_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TotpGenerator.Generate(Sha1Seed, 59L, digits: 10));
    }

    [Fact]
    public void Generate_TimeStepZero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TotpGenerator.Generate(Sha1Seed, 59L, timeStepSeconds: 0));
    }

    [Fact]
    public void Generate_TimeStepNegative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TotpGenerator.Generate(Sha1Seed, 59L, timeStepSeconds: -30));
    }

    [Fact]
    public void Generate_UnsupportedAlgorithm_Md5_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => TotpGenerator.Generate(Sha1Seed, 59L, HashAlgorithmKind.Md5));
    }

    [Fact]
    public void Generate_UnsupportedAlgorithm_Sha384_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => TotpGenerator.Generate(Sha1Seed, 59L, HashAlgorithmKind.Sha384));
    }

    [Fact]
    public void Generate_Rfc6238Sha256Vector_ReturnsExpectedCode()
    {
        var actual = TotpGenerator.Generate(Sha256Seed, 59L, HashAlgorithmKind.Sha256);

        Assert.Equal("119246", actual);
    }

    [Fact]
    public void Generate_Rfc6238Sha512Vector_ReturnsExpectedCode()
    {
        var actual = TotpGenerator.Generate(Sha512Seed, 59L, HashAlgorithmKind.Sha512);

        Assert.Equal("693936", actual);
    }

    [Fact]
    public void ElapsedInStep_AtBoundary_ReturnsZero()
    {
        Assert.Equal(0, TotpGenerator.ElapsedInStep(60L, 30));
    }

    [Fact]
    public void ElapsedInStep_MidStep_ReturnsOffset()
    {
        Assert.Equal(15, TotpGenerator.ElapsedInStep(75L, 30));
    }

    [Fact]
    public void RemainingInStep_AtBoundary_ReturnsFullStep()
    {
        Assert.Equal(30, TotpGenerator.RemainingInStep(60L, 30));
    }

    [Fact]
    public void RemainingInStep_MidStep_ReturnsInverse()
    {
        Assert.Equal(15, TotpGenerator.RemainingInStep(75L, 30));
    }

    [Fact]
    public void ElapsedInStep_InvalidStep_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TotpGenerator.ElapsedInStep(60L, 0));
    }

    [Fact]
    public void RemainingInStep_InvalidStep_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TotpGenerator.RemainingInStep(60L, 0));
    }
}
