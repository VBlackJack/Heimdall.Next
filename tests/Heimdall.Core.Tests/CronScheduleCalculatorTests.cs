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

using Heimdall.Core.CronJob;

namespace Heimdall.Core.Tests;

public sealed class CronScheduleCalculatorTests
{
    private static readonly DateTime Anchor = new(2026, 4, 18, 10, 0, 0, DateTimeKind.Local);

    [Fact]
    public void CalculateNextRuns_CountLessThanOne_ReturnsEmpty()
    {
        Assert.Empty(CronScheduleCalculator.CalculateNextRuns(["*", "*", "*", "*", "*"], 0, Anchor));
    }

    [Fact]
    public void CalculateNextRuns_TooFewFields_ReturnsEmpty()
    {
        Assert.Empty(CronScheduleCalculator.CalculateNextRuns(["*", "*", "*"], 1, Anchor));
    }

    [Fact]
    public void CalculateNextRuns_EveryMinute_StartsOnNextMinute()
    {
        var runs = CronScheduleCalculator.CalculateNextRuns(["*", "*", "*", "*", "*"], 2, Anchor);

        Assert.Equal(["2026-04-18 10:01 (Sat)", "2026-04-18 10:02 (Sat)"], runs);
    }

    [Fact]
    public void CalculateNextRuns_DailyAtSpecificTime_Works()
    {
        var runs = CronScheduleCalculator.CalculateNextRuns(["5", "12", "*", "*", "*"], 2, Anchor);

        Assert.Equal(["2026-04-18 12:05 (Sat)", "2026-04-19 12:05 (Sun)"], runs);
    }

    [Fact]
    public void CalculateNextRuns_StepPattern_Works()
    {
        var runs = CronScheduleCalculator.CalculateNextRuns(["*/15", "*", "*", "*", "*"], 3, Anchor);

        Assert.Equal(["2026-04-18 10:15 (Sat)", "2026-04-18 10:30 (Sat)", "2026-04-18 10:45 (Sat)"], runs);
    }

    [Fact]
    public void CalculateNextRuns_RangePattern_Works()
    {
        var runs = CronScheduleCalculator.CalculateNextRuns(["0", "9-11", "*", "*", "*"], 3, Anchor);

        Assert.Equal(["2026-04-18 11:00 (Sat)", "2026-04-19 09:00 (Sun)", "2026-04-19 10:00 (Sun)"], runs);
    }

    [Fact]
    public void CalculateNextRuns_SundaySeven_WrapsToZero()
    {
        var saturday = new DateTime(2026, 4, 18, 8, 0, 0, DateTimeKind.Local);
        var runs = CronScheduleCalculator.CalculateNextRuns(["0", "9", "*", "*", "7"], 1, saturday);

        Assert.Single(runs);
        Assert.Equal("2026-04-19 09:00 (Sun)", runs[0]);
    }

    [Fact]
    public void CalculateNextRuns_InvalidField_ReturnsEmpty()
    {
        Assert.Empty(CronScheduleCalculator.CalculateNextRuns(["abc", "*", "*", "*", "*"], 1, Anchor));
    }
}
