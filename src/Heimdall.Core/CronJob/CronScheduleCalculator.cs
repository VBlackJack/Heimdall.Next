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
/// Calculates upcoming cron run times from classic five-field schedules.
/// </summary>
public static class CronScheduleCalculator
{
    private const int MaxIterations = 525960;

    public static IReadOnlyList<string> CalculateNextRuns(string[] fields, int count, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Length < 5 || count <= 0)
        {
            return Array.Empty<string>();
        }

        var validMinutes = ExpandField(fields[0], 0, 59, false);
        var validHours = ExpandField(fields[1], 0, 23, false);
        var validDoms = ExpandField(fields[2], 1, 31, false);
        var validMonths = ExpandField(fields[3], 1, 12, false);
        var validDows = ExpandField(fields[4], 0, 6, true);

        if (validMinutes is null || validHours is null || validDoms is null || validMonths is null || validDows is null)
        {
            return Array.Empty<string>();
        }

        var current = now.AddMinutes(1);
        current = new DateTime(
            current.Year,
            current.Month,
            current.Day,
            current.Hour,
            current.Minute,
            0,
            current.Kind);

        var results = new List<string>(count);
        var iterations = 0;
        while (results.Count < count && iterations < MaxIterations)
        {
            iterations++;

            if (validMonths.Contains(current.Month) &&
                validDoms.Contains(current.Day) &&
                validHours.Contains(current.Hour) &&
                validMinutes.Contains(current.Minute) &&
                validDows.Contains((int)current.DayOfWeek))
            {
                results.Add(current.ToString("yyyy-MM-dd HH:mm (ddd)", CultureInfo.InvariantCulture));
            }

            current = current.AddMinutes(1);
        }

        return results;
    }

    private static HashSet<int>? ExpandField(string field, int min, int max, bool sundayWrap)
    {
        var values = new HashSet<int>();

        if (field == "*")
        {
            for (var i = min; i <= max; i++)
            {
                values.Add(i);
            }

            if (sundayWrap)
            {
                values.Add(0);
            }

            return values;
        }

        foreach (var part in field.Split(','))
        {
            if (part.Contains('/'))
            {
                var stepParts = part.Split('/');
                if (stepParts.Length != 2 || !int.TryParse(stepParts[1], out var step) || step <= 0)
                {
                    return null;
                }

                var start = stepParts[0] == "*"
                    ? min
                    : int.TryParse(stepParts[0], out var s) ? s : min;

                for (var i = start; i <= max; i += step)
                {
                    AddValue(values, i, min, max, sundayWrap);
                }
            }
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length != 2 ||
                    !int.TryParse(rangeParts[0], out var rangeStart) ||
                    !int.TryParse(rangeParts[1], out var rangeEnd))
                {
                    return null;
                }

                for (var i = rangeStart; i <= rangeEnd; i++)
                {
                    AddValue(values, i, min, max, sundayWrap);
                }
            }
            else if (int.TryParse(part, out var val))
            {
                AddValue(values, val, min, max, sundayWrap);
            }
            else
            {
                return null;
            }
        }

        return values;
    }

    private static void AddValue(HashSet<int> values, int value, int min, int max, bool sundayWrap)
    {
        if (sundayWrap && value == 7)
        {
            values.Add(0);
            return;
        }

        if (value >= min && value <= max)
        {
            values.Add(value);
        }
    }
}
