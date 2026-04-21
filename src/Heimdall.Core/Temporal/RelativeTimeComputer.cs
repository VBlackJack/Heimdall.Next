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

namespace Heimdall.Core.Temporal;

public enum RelativeTimeUnit
{
    Seconds,
    Minutes,
    Hours,
    Days,
    Months,
    Years,
}

public readonly record struct RelativeDuration(RelativeTimeUnit Unit, int Value, bool IsPast);

public static class RelativeTimeComputer
{
    public static RelativeDuration Compute(DateTimeOffset input, DateTimeOffset now)
    {
        var difference = now - input;
        var isPast = difference.TotalSeconds >= 0;
        var absoluteDifference = isPast ? difference : difference.Negate();

        if (absoluteDifference.TotalSeconds < 60)
        {
            return new RelativeDuration(RelativeTimeUnit.Seconds, (int)absoluteDifference.TotalSeconds, isPast);
        }

        if (absoluteDifference.TotalMinutes < 60)
        {
            return new RelativeDuration(RelativeTimeUnit.Minutes, (int)absoluteDifference.TotalMinutes, isPast);
        }

        if (absoluteDifference.TotalHours < 24)
        {
            return new RelativeDuration(RelativeTimeUnit.Hours, (int)absoluteDifference.TotalHours, isPast);
        }

        if (absoluteDifference.TotalDays < 30)
        {
            return new RelativeDuration(RelativeTimeUnit.Days, (int)absoluteDifference.TotalDays, isPast);
        }

        // Legacy approximation: months are 30 days and years are 365 days.
        if (absoluteDifference.TotalDays < 365)
        {
            return new RelativeDuration(RelativeTimeUnit.Months, (int)(absoluteDifference.TotalDays / 30), isPast);
        }

        return new RelativeDuration(RelativeTimeUnit.Years, (int)(absoluteDifference.TotalDays / 365), isPast);
    }
}
