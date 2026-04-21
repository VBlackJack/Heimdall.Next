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

using Heimdall.App.Services;
using Heimdall.Core.Matching;

namespace Heimdall.App.Tests;

public sealed class RegexTesterToolServiceTests
{
    private readonly RegexTesterToolService _service = new();

    [Fact]
    public void Test_IgnoreCaseFlag_EnablesCaseInsensitiveMatch()
    {
        var result = _service.Test("abc", "ABC", ignoreCase: true, multiline: false, singleline: false);

        Assert.Equal(RegexTestStatus.Success, result.Status);
        Assert.Equal(1, result.TotalMatchCount);
    }

    [Fact]
    public void Test_IgnoreCaseFlag_Off_DoesNotMatchDifferentCase()
    {
        var result = _service.Test("abc", "ABC", ignoreCase: false, multiline: false, singleline: false);

        Assert.Equal(0, result.TotalMatchCount);
    }

    [Fact]
    public void Test_MultilineFlag_EnablesLineAnchors()
    {
        var result = _service.Test("^abc$", "zzz\nabc\nzzz", ignoreCase: false, multiline: true, singleline: false);

        Assert.Equal(1, result.TotalMatchCount);
    }

    [Fact]
    public void Test_SinglelineFlag_EnablesDotAll()
    {
        var result = _service.Test("a.*c", "a\nb\nc", ignoreCase: false, multiline: false, singleline: true);

        Assert.Equal(1, result.TotalMatchCount);
    }

    [Fact]
    public void Test_SinglelineFlag_Off_DoesNotCrossNewlines()
    {
        var result = _service.Test("a.*c", "a\nb\nc", ignoreCase: false, multiline: false, singleline: false);

        Assert.Equal(0, result.TotalMatchCount);
    }

    [Fact]
    public void Test_InvalidPattern_PropagatesInvalidStatus()
    {
        var result = _service.Test("[", "abc", ignoreCase: false, multiline: false, singleline: false);

        Assert.Equal(RegexTestStatus.InvalidPattern, result.Status);
    }

    [Fact]
    public void Test_EmptyPattern_PropagatesEmptyPattern()
    {
        var result = _service.Test(string.Empty, "abc", ignoreCase: false, multiline: false, singleline: false);

        Assert.Equal(RegexTestStatus.EmptyPattern, result.Status);
    }
}
