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
using Heimdall.Core.Temporal;

namespace Heimdall.App.Tests;

public sealed class DateTimeConverterToolServiceTests
{
    private readonly DateTimeConverterToolService _service = new();

    [Fact]
    public void Parse_DelegatesToCoreParser()
    {
        var outcome = _service.Parse("1712345678");

        Assert.True(outcome.IsSuccess);
        Assert.Equal(DateTimeFormat.UnixSeconds, outcome.Detected);
    }

    [Fact]
    public void ComputeRelative_DelegatesToCoreComputer()
    {
        var now = new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

        var result = _service.ComputeRelative(now.AddMinutes(-5), now);

        Assert.Equal(RelativeTimeUnit.Minutes, result.Unit);
        Assert.Equal(5, result.Value);
        Assert.True(result.IsPast);
    }

    [Fact]
    public void Parse_InvalidInput_ReturnsInvalidOutcome()
    {
        var outcome = _service.Parse("nope");

        Assert.False(outcome.IsSuccess);
        Assert.Equal(DateTimeFormat.Invalid, outcome.Detected);
    }
}
