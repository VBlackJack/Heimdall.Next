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

namespace Heimdall.Core.Temporal;

public enum DateTimeFormat
{
    UnixSeconds,
    UnixMilliseconds,
    Iso8601,
    Invalid,
}

public readonly record struct DateTimeParseOutcome(bool IsSuccess, DateTimeOffset Value, DateTimeFormat Detected);

public static class DateTimeParser
{
    public const long MinUnixSeconds = -62_135_596_800L;
    public const long MaxUnixSeconds = 32_503_680_000L;
    public const long MinUnixMilliseconds = MinUnixSeconds * 1000L;
    public const long MaxUnixMilliseconds = MaxUnixSeconds * 1000L;

    public static DateTimeParseOutcome Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Failure();
        }

        var trimmed = input.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericValue))
        {
            return ParseUnix(trimmed, numericValue);
        }

        return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeParseOutcome(true, parsed, DateTimeFormat.Iso8601)
            : Failure();
    }

    private static DateTimeParseOutcome ParseUnix(string input, long value)
    {
        var isMilliseconds = input.Length >= 13 || value > MaxUnixSeconds;
        if (isMilliseconds)
        {
            return value < MinUnixMilliseconds || value > MaxUnixMilliseconds
                ? Failure()
                : new DateTimeParseOutcome(true, DateTimeOffset.FromUnixTimeMilliseconds(value), DateTimeFormat.UnixMilliseconds);
        }

        return value < MinUnixSeconds || value > MaxUnixSeconds
            ? Failure()
            : new DateTimeParseOutcome(true, DateTimeOffset.FromUnixTimeSeconds(value), DateTimeFormat.UnixSeconds);
    }

    private static DateTimeParseOutcome Failure() => new(false, default, DateTimeFormat.Invalid);
}
