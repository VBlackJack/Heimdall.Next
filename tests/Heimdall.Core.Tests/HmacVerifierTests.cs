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

public sealed class HmacVerifierTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_NullOrWhitespaceCandidate_ReturnsNoMatch(string? candidate)
    {
        var bytes = Encoding.UTF8.GetBytes("abc");

        var actual = HmacVerifier.Verify(bytes, candidate!);

        Assert.False(actual.Matched);
        Assert.Null(actual.MatchedFormat);
    }

    [Fact]
    public void Verify_ExactHexMatch_ReturnsHex()
    {
        var bytes = Encoding.UTF8.GetBytes("abc");
        var candidate = HmacComputer.Format(bytes, HmacOutputFormat.Hex);

        var actual = HmacVerifier.Verify(bytes, candidate);

        Assert.True(actual.Matched);
        Assert.Equal(HmacOutputFormat.Hex, actual.MatchedFormat);
    }

    [Fact]
    public void Verify_HexMatch_IgnoresCase()
    {
        var bytes = Encoding.UTF8.GetBytes("abc");
        var candidate = HmacComputer.Format(bytes, HmacOutputFormat.Hex).ToUpperInvariant();

        var actual = HmacVerifier.Verify(bytes, candidate);

        Assert.True(actual.Matched);
        Assert.Equal(HmacOutputFormat.Hex, actual.MatchedFormat);
    }

    [Fact]
    public void Verify_ExactBase64Match_ReturnsBase64()
    {
        var bytes = HmacComputer.Compute(HashAlgorithmKind.Sha256, Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("message"));
        var candidate = HmacComputer.Format(bytes, HmacOutputFormat.Base64);

        var actual = HmacVerifier.Verify(bytes, candidate);

        Assert.True(actual.Matched);
        Assert.Equal(HmacOutputFormat.Base64, actual.MatchedFormat);
    }

    [Fact]
    public void Verify_Base64Match_IgnoresCase()
    {
        var bytes = HmacComputer.Compute(HashAlgorithmKind.Sha256, Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("message"));
        var candidateChars = HmacComputer.Format(bytes, HmacOutputFormat.Base64).ToCharArray();
        var index = Array.FindIndex(candidateChars, char.IsLetter);
        candidateChars[index] = char.IsUpper(candidateChars[index])
            ? char.ToLowerInvariant(candidateChars[index])
            : char.ToUpperInvariant(candidateChars[index]);
        var candidate = new string(candidateChars);

        var actual = HmacVerifier.Verify(bytes, candidate);

        Assert.True(actual.Matched);
        Assert.Equal(HmacOutputFormat.Base64, actual.MatchedFormat);
    }

    [Fact]
    public void Verify_WithLeadingAndTrailingWhitespace_StillMatches()
    {
        var bytes = Encoding.UTF8.GetBytes("abc");
        var candidate = $"  {HmacComputer.Format(bytes, HmacOutputFormat.Hex)}  ";

        var actual = HmacVerifier.Verify(bytes, candidate);

        Assert.True(actual.Matched);
        Assert.Equal(HmacOutputFormat.Hex, actual.MatchedFormat);
    }

    [Fact]
    public void Verify_NoMatch_ReturnsFalse()
    {
        var bytes = Encoding.UTF8.GetBytes("abc");

        var actual = HmacVerifier.Verify(bytes, "not-a-match");

        Assert.False(actual.Matched);
        Assert.Null(actual.MatchedFormat);
    }

    [Fact]
    public void Verify_EmptyBytes_WithAnyCandidate_ReturnsNoMatch()
    {
        var actual = HmacVerifier.Verify([], "abc");

        Assert.False(actual.Matched);
        Assert.Null(actual.MatchedFormat);
    }

    [Fact]
    public void Verify_NullBytes_Throws()
    {
        byte[]? bytes = null;

        Assert.Throws<ArgumentNullException>(() => HmacVerifier.Verify(bytes!, "abc"));
    }
}
