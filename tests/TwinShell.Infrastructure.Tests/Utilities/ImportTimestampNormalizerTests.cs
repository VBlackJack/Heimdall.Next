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

using FluentAssertions;
using TwinShell.Core.Utilities;

namespace TwinShell.Infrastructure.Tests;

public sealed class ImportTimestampNormalizerTests
{
    [Fact]
    public void ClampFutureToNow_ClampsFarFutureTimestamp()
    {
        DateTime nowUtc = new(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);
        TimeSpan skew = ImportTimestampNormalizer.DefaultFutureSkew;
        DateTime candidate = nowUtc.AddYears(50);

        DateTime actual = ImportTimestampNormalizer.ClampFutureToNow(candidate, nowUtc, skew, out bool wasClamped);

        actual.Should().Be(nowUtc);
        wasClamped.Should().BeTrue();
    }

    [Fact]
    public void ClampFutureToNow_PreservesWithinSkewFutureTimestamp()
    {
        DateTime nowUtc = new(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);
        TimeSpan skew = ImportTimestampNormalizer.DefaultFutureSkew;
        DateTime candidate = nowUtc.AddMinutes(4);

        DateTime actual = ImportTimestampNormalizer.ClampFutureToNow(candidate, nowUtc, skew, out bool wasClamped);

        actual.Should().Be(candidate);
        wasClamped.Should().BeFalse();
    }

    [Fact]
    public void ClampFutureToNow_PreservesPastTimestamp()
    {
        DateTime nowUtc = new(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);
        TimeSpan skew = ImportTimestampNormalizer.DefaultFutureSkew;
        DateTime candidate = nowUtc.AddDays(-1);

        DateTime actual = ImportTimestampNormalizer.ClampFutureToNow(candidate, nowUtc, skew, out bool wasClamped);

        actual.Should().Be(candidate);
        wasClamped.Should().BeFalse();
    }

    [Fact]
    public void ClampFutureToNow_PreservesUpperBoundaryTimestamp()
    {
        DateTime nowUtc = new(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);
        TimeSpan skew = ImportTimestampNormalizer.DefaultFutureSkew;
        DateTime candidate = nowUtc + skew;

        DateTime actual = ImportTimestampNormalizer.ClampFutureToNow(candidate, nowUtc, skew, out bool wasClamped);

        actual.Should().Be(candidate);
        wasClamped.Should().BeFalse();
    }
}
