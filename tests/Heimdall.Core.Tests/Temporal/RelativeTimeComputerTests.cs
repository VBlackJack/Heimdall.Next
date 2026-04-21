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

public sealed class RelativeTimeComputerTests
{
    private static readonly DateTimeOffset ReferenceNow = new(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-30, RelativeTimeUnit.Seconds, 30, true)]
    [InlineData(30, RelativeTimeUnit.Seconds, 30, false)]
    [InlineData(-120, RelativeTimeUnit.Minutes, 2, true)]
    [InlineData(120, RelativeTimeUnit.Minutes, 2, false)]
    [InlineData(-10800, RelativeTimeUnit.Hours, 3, true)]
    [InlineData(10800, RelativeTimeUnit.Hours, 3, false)]
    [InlineData(-432000, RelativeTimeUnit.Days, 5, true)]
    [InlineData(432000, RelativeTimeUnit.Days, 5, false)]
    public void Compute_SubDayAndDayBuckets_ReturnExpectedUnit(long offsetSeconds, RelativeTimeUnit expectedUnit, int expectedValue, bool expectedPast)
    {
        var input = ReferenceNow.AddSeconds(offsetSeconds);

        var result = RelativeTimeComputer.Compute(input, ReferenceNow);

        Assert.Equal(expectedUnit, result.Unit);
        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedPast, result.IsPast);
    }

    [Fact]
    public void Compute_MonthBucket_UsesThirtyDayApproximation()
    {
        var input = ReferenceNow.AddDays(-90);

        var result = RelativeTimeComputer.Compute(input, ReferenceNow);

        Assert.Equal(RelativeTimeUnit.Months, result.Unit);
        Assert.Equal(3, result.Value);
        Assert.True(result.IsPast);
    }

    [Fact]
    public void Compute_YearBucket_UsesThreeHundredSixtyFiveDayApproximation()
    {
        var input = ReferenceNow.AddDays(730);

        var result = RelativeTimeComputer.Compute(input, ReferenceNow);

        Assert.Equal(RelativeTimeUnit.Years, result.Unit);
        Assert.Equal(2, result.Value);
        Assert.False(result.IsPast);
    }

    [Fact]
    public void Compute_ZeroDifference_UsesSecondsPast()
    {
        var result = RelativeTimeComputer.Compute(ReferenceNow, ReferenceNow);

        Assert.Equal(RelativeTimeUnit.Seconds, result.Unit);
        Assert.Equal(0, result.Value);
        Assert.True(result.IsPast);
    }
}
