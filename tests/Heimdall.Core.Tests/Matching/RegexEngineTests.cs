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

using System.Text.RegularExpressions;
using Heimdall.Core.Matching;

namespace Heimdall.Core.Tests;

public sealed class RegexEngineTests
{
    [Fact]
    public void Test_NullPattern_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RegexEngine.Test(null!, string.Empty, RegexOptions.None));
    }

    [Fact]
    public void Test_NullInput_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RegexEngine.Test("a", null!, RegexOptions.None));
    }

    [Fact]
    public void Test_EmptyPattern_ReturnsEmptyPattern()
    {
        var result = RegexEngine.Test(string.Empty, "abc", RegexOptions.None);

        Assert.Equal(RegexTestStatus.EmptyPattern, result.Status);
        Assert.Equal(0, result.TotalMatchCount);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Test_InvalidPattern_ReturnsInvalidPattern()
    {
        var result = RegexEngine.Test("[", "abc", RegexOptions.None);

        Assert.Equal(RegexTestStatus.InvalidPattern, result.Status);
        Assert.NotEmpty(result.ErrorMessage);
    }

    [Fact]
    public void Test_ValidPattern_EmptyInput_ReturnsSuccessWithoutMatches()
    {
        var result = RegexEngine.Test("a", string.Empty, RegexOptions.None);

        Assert.Equal(RegexTestStatus.Success, result.Status);
        Assert.Equal(0, result.TotalMatchCount);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Test_NoMatches_ReturnsSuccess()
    {
        var result = RegexEngine.Test("z", "abc", RegexOptions.None);

        Assert.Equal(RegexTestStatus.Success, result.Status);
        Assert.Equal(0, result.TotalMatchCount);
    }

    [Fact]
    public void Test_BasicMatch_ReturnsIndexLengthAndValue()
    {
        var result = RegexEngine.Test("b+", "abbb c", RegexOptions.None);

        var match = Assert.Single(result.Matches);
        Assert.Equal(1, match.Index);
        Assert.Equal(3, match.Length);
        Assert.Equal("bbb", match.Value);
    }

    [Fact]
    public void Test_MultipleMatches_ReturnsTotalCount()
    {
        var result = RegexEngine.Test("\\d+", "a1 b22 c333", RegexOptions.None);

        Assert.Equal(3, result.TotalMatchCount);
        Assert.Equal(3, result.Matches.Count);
    }

    [Fact]
    public void Test_Groups_ExcludeGroupZero()
    {
        var result = RegexEngine.Test("(ab)(cd)", "xxabcdyy", RegexOptions.None);

        var match = Assert.Single(result.Matches);
        Assert.Equal(2, match.Groups.Count);
        Assert.Equal(1, match.Groups[0].Index);
        Assert.Equal(2, match.Groups[1].Index);
    }

    [Fact]
    public void Test_NamedGroup_IsMarkedNamed()
    {
        var result = RegexEngine.Test("(?<word>ab)c", "abc", RegexOptions.None);

        var group = Assert.Single(Assert.Single(result.Matches).Groups);
        Assert.Equal("word", group.Name);
        Assert.True(group.IsNamed);
    }

    [Fact]
    public void Test_UnnamedGroup_IsNotMarkedNamed()
    {
        var result = RegexEngine.Test("(ab)c", "abc", RegexOptions.None);

        var group = Assert.Single(Assert.Single(result.Matches).Groups);
        Assert.False(group.IsNamed);
    }

    [Fact]
    public void Test_UnsuccessfulGroup_UsesNegativeStartIndex()
    {
        var result = RegexEngine.Test("(a)?b", "b", RegexOptions.None);

        var group = Assert.Single(Assert.Single(result.Matches).Groups);
        Assert.Equal(-1, group.StartIndex);
        Assert.Equal(0, group.Length);
        Assert.Equal(string.Empty, group.Value);
    }

    [Fact]
    public void Test_IgnoreCaseOption_AffectsMatching()
    {
        var result = RegexEngine.Test("abc", "ABC", RegexOptions.IgnoreCase);

        Assert.Equal(1, result.TotalMatchCount);
    }

    [Fact]
    public void Test_WithoutIgnoreCaseOption_DoesNotMatchDifferentCase()
    {
        var result = RegexEngine.Test("abc", "ABC", RegexOptions.None);

        Assert.Equal(0, result.TotalMatchCount);
    }

    [Fact]
    public void Test_MultilineOption_AffectsAnchors()
    {
        var result = RegexEngine.Test("^abc$", "zzz\nabc\nzzz", RegexOptions.Multiline);

        Assert.Equal(1, result.TotalMatchCount);
    }

    [Fact]
    public void Test_WithoutMultilineOption_DoesNotMatchInnerLineAnchors()
    {
        var result = RegexEngine.Test("^abc$", "zzz\nabc\nzzz", RegexOptions.None);

        Assert.Equal(0, result.TotalMatchCount);
    }

    [Fact]
    public void Test_SinglelineOption_AffectsDotMatching()
    {
        var result = RegexEngine.Test("a.*c", "a\nb\nc", RegexOptions.Singleline);

        Assert.Equal(1, result.TotalMatchCount);
    }

    [Fact]
    public void Test_WithoutSinglelineOption_DoesNotCrossNewlines()
    {
        var result = RegexEngine.Test("a.*c", "a\nb\nc", RegexOptions.None);

        Assert.Equal(0, result.TotalMatchCount);
    }

    [Fact]
    public void Test_UnicodeInput_Matches()
    {
        var result = RegexEngine.Test("é+", "caféé", RegexOptions.None);

        Assert.Equal(1, result.TotalMatchCount);
        Assert.Equal("éé", Assert.Single(result.Matches).Value);
    }

    [Fact]
    public void Test_MultipleMatches_PreserveOrder()
    {
        var result = RegexEngine.Test("\\d+", "a1 b22 c333", RegexOptions.None);

        Assert.Equal("1", result.Matches[0].Value);
        Assert.Equal("22", result.Matches[1].Value);
        Assert.Equal("333", result.Matches[2].Value);
    }

    [Fact]
    public void Test_GroupValue_IsPreserved()
    {
        var result = RegexEngine.Test("(?<word>ab)c", "abc", RegexOptions.None);

        Assert.Equal("ab", Assert.Single(Assert.Single(result.Matches).Groups).Value);
    }

    [Fact]
    public void Test_Timeout_ReturnsMatchTimeout()
    {
        var input = new string('a', 20000);

        var result = RegexEngine.Test("(a+)+b", input, RegexOptions.None, TimeSpan.FromMilliseconds(1));

        Assert.Equal(RegexTestStatus.MatchTimeout, result.Status);
    }

    [Theory]
    [InlineData("a+", "aaa", 1)]
    [InlineData("\\w+", "alpha beta", 2)]
    [InlineData("(?<x>ab)", "ab ab", 2)]
    public void Test_ReturnsExpectedMatchCounts(string pattern, string input, int expectedCount)
    {
        var result = RegexEngine.Test(pattern, input, RegexOptions.None);

        Assert.Equal(expectedCount, result.TotalMatchCount);
    }
}
