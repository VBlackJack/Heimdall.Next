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

using System.Globalization;

namespace Heimdall.Core.CronJob;

/// <summary>
/// Converts cron field arrays into localized human-readable descriptions.
/// </summary>
public static class CronDescriber
{
    public static string Describe(string[] fields, Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(localize);

        if (fields.Length < 5)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                localize("ToolCronDescCustom"),
                string.Join(" ", fields));
        }

        var minute = fields[0];
        var hour = fields[1];
        var dom = fields[2];
        var month = fields[3];
        var dow = fields[4];

        if (minute == "*" && hour == "*" && dom == "*" && month == "*" && dow == "*")
        {
            return localize("ToolCronDescEveryMinute");
        }

        if (minute == "0" && hour == "*" && dom == "*" && month == "*" && dow == "*")
        {
            return localize("ToolCronDescEveryHour");
        }

        if (minute == "0" && hour == "0" && dom == "*" && month == "*" && dow == "*")
        {
            return localize("ToolCronDescEveryDay");
        }

        if (minute.StartsWith("*/", StringComparison.Ordinal) &&
            hour == "*" &&
            dom == "*" &&
            month == "*" &&
            dow == "*")
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                localize("ToolCronDescEveryNMin"),
                minute[2..]);
        }

        if (int.TryParse(minute, out var m) &&
            int.TryParse(hour, out var h) &&
            dom == "*" &&
            month == "*" &&
            dow == "*")
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                localize("ToolCronDescDailyAt"),
                $"{h:D2}:{m:D2}");
        }

        if (int.TryParse(minute, out m) &&
            int.TryParse(hour, out h) &&
            dom == "*" &&
            month == "*" &&
            dow != "*")
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                localize("ToolCronDescWeeklyAt"),
                GetDayName(dow, localize),
                $"{h:D2}:{m:D2}");
        }

        if (int.TryParse(minute, out m) &&
            int.TryParse(hour, out h) &&
            int.TryParse(dom, out var d) &&
            month == "*" &&
            dow == "*")
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                localize("ToolCronDescMonthlyAt"),
                d,
                $"{h:D2}:{m:D2}");
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            localize("ToolCronDescCustom"),
            string.Join(" ", fields));
    }

    public static string GetDayName(string field, Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        string[] keys =
        [
            "ToolCronDaySunday",
            "ToolCronDayMonday",
            "ToolCronDayTuesday",
            "ToolCronDayWednesday",
            "ToolCronDayThursday",
            "ToolCronDayFriday",
            "ToolCronDaySaturday",
        ];

        if (int.TryParse(field, out var idx) && idx >= 0 && idx < 7)
        {
            return localize(keys[idx]);
        }

        return field;
    }
}
