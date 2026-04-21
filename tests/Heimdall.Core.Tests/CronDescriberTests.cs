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

public sealed class CronDescriberTests
{
    private static readonly Dictionary<string, string> Strings = new()
    {
        ["ToolCronDescEveryMinute"] = "Every minute",
        ["ToolCronDescEveryHour"] = "Every hour",
        ["ToolCronDescEveryDay"] = "Every day",
        ["ToolCronDescEveryNMin"] = "Every {0} min",
        ["ToolCronDescDailyAt"] = "Daily at {0}",
        ["ToolCronDescWeeklyAt"] = "Weekly on {0} at {1}",
        ["ToolCronDescMonthlyAt"] = "Monthly on {0} at {1}",
        ["ToolCronDescCustom"] = "Custom: {0}",
        ["ToolCronDaySunday"] = "Sunday",
        ["ToolCronDayMonday"] = "Monday",
        ["ToolCronDayTuesday"] = "Tuesday",
        ["ToolCronDayWednesday"] = "Wednesday",
        ["ToolCronDayThursday"] = "Thursday",
        ["ToolCronDayFriday"] = "Friday",
        ["ToolCronDaySaturday"] = "Saturday",
    };

    [Fact]
    public void Describe_EveryMinute_ReturnsLocalizedText()
    {
        var result = CronDescriber.Describe(["*", "*", "*", "*", "*"], Localize);

        Assert.Equal("Every minute", result);
    }

    [Fact]
    public void Describe_EveryNMinutes_ReturnsFormattedText()
    {
        var result = CronDescriber.Describe(["*/15", "*", "*", "*", "*"], Localize);

        Assert.Equal("Every 15 min", result);
    }

    [Fact]
    public void Describe_DailyAt_ReturnsFormattedText()
    {
        var result = CronDescriber.Describe(["5", "3", "*", "*", "*"], Localize);

        Assert.Equal("Daily at 03:05", result);
    }

    [Fact]
    public void Describe_WeeklyAt_UsesDayName()
    {
        var result = CronDescriber.Describe(["30", "8", "*", "*", "1"], Localize);

        Assert.Equal("Weekly on Monday at 08:30", result);
    }

    [Fact]
    public void Describe_MonthlyAt_ReturnsFormattedText()
    {
        var result = CronDescriber.Describe(["0", "6", "15", "*", "*"], Localize);

        Assert.Equal("Monthly on 15 at 06:00", result);
    }

    [Fact]
    public void Describe_CustomFallback_PreservesFields()
    {
        var result = CronDescriber.Describe(["1", "2", "3"], Localize);

        Assert.Equal("Custom: 1 2 3", result);
    }

    [Theory]
    [InlineData("0", "Sunday")]
    [InlineData("3", "Wednesday")]
    [InlineData("6", "Saturday")]
    [InlineData("mon", "mon")]
    public void GetDayName_Works(string input, string expected)
    {
        Assert.Equal(expected, CronDescriber.GetDayName(input, Localize));
    }

    private static string Localize(string key) => Strings.TryGetValue(key, out var value) ? value : key;
}
