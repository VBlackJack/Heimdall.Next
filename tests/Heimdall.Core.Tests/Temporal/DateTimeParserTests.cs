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

using Heimdall.Core.Temporal;

namespace Heimdall.Core.Tests;

public sealed class DateTimeParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrNull_ReturnsInvalid(string? input)
    {
        var outcome = DateTimeParser.Parse(input);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(DateTimeFormat.Invalid, outcome.Detected);
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData(" 1712345678 ", 1712345678L)]
    [InlineData("-62135596800", -62135596800L)]
    [InlineData("32503680000", 32503680000L)]
    public void Parse_UnixSeconds_ReturnsDetectedFormat(string input, long expectedSeconds)
    {
        var outcome = DateTimeParser.Parse(input);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(DateTimeFormat.UnixSeconds, outcome.Detected);
        Assert.Equal(expectedSeconds, outcome.Value.ToUnixTimeSeconds());
    }

    [Theory]
    [InlineData("1712345678000", 1712345678000L)]
    [InlineData("-62135596800000", -62135596800000L)]
    [InlineData("32503680000000", 32503680000000L)]
    public void Parse_UnixMilliseconds_UsesLengthAndBounds(string input, long expectedMilliseconds)
    {
        var outcome = DateTimeParser.Parse(input);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(DateTimeFormat.UnixMilliseconds, outcome.Detected);
        Assert.Equal(expectedMilliseconds, outcome.Value.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Parse_NumberAboveMaxSeconds_TreatedAsMilliseconds()
    {
        var outcome = DateTimeParser.Parse("32503680001");

        Assert.True(outcome.IsSuccess);
        Assert.Equal(DateTimeFormat.UnixMilliseconds, outcome.Detected);
    }

    [Theory]
    [InlineData("32503680000001")]
    [InlineData("-62135596800001")]
    [InlineData("999999999999999999")]
    public void Parse_OutOfRangeUnix_ReturnsInvalid(string input)
    {
        var outcome = DateTimeParser.Parse(input);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(DateTimeFormat.Invalid, outcome.Detected);
    }

    [Theory]
    [InlineData("2024-12-25T10:30:45Z", 1735122645L)]
    [InlineData("2024-12-25T10:30:45+02:00", 1735115445L)]
    public void Parse_Iso8601_ReturnsDetectedFormat(string input, long expectedUnixSeconds)
    {
        var outcome = DateTimeParser.Parse(input);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(DateTimeFormat.Iso8601, outcome.Detected);
        Assert.Equal(expectedUnixSeconds, outcome.Value.ToUnixTimeSeconds());
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2024-99-99T00:00:00Z")]
    [InlineData("2024-13-40T99:99:99Z")]
    public void Parse_InvalidInput_ReturnsInvalid(string input)
    {
        var outcome = DateTimeParser.Parse(input);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(DateTimeFormat.Invalid, outcome.Detected);
    }
}
